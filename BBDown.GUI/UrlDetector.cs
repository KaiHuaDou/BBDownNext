using System;
using System.Text.RegularExpressions;

using BBDown.Core;

namespace BBDown.GUI;

/// <summary>下载目标识别，纯函数；只描述识别结果，不做格式转换。ID 前缀复用 Core 的 IdPrefix 常量。</summary>
public static partial class UrlDetector
{
    /// <summary>识别输入文本，返回可读描述；无法识别返回 null。</summary>
    public static string? Describe(string? input)
    {
        var text = input?.Trim( ) ?? "";
        if (text.Length == 0)
        {
            return null;
        }

        if (MatchKnownPrefix(text) is { } description)
        {
            return description;
        }

        if (AvNumberRegex( ).IsMatch(text))
        {
            return "视频（av 号）";
        }

        if (Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
        {
            return DescribeUrl(text);
        }

        return null;
    }

    /// <summary>匹配已知 ID 前缀与特殊 URL，前缀后必须紧跟数字（BV 号亦以数字开头）。</summary>
    private static string? MatchKnownPrefix(string text)
    {
        if (StartsWithId(text, IdPrefix.Av))
        {
            return "视频（av 号）";
        }

        if (StartsWithId(text, IdPrefix.Bv))
        {
            return "视频（BV 号）";
        }

        if (StartsWithId(text, IdPrefix.Ep))
        {
            return "番剧（ep 号）";
        }

        if (StartsWithId(text, IdPrefix.Ss))
        {
            return "番剧（ss 号）";
        }

        if (StartsWithId(text, IdPrefix.Md))
        {
            return "番剧（md 号）";
        }

        // 课程简写格式为 cheese/ep 号 / cheese/ss 号（与 Core 的 IdPrefix.CheeseSlash 前缀一致）
        if (StartsWithId(text, "cheese/ep"))
        {
            return "课程（ep 号）";
        }

        if (StartsWithId(text, "cheese/ss"))
        {
            return "课程（ss 号）";
        }

        if (StartsWithId(text, "opus"))
        {
            return "专栏（opus）";
        }

        if (StartsWithId(text, "cv"))
        {
            return "专栏（cv）";
        }

        if (StartsWithId(text, "space"))
        {
            return "用户空间";
        }

        if (StartsWithId(text, "live"))
        {
            return "直播间（live 号）";
        }

        if (text.StartsWith("https://www.bilibili.com/watchlater", StringComparison.OrdinalIgnoreCase))
        {
            return "稍后再看列表";
        }

        if (text.StartsWith("https://live.bilibili.com", StringComparison.OrdinalIgnoreCase))
        {
            return "直播地址";
        }

        return null;
    }

    private static string? DescribeUrl(string text)
    {
        if (text.Contains("/cheese/", StringComparison.OrdinalIgnoreCase))
        {
            return "课程地址";
        }

        if (BvRegex( ).Match(text) is { Success: true } bv)
        {
            return $"视频（{bv.Value}）";
        }

        if (AvInUrlRegex( ).IsMatch(text))
        {
            return "视频（av 号）";
        }

        if (EpRegex( ).IsMatch(text))
        {
            return "番剧（ep 号）";
        }

        if (SsRegex( ).IsMatch(text))
        {
            return "番剧（ss 号）";
        }

        if (OpusRegex( ).IsMatch(text))
        {
            return "专栏（opus）";
        }

        if (CvRegex( ).IsMatch(text))
        {
            return "专栏（cv）";
        }

        return "视频地址";
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
}
