using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Text;

namespace BBDown.Core.Opus;

/// <summary>
/// 渲染选项。<see cref="EmbedFrontMatter"/> 控制是否输出 YAML 头部；<see cref="ImagePathMap"/> 把归一化后的
/// 远程图片 URL 映射到 Markdown 中使用的相对路径（为 null 或查不到时直接写远程 URL，即 --no-images 与单图下载失败时的降级）。
/// </summary>
public sealed record OpusRenderOptions(
    bool EmbedFrontMatter = true,
    IReadOnlyDictionary<string, string>? ImagePathMap = null);

public static class OpusMarkdownRenderer
{
    public static string Render(OpusDocument doc, OpusRenderOptions options)
    {
        var sb = new StringBuilder( );
        if (options.EmbedFrontMatter)
        {
            AppendFrontMatter(sb, doc);
        }

        sb.AppendLine($"# {doc.Title.Trim( )}");
        sb.AppendLine( );

        foreach (var p in doc.Paragraphs)
        {
            RenderParagraph(sb, p, options);
        }

        return sb.ToString( ).TrimEnd( ) + Environment.NewLine;
    }

    private static void AppendFrontMatter(StringBuilder sb, OpusDocument doc)
    {
        sb.AppendLine("---");
        sb.AppendLine($"title: \"{EscapeYaml(doc.Title)}\"");
        if (!string.IsNullOrEmpty(doc.AuthorName))
        {
            sb.AppendLine($"author: \"{EscapeYaml(doc.AuthorName)}\"");
        }

        if (!string.IsNullOrEmpty(doc.AuthorMid))
        {
            sb.AppendLine($"mid: {doc.AuthorMid}");
        }

        if (doc.PublishTime > 0)
        {
            sb.AppendLine($"date: {FormatTime(doc.PublishTime)}");
        }

        if (!string.IsNullOrEmpty(doc.SourceUrl))
        {
            sb.AppendLine($"url: {doc.SourceUrl}");
        }

        if (!string.IsNullOrEmpty(doc.OpusId))
        {
            sb.AppendLine($"opus: {BiliApi.OpusPage}/{doc.OpusId}");
        }

        if (doc.Tags.Count > 0)
        {
            sb.AppendLine("tags:");
            foreach (var tag in doc.Tags)
            {
                sb.AppendLine($"  - {EscapeYaml(tag)}");
            }
        }

        if (!string.IsNullOrEmpty(doc.Summary))
        {
            sb.AppendLine($"summary: \"{EscapeYaml(doc.Summary)}\"");
        }

        sb.AppendLine("---");
        sb.AppendLine( );
    }

    private static void RenderParagraph(StringBuilder sb, OpusParagraph p, OpusRenderOptions options)
    {
        switch (p.Kind)
        {
            case OpusParagraphKind.Heading:
                sb.AppendLine($"{new string('#', p.HeadingLevel)} {RenderInline(p.TextNodes)}");
                sb.AppendLine( );
                break;
            case OpusParagraphKind.Text:
                sb.AppendLine(RenderInline(p.TextNodes));
                sb.AppendLine( );
                break;
            case OpusParagraphKind.Image:
                foreach (var img in p.Images)
                {
                    var src = ResolveImageSrc(img.Url, options);
                    var alt = string.IsNullOrEmpty(img.Caption) ? "image" : img.Caption;
                    sb.AppendLine($"![{EscapeLinkText(alt)}]({FormatLinkTarget(src)})");
                    if (!string.IsNullOrEmpty(img.Caption))
                    {
                        sb.AppendLine($"*{EscapeInline(img.Caption)}*");
                    }
                }

                sb.AppendLine( );
                break;
            case OpusParagraphKind.Divider:
                sb.AppendLine("---");
                sb.AppendLine( );
                break;
            case OpusParagraphKind.Quote:
                var quote = RenderInline(p.TextNodes);
                foreach (var line in quote.Split('\n'))
                {
                    sb.AppendLine($"> {line}");
                }

                sb.AppendLine( );
                break;
            case OpusParagraphKind.List:
                foreach (var item in p.ListItems)
                {
                    var indent = new string(' ', item.Level * 2);
                    var marker = p.ListStyle == OpusListStyle.Ordered ? $"{item.Order}. " : "- ";
                    sb.AppendLine($"{indent}{marker}{RenderInline(item.Nodes)}");
                }

                sb.AppendLine( );
                break;
            case OpusParagraphKind.Code:
                RenderCode(sb, p);
                sb.AppendLine( );
                break;
            case OpusParagraphKind.LinkCard:
                var title = string.IsNullOrEmpty(p.LinkTitle) ? p.LinkUrl : p.LinkTitle;
                sb.AppendLine($"> [{EscapeLinkText(title)}]({FormatLinkTarget(p.LinkUrl)})");
                sb.AppendLine(">");
                sb.AppendLine( );
                break;
            case OpusParagraphKind.Unknown:
            default:
                break;
        }
    }

