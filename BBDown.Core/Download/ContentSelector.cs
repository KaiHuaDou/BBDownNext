using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace BBDown.Core.Download;

/// <summary>
/// 下载内容标记，与 --get / --with / --without 的内容字符一一对应。
/// 命令行按「get ∪ with − without」解析为规范化标志集后存入 <see cref="DownloadRequest.Content"/>，
/// 消费点用 <see cref="Has"/> / <see cref="HasAny"/> 查询。
/// </summary>
[Flags]
public enum DownloadContent
{
    None = 0,
    Audio = 1 << 0,        // a
    Video = 1 << 1,        // v
    Cover = 1 << 2,        // c：单独下载一个封面图像文件
    MuxCover = 1 << 3,     // C：封面混流进视频文件
    Danmaku = 1 << 4,      // d
    OpusImage = 1 << 5,    // i：专栏图片
    MuxMetadata = 1 << 6,  // m
    FrontMatter = 1 << 7,  // M：专栏 YAML front matter
    Comments = 1 << 8,     // o
    FullComments = 1 << 9, // O：全部评论
    AiSubtitle = 1 << 10,  // S
    Subtitle = 1 << 11,    // s
}

/// <summary>内容字符的适用域：字符落在模式域之外时自然失效（debug 提示，不警告）。</summary>
public enum ContentMode
{
    Video,
    Opus,
    Live,
    Audio,

    /// <summary>空间动态：图文导出与视频下载混合，全部内容字符都可能生效</summary>
    Mixed,
}

/// <summary>内容字符条目：字符、对应标志与中文名。GUI 内容复选框与警告文案共用此表，避免字符集合多处硬编码。</summary>
public readonly record struct ContentItem(char Ch, DownloadContent Flag, string Name);

public static class ContentSelector
{
    public const string Default = "avmsCiM";

    /// <summary>字符的唯一规范顺序，同时用于输出、互转、警告文案与 GUI 内容选项；顺序与 CLI 帮助、GUI 面板布局保持一致。</summary>
    public static IReadOnlyList<ContentItem> Order { get; } =
    [
        new('a', DownloadContent.Audio, "音频"),
        new('v', DownloadContent.Video, "视频"),
        new('c', DownloadContent.Cover, "独立封面"),
        new('C', DownloadContent.MuxCover, "封面嵌入"),
        new('d', DownloadContent.Danmaku, "弹幕"),
        new('i', DownloadContent.OpusImage, "专栏图片"),
        new('m', DownloadContent.MuxMetadata, "嵌入元数据"),
        new('M', DownloadContent.FrontMatter, "YAML front matter"),
        new('o', DownloadContent.Comments, "评论"),
        new('O', DownloadContent.FullComments, "全部评论"),
        new('S', DownloadContent.AiSubtitle, "AI 字幕"),
        new('s', DownloadContent.Subtitle, "字幕"),
    ];

    private static readonly string ValidChars = string.Concat(Order.Select(e => e.Ch));

    /// <summary>默认内容集 a v m s C i M（opus 模式下仅 i / M 生效）。</summary>
    public static DownloadContent DefaultFlags { get; } = Resolve([Default], [], [], false, false, false, false, out _);

    /// <summary>
    /// get ∪ with − without。仅「用户显式写错」产出警告；
    /// 依赖自然失效（C/m 无 a/v、配套选项无对应字符）同样警告，模式失效由 <see cref="DescribeInactive"/> 走 debug。
    /// </summary>
    public static DownloadContent Resolve(
        IEnumerable<string> get,
        IEnumerable<string> with,
        IEnumerable<string> without,
        bool commentCountExplicit,
        bool commentSortExplicit,
        bool commentFormatsExplicit,
        bool danmakuFormatsExplicit,
        out List<string> warnings)
    {
        warnings = [];
        var flags = Apply(DownloadContent.None, get, subtract: false, warnings);
        flags = Apply(flags, with, subtract: false, warnings);
        flags = Apply(flags, without, subtract: true, warnings);

        // 同时使用 o / O 按 O 处理，不警告
        if (flags.Has(DownloadContent.Comments) && flags.Has(DownloadContent.FullComments))
        {
            flags &= ~DownloadContent.Comments;
        }

        if (!flags.HasAny(DownloadContent.Audio | DownloadContent.Video)
            && flags.HasAny(DownloadContent.MuxCover | DownloadContent.MuxMetadata))
        {
            warnings.Add("未选择音频或视频，封面嵌入（C）与嵌入元数据（m）不生效");
        }

        if ((commentCountExplicit || commentSortExplicit || commentFormatsExplicit)
            && !flags.HasAny(DownloadContent.Comments | DownloadContent.FullComments))
        {
            warnings.Add("已设置评论选项，但内容中未包含 o / O，评论不会下载");
        }

        if (danmakuFormatsExplicit && !flags.Has(DownloadContent.Danmaku))
        {
            warnings.Add("已设置 --danmaku-formats，但内容中未包含 d，弹幕不会下载");
        }

        return flags;
    }

