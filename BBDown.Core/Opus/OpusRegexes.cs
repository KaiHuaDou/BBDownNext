using System.Text.RegularExpressions;

namespace BBDown.Core.Opus;

/// <summary>
/// Opus 模块共用的源生成正则表达式。统一用 <c>[GeneratedRegex]</c> 替代
/// <c>Regex.Replace</c> / <c>Regex.Match</c> 的运行时内联模式，保证 AOT 发布零反射、零解释器回退。
/// </summary>
internal static partial class OpusRegexes
{
    [GeneratedRegex(@"<(script|style)\b[^>]*>.*?</(script|style)>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    public static partial Regex ScriptStyle( );

    [GeneratedRegex(@"<a\b[^>]*?href\s*=\s*""([^""]*)""[^>]*?>(.*?)</a>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    public static partial Regex AnchorDoubleQuote( );

    [GeneratedRegex(@"<a\b[^>]*?href\s*=\s*'([^']*)'[^>]*?>(.*?)</a>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    public static partial Regex AnchorSingleQuote( );

    [GeneratedRegex(@"<(strong|b)\b[^>]*>(.*?)</(strong|b)>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    public static partial Regex Bold( );

    [GeneratedRegex(@"<(em|i)\b[^>]*>(.*?)</(em|i)>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    public static partial Regex Italic( );

    [GeneratedRegex(@"<(pre|code)\b[^>]*>(.*?)</(pre|code)>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    public static partial Regex PreCode( );

    [GeneratedRegex(@"<blockquote\b[^>]*>(.*?)</blockquote>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    public static partial Regex Blockquote( );

    [GeneratedRegex(@"<h([1-6])\b[^>]*>(.*?)</h[1-6]>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    public static partial Regex Heading( );

    [GeneratedRegex(@"<li\b[^>]*>(.*?)</li>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    public static partial Regex ListItem( );

    [GeneratedRegex(@"<hr\b[^>]*/?>", RegexOptions.IgnoreCase)]
    public static partial Regex HorizontalRule( );

    // 块级开标签：p/ul/ol 是纯结构容器，直接转换行；div/section/figure/table 保留原样但前补换行
    [GeneratedRegex(@"<(p|ul|ol)\b[^>]*>", RegexOptions.IgnoreCase)]
    public static partial Regex BlockStart( );

    [GeneratedRegex(@"<(div|section|figure|table)\b[^>]*>", RegexOptions.IgnoreCase)]
    public static partial Regex BlockOpen( );

    // 块级闭合标签：p/ul/ol 转换行；div/section/figure/table 保留标签本身，仅在其后补换行
    [GeneratedRegex(@"</(p|ul|ol|div|section|figure|table)>", RegexOptions.IgnoreCase)]
    public static partial Regex BlockClose( );

    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase)]
    public static partial Regex Break( );

    [GeneratedRegex(@"<[^>]+>", RegexOptions.Singleline)]
    public static partial Regex AnyTag( );

    [GeneratedRegex(@"\n{3,}", RegexOptions.Singleline)]
    public static partial Regex MultipleBlankLines( );

    // OpusFetcher.Friendly：从异常消息里抽取 code= 数值
    [GeneratedRegex(@"code=(-?\d+)")]
    public static partial Regex CodeInMessage( );
}