    private static void RenderCode(StringBuilder sb, OpusParagraph p)
    {
        var lang = p.CodeLang;
        if (lang.StartsWith("language-", StringComparison.OrdinalIgnoreCase))
        {
            lang = lang["language-".Length..];
        }

        var content = WebUtility.HtmlDecode(p.Code ?? "");
        // 内容里若已含 ``` ，围栏加长到 4 个反引号避免提前闭合
        var fence = content.Contains("```", StringComparison.Ordinal) ? "````" : "```";
        sb.AppendLine($"{fence}{lang}");
        sb.AppendLine(content.TrimEnd( ));
        sb.AppendLine(fence);
    }

    private static string RenderInline(List<OpusTextNode> nodes)
    {
        var sb = new StringBuilder( );
        foreach (var node in nodes)
        {
            if (node.IsFormula && !string.IsNullOrEmpty(node.FormulaLatex))
            {
                sb.Append($"${node.FormulaLatex}$");
                continue;
            }

            var text = EscapeInline(node.Text ?? "");
            if (!string.IsNullOrEmpty(node.Url))
            {
                var url = NormalizeUrl(node.Url);
                sb.Append($"[{text}]({FormatLinkTarget(url)})");
            }
            else if (node.Bold)
            {
                // 把首尾空格移到 ** 之外，否则多数渲染器不认 ** 文字 **
                var (lead, mid, tail) = SplitEdgeWhitespace(text);
                sb.Append(lead);
                sb.Append($"**{mid}**");
                sb.Append(tail);
            }
            else
            {
                sb.Append(text);
            }
        }

        return sb.ToString( );
    }

    private static string ResolveImageSrc(string url, OpusRenderOptions options)
    {
        var normalized = OpusImageUtil.Normalize(url);
        if (options.ImagePathMap != null && options.ImagePathMap.TryGetValue(normalized, out var local))
        {
            return local;
        }

        return normalized;
    }

    private static (string Lead, string Mid, string Tail) SplitEdgeWhitespace(string s)
    {
        var i = 0;
        while (i < s.Length && char.IsWhiteSpace(s[i]))
        {
            i++;
        }

        var lead = s[..i];

        var j = s.Length;
        while (j > i && char.IsWhiteSpace(s[j - 1]))
        {
            j--;
        }

        var tail = s[j..];
        return (lead, s[i..j], tail);
    }

    private static string FormatTime(long unixSeconds)
    {
        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(unixSeconds)
                .ToLocalTime( )
                .ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }
        catch
        {
            return "";
        }
    }

    // YAML 双引号字符串转义：先转义反斜杠与双引号，再把换行压成空格
    private static string EscapeYaml(string s)
    {
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", " ");
    }

    // 行内 Markdown 语法字符转义（只转义会破坏行内语义的字符，不碰中文标点与行首符号）
    private static string EscapeInline(string s)
    {
        if (string.IsNullOrEmpty(s))
        {
            return s;
        }

        return s.Replace("\\", "\\\\")
                .Replace("`", "\\`")
                .Replace("*", "\\*")
                .Replace("_", "\\_")
                .Replace("[", "\\[")
                .Replace("]", "\\]");
    }

    private static string EscapeLinkText(string s)
    {
        return s.Replace("[", "\\[").Replace("]", "\\]").Replace("(", "\\(").Replace(")", "\\)");
    }

    /// <summary>
    /// 远程 URL 与本地相对路径的转义规则相反：URL 里的 <c>? # %</c> 是有效语法，百分号转义会把链接改写成
    /// 另一个（打不开的）地址；本地文件名则相反，空格与括号必须转义才不会截断链接。
    /// </summary>
    private static string FormatLinkTarget(string target)
    {
        if (string.IsNullOrEmpty(target))
        {
            return "";
        }

        if (target.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || target.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            // 仅空格与括号会破坏 (...) 形式，改用 <...> 形式原样保留 URL
            return target.IndexOfAny([' ', '(', ')']) >= 0 ? $"<{target}>" : target;
        }

        // 本地相对路径：% 必须最先处理，否则后续插入的 %20 会被二次转义
        return target.Replace("%", "%25", StringComparison.Ordinal)
                     .Replace(" ", "%20", StringComparison.Ordinal)
                     .Replace("(", "%28", StringComparison.Ordinal)
                     .Replace(")", "%29", StringComparison.Ordinal)
                     .Replace("#", "%23", StringComparison.Ordinal)
                     .Replace("?", "%3F", StringComparison.Ordinal);
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
