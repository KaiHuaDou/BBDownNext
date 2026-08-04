using System.Text.RegularExpressions;

namespace BBDown.Core.Opus;

/// <summary>
/// Opus 模块共用的源生成正则表达式。统一用 <c>[GeneratedRegex]</c> 替代
/// <c>Regex.Replace</c> / <c>Regex.Match</c> 的运行时内联模式，保证 AOT 发布零反射、零解释器回退。
/// </summary>
internal static partial class OpusRegexes
{
    [GeneratedRegex(@"<(script|style)\b[^>]*>.*?</\1>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    public static partial Regex ScriptStyle( );

    [GeneratedRegex(@"<img\b[^>]*?src\s*=\s*""([^""]*)""[^>]*?>", RegexOptions.IgnoreCase)]
    public static partial Regex ImgDoubleQuote( );

    [GeneratedRegex(@"<img\b[^>]*?src\s*=\s*'([^']*)'[^>]*?>", RegexOptions.IgnoreCase)]
    public static partial Regex ImgSingleQuote( );

    [GeneratedRegex(@"<a\b[^>]*?href\s*=\s*""([^""]*)""[^>]*?>(.*?)</a>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    public static partial Regex AnchorDoubleQuote( );

    [GeneratedRegex(@"<a\b[^>]*?href\s*=\s*'([^']*)'[^>]*?>(.*?)</a>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    public static partial Regex AnchorSingleQuote( );

    [GeneratedRegex(@"<(strong|b)\b[^>]*>(.*?)</\1>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    public static partial Regex Bold( );

    [GeneratedRegex(@"<(em|i)\b[^>]*>(.*?)</\1>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    public static partial Regex Italic( );

    [GeneratedRegex(@"<(pre|code)\b[^>]*>(.*?)</\1>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    public static partial Regex PreCode( );

    [GeneratedRegex(@"<blockquote\b[^>]*>(.*?)</blockquote>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    public static partial Regex Blockquote( );

    [GeneratedRegex(@"<h([1-6])\b[^>]*>(.*?)</h\1>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    public static partial Regex Heading( );

    [GeneratedRegex(@"<li\b[^>]*>(.*?)</li>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    public static partial Regex ListItem( );

    [GeneratedRegex(@"<hr\b[^>]*/?>", RegexOptions.IgnoreCase)]
    public static partial Regex HorizontalRule( );

    [GeneratedRegex(@"</(p|div|section)>", RegexOptions.IgnoreCase)]
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
