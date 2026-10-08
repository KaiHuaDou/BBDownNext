using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using BBDown.Core.Entity;
using BBDown.Core.Util;

using static BBDown.Core.Logger;
using static BBDown.Core.Util.FileNameUtil;

namespace BBDown.Core.Download;

/// <summary>
/// 文件命名变量：Token 为尖括号占位符本体，Description 供 GUI 变量表与 CLI help 共用
/// </summary>
public sealed record NamingVariable(string Token, string Description);

public static partial class SavePath
{
    public static string SinglePageDefaultSavePath { get; } = "<videoTitle>";
    public static string MultiPageDefaultSavePath { get; } = "<videoTitle>/[P<pageNumberWithZero>]<pageTitle>";

    // 与 Format 中 switch 的替换键一一对应，SavePathTests 钉住两者同步
    public static readonly NamingVariable[] Variables =
    [
        new("<videoTitle>", "视频主标题"),
        new("<pageNumber>", "分 P 序号，不补零"),
        new("<pageNumberWithZero>", "分 P 序号，按总 P 数位数补零"),
        new("<pageTitle>", "分 P 标题"),
        new("<bvid>", "视频 BV 号"),
        new("<aid>", "视频 aid"),
        new("<cid>", "视频 cid"),
        new("<dfn>", "所选清晰度名称，如 1080P 高清"),
        new("<res>", "所选分辨率，如 1920x1080"),
        new("<fps>", "所选帧率"),
        new("<videoCodecs>", "所选视频编码，如 HEVC"),
        new("<videoBandwidth>", "所选视频码率（bps）"),
        new("<audioCodecs>", "所选音频编码，如 M4A"),
        new("<audioBandwidth>", "所选音频码率（bps）"),
        new("<ownerName>", "UP 主名称"),
        new("<ownerMid>", "UP 主 mid"),
        new("<publishDate>", "收藏夹 / 番剧 / 合集发布时间，默认 yyyy-MM-dd_HH-mm-ss，可写 <publishDate:格式> 自定义"),
        new("<videoDate>", "视频发布时间（分 P 视频发布时间与 <publishDate> 相同），自定义格式同上"),
        new("<apiType>", "API 类型（WEB / TV / APP / INTL）"),
    ];

    // 1. 多 P; 2. 只有 1P, 但是是番剧，尚未完结时 按照多 P 处理
    internal static string Resolve(DownloadRequest myOption, int pagesCount, bool isBangumi, bool isBangumiEnd)
    {
        return pagesCount > 1 || (isBangumi && !isBangumiEnd)
            ? (string.IsNullOrEmpty(myOption.MultiFilePattern) ? MultiPageDefaultSavePath : myOption.MultiFilePattern)
            : (string.IsNullOrEmpty(myOption.FilePattern) ? SinglePageDefaultSavePath : myOption.FilePattern);
    }

    internal static string Build(WorkContext ctx, PageContext pageCtx, Video? videoTrack, Audio? audioTrack)
    {
        var relative = Format(ctx.SavePathFormat, pageCtx.Title, videoTrack, audioTrack, pageCtx.Page, pageCtx.PagesCount, ctx.Fetch.ApiType, pageCtx.PubTime);
        if (pageCtx.IsPreview)
        {
            relative = ApplyPreviewPrefix(relative);
        }

        return Path.Combine(ctx.Run.WorkDir, relative);
    }

    // 多 P 模板形如 <videoTitle>/[P01]<pageTitle>，前缀只能加到最后一段，否则会造出带前缀的目录
    internal static string ApplyPreviewPrefix(string relative)
    {
        var i = relative.LastIndexOfAny(['/', '\\']);
        return i < 0 ? "[试看]" + relative : relative[..(i + 1)] + "[试看]" + relative[(i + 1)..];
    }

    internal static string Format(string savePathFormat, string title, Video? videoTrack, Audio? audioTrack, Page p, int pagesCount, ApiType apiType, long pubTime)
    {
        var result = savePathFormat.Replace('\\', '/');
        var regex = InfoRegex( );
        var matches = regex.Matches(result).Cast<Match>( ).ToList( );
        var replacements = new List<(int Index, int Length, string Value)>(matches.Count);
        foreach (var m in matches)
        {
            var key = m.Groups[1].Value;

            //解析自定义日期格式
            var defaultDateFormat = "yyyy-MM-dd_HH-mm-ss";
            string[] prefixes = ["publishDate:", "videoDate:"];
            foreach (var prefix in prefixes)
            {
                if (key.StartsWith(prefix))
                {
                    defaultDateFormat = key[(key.IndexOf(':') + 1)..];
                    key = prefix.Replace(":", "");
                    break;
                }
            }

            var v = key switch
            {
                "videoTitle" => GetValidFileName(title),
                "pageNumber" => p.Index.ToString( ),
                "pageNumberWithZero" => p.Index.ToString( ).PadLeft(pagesCount.ToString( ).Length, '0'),
                "pageTitle" => GetValidFileName(p.Title),
                "bvid" => p.Bvid,
                "aid" => p.Aid,
                "cid" => p.Cid,
                "ownerName" => p.OwnerName == null ? "" : GetValidFileName(p.OwnerName),
                "ownerMid" => GetValidFileName(p.OwnerMid ?? ""),
                // 清晰度 / 分辨率 / 帧率 / 编码逐字来自 playurl 响应（对端可控），是路径组成段
                // 与标题同构过 GetValidFileName，镜像站下发含分隔符或 .. 的值无法穿越工作目录
                "dfn" => videoTrack == null ? "" : GetValidFileName(videoTrack.Dfn),
                "res" => videoTrack == null ? "" : GetValidFileName(videoTrack.Res ?? ""),
                "fps" => videoTrack == null ? "" : GetValidFileName(videoTrack.Fps ?? ""),
                "videoCodecs" => videoTrack == null ? "" : GetValidFileName(videoTrack.Codecs),
                "videoBandwidth" => videoTrack == null ? "" : videoTrack.Bandwidth.ToString( ),
                "audioCodecs" => audioTrack == null ? "" : GetValidFileName(audioTrack.Codecs),
                "audioBandwidth" => audioTrack == null ? "" : audioTrack.Bandwidth.ToString( ),
                "publishDate" => GetValidFileName(Utils.FormatTimeStamp(pubTime, defaultDateFormat)),
                "videoDate" => GetValidFileName(Utils.FormatTimeStamp(p.PubTime, defaultDateFormat)),
                "apiType" => apiType.ToString( ).ToUpperInvariant( ),
                _ => UnknownPlaceholder(key)
            };
            replacements.Add((m.Index, m.Length, v ?? ""));
        }

        for (var i = replacements.Count - 1; i >= 0; i--)
        {
            var (index, length, value) = replacements[i];
            result = result.Remove(index, length).Insert(index, value);
        }

        if (!result.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)) { result += ".mp4"; }

        return result;
    }

    private static string UnknownPlaceholder(string key)
    {
        LogWarn($"未知的文件名变量 <{key}>，已原样保留");
        return $"<{key}>";
    }

    [GeneratedRegex("<([\\w:\\-.]+?)>")]
    private static partial Regex InfoRegex( );
}
