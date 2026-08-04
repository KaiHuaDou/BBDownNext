using System;
using System.Net;
using System.Text;

namespace BBDown.Core.Opus;

/// <summary>
/// 旧版专栏（<c>data.type == 0</c>，<c>data.content</c> 为 HTML）的降级转换。仓库未引入 HTML 解析库，
/// 这里只覆盖常见标签、其余标签剥壳保留文本，属于尽力而为；调用方应 <see cref="Logger.LogWarn"/> 提示用户。
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

        // 行内：图片（先转，避免被后续标签处理吞掉 src）
        s = OpusRegexes.ImgDoubleQuote( ).Replace(s, m => $" ![]({NormalizeUrl(m.Groups[1].Value)}) ");
        s = OpusRegexes.ImgSingleQuote( ).Replace(s, m => $" ![]({NormalizeUrl(m.Groups[1].Value)}) ");

        // 行内：链接
        s = OpusRegexes.AnchorDoubleQuote( ).Replace(s, m => $" [{StripTags(m.Groups[2].Value)}]({NormalizeUrl(m.Groups[1].Value)}) ");
        s = OpusRegexes.AnchorSingleQuote( ).Replace(s, m => $" [{StripTags(m.Groups[2].Value)}]({NormalizeUrl(m.Groups[1].Value)}) ");

        // 行内：加粗 / 斜体
        s = OpusRegexes.Bold( ).Replace(s, m => $"**{StripTags(m.Groups[2].Value)}**");
        s = OpusRegexes.Italic( ).Replace(s, m => $"*{StripTags(m.Groups[2].Value)}*");

        // 代码块 / 行内代码
        s = OpusRegexes.PreCode( ).Replace(s, m => $"\n```\n{WebUtility.HtmlDecode(StripTags(m.Groups[2].Value).Trim( ))}\n```\n");

        // 引用块：逐行加 >
        s = OpusRegexes.Blockquote( ).Replace(s, m => Blockquote(StripTags(m.Groups[1].Value)));

        // 标题
        s = OpusRegexes.Heading( ).Replace(s, m =>
        {
            var level = int.Parse(m.Groups[1].Value);
            return $"\n{new string('#', level)} {StripTags(m.Groups[2].Value).Trim( )}\n";
        });

        // 列表项
        s = OpusRegexes.ListItem( ).Replace(s, m => $"- {StripTags(m.Groups[1].Value).Trim( )}\n");

        // 分割线
        s = OpusRegexes.HorizontalRule( ).Replace(s, "\n---\n");

        // 段落 / 分区 / 换行
        s = OpusRegexes.BlockClose( ).Replace(s, "\n");
        s = OpusRegexes.Break( ).Replace(s, "\n");

        // 剥掉剩余标签
        s = StripTags(s);

        // 解码 HTML 实体
        s = WebUtility.HtmlDecode(s);

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

    private static string NormalizeUrl(string url)
    {
        if (string.IsNullOrEmpty(url))
        {
            return "";
        }

        if (url.StartsWith("//", StringComparison.Ordinal))
        {
            return "https:" + url;
        }

        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            return "https://" + url[7..];
        }

        return url;
    }
}
