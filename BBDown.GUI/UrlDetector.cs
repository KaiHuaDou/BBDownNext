using System;
using System.Text.RegularExpressions;

using BBDown.Core;
using BBDown.Core.Live;

namespace BBDown.GUI;

/// <summary>目标资源域，GUI 可用性联动的判定依据；与 Core 的 ContentSelector.ModeOf 分支对齐。</summary>
public enum TargetKind
{
    Video,
    Pgc,
    Opus,
    Audio,
    Live,
    Mixed,
}

/// <summary>识别结果：给人看的描述 + 给程序用的域。</summary>
public sealed record TargetInfo(string Description, TargetKind Kind);

/// <summary>下载目标识别，纯函数；不做格式转换。ID 前缀复用 Core 的 IdPrefix 常量。</summary>
public static partial class UrlDetector
{
    /// <summary>识别输入文本；无法识别返回 null。</summary>
    public static TargetInfo? Describe(string? input)
    {
        var text = input?.Trim( ) ?? "";
        if (text.Length == 0)
        {
            return null;
        }

        // 直播形态以 Core 的 LiveInputResolver 为单一来源（live 号 / 带协议地址 / 无协议裸域名），
        // 与任务执行期的路由判定同源，避免 GUI 预检与 Core 接受域不一致
        if (LiveInputResolver.TryParse(text, out _))
        {
            return new TargetInfo("直播间（live 号或地址）", TargetKind.Live);
        }

        if (MatchKnownPrefix(text) is { } info)
        {
            return info;
        }

        if (AvNumberRegex( ).IsMatch(text))
        {
            return new TargetInfo("视频（av 号）", TargetKind.Video);
        }

        if (Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
        {
            return DescribeUrl(text);
        }

        return null;
    }

    /// <summary>匹配已知 ID 前缀与特殊 URL，前缀后必须紧跟数字（BV 号亦以数字开头）。</summary>
    private static TargetInfo? MatchKnownPrefix(string text)
    {
        if (StartsWithId(text, IdPrefix.Av))
        {
            return new TargetInfo("视频（av 号）", TargetKind.Video);
        }

        if (StartsWithId(text, IdPrefix.Bv))
        {
            return new TargetInfo("视频（BV 号）", TargetKind.Video);
        }

        if (StartsWithId(text, IdPrefix.Ep))
        {
            return new TargetInfo("番剧（ep 号）", TargetKind.Pgc);
        }

        if (StartsWithId(text, IdPrefix.Ss))
        {
            return new TargetInfo("番剧（ss 号）", TargetKind.Pgc);
        }

        if (StartsWithId(text, IdPrefix.Md))
        {
            return new TargetInfo("番剧（md 号）", TargetKind.Pgc);
        }

        // 课程简写格式为 cheese/ep 号 / cheese/ss 号（与 Core 的 IdPrefix.CheeseSlash 前缀一致）
        if (StartsWithId(text, "cheese/ep"))
        {
            return new TargetInfo("课程（ep 号）", TargetKind.Pgc);
        }

        if (StartsWithId(text, "cheese/ss"))
        {
            return new TargetInfo("课程（ss 号）", TargetKind.Pgc);
        }

        if (StartsWithId(text, "opus"))
        {
            return new TargetInfo("专栏（opus）", TargetKind.Opus);
        }

        if (StartsWithId(text, "cv"))
        {
            return new TargetInfo("专栏（cv）", TargetKind.Opus);
        }

        if (StartsWithId(text, "space"))
        {
            return new TargetInfo("用户空间", TargetKind.Video);
        }

        // 集合简写（spaceOpus123 等）：space 分支要求 space 后紧跟数字，spaceOpus123 不会命中 space 分支
        if (StartsWithId(text, IdPrefix.SpaceOpus))
        {
            return new TargetInfo("空间图文投稿", TargetKind.Opus);
        }

        if (StartsWithId(text, IdPrefix.SpaceAudio))
        {
            return new TargetInfo("空间音频投稿", TargetKind.Audio);
        }

        if (StartsWithId(text, IdPrefix.SpaceDynamic))
        {
            return new TargetInfo("空间动态", TargetKind.Mixed);
        }

        if (StartsWithId(text, IdPrefix.ReadList))
        {
            return new TargetInfo("文集", TargetKind.Opus);
        }

        if (StartsWithId(text, IdPrefix.Rl))
        {
            return new TargetInfo("文集", TargetKind.Opus);
        }

        if (StartsWithId(text, IdPrefix.Au))
        {
            return new TargetInfo("音频（au 号）", TargetKind.Audio);
        }

        if (text.StartsWith("https://www.bilibili.com/watchlater", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("page=watchlater", StringComparison.OrdinalIgnoreCase))
        {
            return new TargetInfo("稍后再看列表", TargetKind.Video);
        }

        return null;
    }

    private static TargetInfo? DescribeUrl(string text)
    {
        // live 域名但无房间号（如直播首页）：LiveInputResolver 未命中才会走到这里，无法下载，按未识别处理
        if (text.Contains("live.bilibili.com", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (text.Contains("/cheese/", StringComparison.OrdinalIgnoreCase))
        {
            return new TargetInfo("课程地址", TargetKind.Pgc);
        }

        if (text.Contains("/read/readlist/", StringComparison.OrdinalIgnoreCase))
        {
            return new TargetInfo("文集地址", TargetKind.Opus);
        }

        // 空间子页限定 host（与 Core 的 TryParseCollection 守卫一致），非空间域的 /audio 等路径不误标
        var spaceHost = text.Contains("/space.bilibili.com/", StringComparison.OrdinalIgnoreCase);
        if (spaceHost && text.Contains("/upload/opus", StringComparison.OrdinalIgnoreCase))
        {
            return new TargetInfo("空间图文投稿地址", TargetKind.Opus);
        }

        // 旧版音频页 space.bilibili.com/{mid}/audio 与新版 /upload/audio 同义（/audio 判定两者通吃）
        if (spaceHost && text.Contains("/audio", StringComparison.OrdinalIgnoreCase))
        {
            return new TargetInfo("空间音频投稿地址", TargetKind.Audio);
        }

        if (spaceHost && text.Contains("/dynamic", StringComparison.OrdinalIgnoreCase))
        {
            return new TargetInfo("空间动态地址", TargetKind.Mixed);
        }

        // 合集 / 系列：space lists 页（?type=series 为系列，其余按合集）、channel 页、老版 medialist/ml 分享链接
        if (spaceHost && text.Contains("/lists/", StringComparison.OrdinalIgnoreCase))
        {
            return text.Contains("type=series", StringComparison.OrdinalIgnoreCase)
                ? new TargetInfo("系列地址", TargetKind.Video)
                : new TargetInfo("合集地址", TargetKind.Video);
        }

        if (text.Contains("/channel/collectiondetail", StringComparison.OrdinalIgnoreCase))
        {
            return new TargetInfo("合集地址", TargetKind.Video);
        }

        if (text.Contains("/channel/seriesdetail", StringComparison.OrdinalIgnoreCase))
        {
            return new TargetInfo("系列地址", TargetKind.Video);
        }

        if (MedialistMlRegex( ).IsMatch(text))
        {
            return new TargetInfo("合集地址", TargetKind.Video);
        }

        // 单音频页 www.bilibili.com/audio/au12345（space 域的 /audio 列表页已在上面先行识别）
        if (text.Contains("/audio/au", StringComparison.OrdinalIgnoreCase))
        {
            return new TargetInfo("音频地址（au 号）", TargetKind.Audio);
        }

        if (BvRegex( ).Match(text) is { Success: true } bv)
        {
            return new TargetInfo($"视频（{bv.Value}）", TargetKind.Video);
        }

        if (AvInUrlRegex( ).IsMatch(text))
        {
            return new TargetInfo("视频（av 号）", TargetKind.Video);
        }

        if (EpRegex( ).IsMatch(text))
        {
            return new TargetInfo("番剧（ep 号）", TargetKind.Pgc);
        }

        if (SsRegex( ).IsMatch(text))
        {
            return new TargetInfo("番剧（ss 号）", TargetKind.Pgc);
        }

        if (OpusRegex( ).IsMatch(text))
        {
            return new TargetInfo("专栏（opus）", TargetKind.Opus);
        }

        if (CvRegex( ).IsMatch(text))
        {
            return new TargetInfo("专栏（cv）", TargetKind.Opus);
        }

        return new TargetInfo("视频地址", TargetKind.Video);
    }

    private static bool StartsWithId(string text, string prefix)
    {
        if (text.Length <= prefix.Length || !text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return char.IsAsciiDigit(text[prefix.Length]);
    }

    [GeneratedRegex(@"^[0-9]+$")]
    private static partial Regex AvNumberRegex( );

    [GeneratedRegex(@"BV[0-9A-Za-z]+", RegexOptions.IgnoreCase)]
    private static partial Regex BvRegex( );

    [GeneratedRegex(@"av[0-9]+", RegexOptions.IgnoreCase)]
    private static partial Regex AvInUrlRegex( );

    [GeneratedRegex(@"ep[0-9]+", RegexOptions.IgnoreCase)]
    private static partial Regex EpRegex( );

    [GeneratedRegex(@"ss[0-9]+", RegexOptions.IgnoreCase)]
    private static partial Regex SsRegex( );

    [GeneratedRegex(@"opus/?[0-9]+", RegexOptions.IgnoreCase)]
    private static partial Regex OpusRegex( );

    [GeneratedRegex(@"cv/?[0-9]+", RegexOptions.IgnoreCase)]
    private static partial Regex CvRegex( );

    [GeneratedRegex(@"medialist/(?:play|detail)/ml[0-9]+", RegexOptions.IgnoreCase)]
    private static partial Regex MedialistMlRegex( );
}
