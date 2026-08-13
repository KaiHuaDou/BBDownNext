using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace BBDown.Core.Opus;

/// <summary>
/// 旧版专栏（<c>data.type == 0</c>，<c>data.content</c> 为 HTML）的降级转换。仓库未引入 HTML 解析库，
/// 策略：白名单标签可靠转换（链接/加粗/斜体/代码/引用/标题/列表/分割线/段落换行），
/// 其余标签（img、span 样式、figure、table 等）原样保留——CommonMark 支持内嵌 HTML，保真优于剥壳。
/// 属于尽力而为；调用方应 <see cref="Logger.LogWarn"/> 提示用户。
/// </summary>
public static class OpusHtmlToMarkdown
{
    public static string Convert(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return "";
        }

        var s = html;

        // 丢掉 script / style 整块
        s = OpusRegexes.ScriptStyle( ).Replace(s, " ");

        // 行内：链接（img 与其余标签不转换，原样保留）
        s = OpusRegexes.AnchorDoubleQuote( ).Replace(s, m => $" [{m.Groups[2].Value}]({OpusImageUtil.NormalizeProtocol(m.Groups[1].Value)}) ");
        s = OpusRegexes.AnchorSingleQuote( ).Replace(s, m => $" [{m.Groups[2].Value}]({OpusImageUtil.NormalizeProtocol(m.Groups[1].Value)}) ");

        // 行内：加粗 / 斜体
        s = OpusRegexes.Bold( ).Replace(s, m => $"**{m.Groups[2].Value}**");
        s = OpusRegexes.Italic( ).Replace(s, m => $"*{m.Groups[2].Value}*");

        // 代码块 / 行内代码：按字面量输出，剥掉内容里的标签
        s = OpusRegexes.PreCode( ).Replace(s, m => $"\n```\n{WebUtility.HtmlDecode(StripTags(m.Groups[2].Value).Trim( ))}\n```\n");

        // 引用块：逐行加 >
        s = OpusRegexes.Blockquote( ).Replace(s, m => Blockquote(m.Groups[1].Value));

        // 标题
        s = OpusRegexes.Heading( ).Replace(s, m =>
        {
            var level = int.Parse(m.Groups[1].Value);
            return $"\n{new string('#', level)} {m.Groups[2].Value.Trim( )}\n";
        });

        // 列表项
        s = OpusRegexes.ListItem( ).Replace(s, m => $"- {m.Groups[1].Value.Trim( )}\n");

        // 分割线
        s = OpusRegexes.HorizontalRule( ).Replace(s, "\n---\n");

        // 块级换行：p/ul/ol 直接转换行，div/section/figure/table 保留标签但前后补换行
        s = OpusRegexes.BlockStart( ).Replace(s, "\n");
        s = OpusRegexes.BlockOpen( ).Replace(s, m => "\n" + m.Value);
        s = OpusRegexes.BlockClose( ).Replace(s, m =>
            m.Groups[1].Value is "p" or "ul" or "ol" ? "\n" : m.Value + "\n");
        s = OpusRegexes.Break( ).Replace(s, "\n");

        // 标签段原样、文本段解码：保留标签属性里的实体（如 &quot;），只解正文文本
        s = DecodeTextPreservingTags(s);

        // 折叠多余空行
        s = OpusRegexes.MultipleBlankLines( ).Replace(s, "\n\n");

        return s.Trim( ) + "\n";
    }

    private static string Blockquote(string inner)
    {
        var sb = new StringBuilder( );
        foreach (var line in inner.Split('\n'))
        {
            var trimmed = line.Trim( );
            if (trimmed.Length != 0)
            {
                sb.AppendLine($"> {trimmed}");
            }
        }

        return sb.ToString( );
    }

    private static string StripTags(string s)
    {
        return OpusRegexes.AnyTag( ).Replace(s, "");
    }

    // 分段解码：<[^>]+> 匹配到的整体按标签段原样保留（属性里的实体不动），其余文本段 HtmlDecode
    private static string DecodeTextPreservingTags(string s)
    {
        var sb = new StringBuilder( );
        var last = 0;
        foreach (Match m in OpusRegexes.AnyTag( ).Matches(s))
        {
            sb.Append(WebUtility.HtmlDecode(s[last..m.Index]));
            sb.Append(m.Value);
            last = m.Index + m.Length;
        }

        sb.Append(WebUtility.HtmlDecode(s[last..]));
        return sb.ToString( );
    }
}