    /// <summary>各模式下自然失效的内容标记 → debug 文案列表（不警告）。</summary>
    public static List<string> DescribeInactive(DownloadContent content, ContentMode mode)
    {
        var active = mode switch
        {
            ContentMode.Opus => DownloadContent.OpusImage | DownloadContent.FrontMatter,
            ContentMode.Live => DownloadContent.Audio | DownloadContent.Video,
            ContentMode.Audio => DownloadContent.Audio,
            // 图文项用 i / M、视频项用其余字符，混合域内不存在自然失效的标志
            ContentMode.Mixed => ~DownloadContent.None,
            _ => ~(DownloadContent.OpusImage | DownloadContent.FrontMatter),
        };
        var list = new List<string>( );
        foreach (var (Ch, Flag, Name) in Order)
        {
            if (content.Has(Flag) && (Flag & active) == 0)
            {
                list.Add($"{Name}（{Ch}）在{ModeName(mode)}模式下不生效");
            }
        }

        return list;
    }

    /// <summary>按规范顺序输出内容集，serve 契约用字符串形式。</summary>
    internal static string ToNormalizedString(DownloadContent content)
    {
        var builder = new StringBuilder( );
        foreach (var (Ch, Flag, Name) in Order)
        {
            if (content.Has(Flag))
            {
                builder.Append(Ch);
            }
        }

        return builder.ToString( );
    }

    /// <summary>解析 serve 传入的内容集字符串，非法字符忽略。</summary>
    public static DownloadContent FromNormalizedString(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return DownloadContent.None;
        }

        var flags = DownloadContent.None;
        foreach (var ch in value)
        {
            foreach (var (Ch, Flag, Name) in Order)
            {
                if (Ch == ch)
                {
                    flags |= Flag;
                    break;
                }
            }
        }

        return flags;
    }

    internal static bool Has(this DownloadContent content, DownloadContent flag)
    {
        return (content & flag) == flag;
    }

    internal static bool HasAny(this DownloadContent content, DownloadContent flags)
    {
        return (content & flags) != 0;
    }

    private static DownloadContent Apply(DownloadContent flags, IEnumerable<string> segments, bool subtract, List<string> warnings)
    {
        foreach (var segment in segments)
        {
            foreach (var ch in segment)
            {
                var found = false;
                foreach (var (Ch, Flag, Name) in Order)
                {
                    if (Ch != ch)
                    {
                        continue;
                    }

                    flags = subtract ? flags & ~Flag : flags | Flag;
                    found = true;
                    break;
                }

                if (!found)
                {
                    warnings.Add($"无效的内容字符「{ch}」（有效字符：{ValidChars}）");
                }
            }
        }

        return flags;
    }

    /// <summary>
    /// 由资源类型推导内容适用域：CLI 与 serve 共用的唯一判定点，
    /// 各执行域（视频管道 / 专栏 / 直播 / 音频）据此提示不生效的内容标志。
    /// </summary>
    public static ContentMode ModeOf(ResourceId id)
    {
        return id switch
        {
            ResourceId.LiveRoom => ContentMode.Live,
            ResourceId.OpusArticle or ResourceId.ReadList or ResourceId.SpaceOpus => ContentMode.Opus,
            ResourceId.SpaceAudio or ResourceId.Audio => ContentMode.Audio,
            ResourceId.SpaceDynamic => ContentMode.Mixed,
            _ => ContentMode.Video,
        };
    }

    private static string ModeName(ContentMode mode)
    {
        return mode switch
        {
            ContentMode.Opus => "专栏导出",
            ContentMode.Live => "直播录制",
            ContentMode.Audio => "音频下载",
            ContentMode.Mixed => "空间动态（图文 + 视频）",
            _ => "视频下载",
        };
    }
}
