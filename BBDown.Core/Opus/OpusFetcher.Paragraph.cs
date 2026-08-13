using System.Collections.Generic;
using System.Text.Json;

using static BBDown.Core.Logger;
using static BBDown.Core.Util.JsonUtil;

namespace BBDown.Core.Opus;

// 段落与节点解析：段落分发（结构探测优先于枚举硬编码）与各类型构造
public static partial class OpusFetcher
{
    internal static List<OpusParagraph> ParseParagraphs(JsonElement paras)
    {
        var list = new List<OpusParagraph>( );
        foreach (var para in EnumerateArrayOrEmpty(paras))
        {
            var p = ParseParagraph(para);
            if (p.Kind != OpusParagraphKind.Unknown)
            {
                list.Add(p);
            }
        }

        return list;
    }

    // 两套 schema（opus/detail 与 article/view）的 para_type 枚举含义不一致，先看存在哪个子对象再决定渲染方式
    internal static OpusParagraph ParseParagraph(JsonElement para)
    {
        if (para.TryGetProperty("pic", out var pic) && pic.ValueKind == JsonValueKind.Object
            && pic.TryGetProperty("pics", out var pics) && pics.ValueKind == JsonValueKind.Array && pics.GetArrayLength( ) > 0)
        {
            return ParseImageParagraph(para, pics);
        }

        if (para.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.Object)
        {
            return ParseCodeParagraph(code);
        }

        if (para.TryGetProperty("list", out var list) && list.ValueKind == JsonValueKind.Object
            && list.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array && items.GetArrayLength( ) > 0)
        {
            return ParseListParagraph(list, items);
        }

        if (para.TryGetProperty("link_card", out var linkCard) && linkCard.ValueKind == JsonValueKind.Object)
        {
            return ParseLinkCard(linkCard);
        }

        if (para.TryGetProperty("format", out var fmt) && fmt.ValueKind == JsonValueKind.Object
            && fmt.TryGetProperty("list_format", out var lf) && lf.ValueKind == JsonValueKind.Object)
        {
            return ParseListFormatParagraph(para, lf);
        }

        if (IsParaType(para, 3))
        {
            return ParseLineParagraph(para);
        }

        if (IsParaType(para, 4))
        {
            return ParseQuoteParagraph(para);
        }

        if (para.TryGetProperty("text", out var text) && text.TryGetProperty("nodes", out var nodes))
        {
            var textNodes = ParseNodes(nodes);
            var headingLevel = DetectHeadingLevel(textNodes);
            if (headingLevel > 0)
            {
                return new OpusParagraph { Kind = OpusParagraphKind.Heading, HeadingLevel = headingLevel, TextNodes = textNodes };
            }

            return new OpusParagraph { Kind = OpusParagraphKind.Text, TextNodes = textNodes };
        }

        LogDebug("未识别的专栏段落 para_type={0}", para.TryGetProperty("para_type", out var p) ? p.ToString( ) : "?");
        return new OpusParagraph { Kind = OpusParagraphKind.Unknown };
    }

    private static bool IsParaType(JsonElement para, int expected)
    {
        return para.TryGetProperty("para_type", out var pt) && pt.ValueKind == JsonValueKind.Number && pt.GetInt32( ) == expected;
    }

    // para_type 3 有两种形态：line.line_type 为分割线，line.pic 为图片（article/view 的 figure 图片落在这里）
    private static OpusParagraph ParseLineParagraph(JsonElement para)
    {
        if (para.TryGetProperty("line", out var line) && line.ValueKind == JsonValueKind.Object
            && line.TryGetProperty("pic", out var linePic) && linePic.ValueKind == JsonValueKind.Object)
        {
            var img = new OpusParagraph { Kind = OpusParagraphKind.Image };
            var url = linePic.TryGetProperty("url", out var lu) ? (lu.GetString( ) ?? "") : "";
            img.Images.Add(new OpusImage { Url = url });
            return img;
        }

        return new OpusParagraph { Kind = OpusParagraphKind.Divider };
    }

    private static OpusParagraph ParseQuoteParagraph(JsonElement para)
    {
        var quote = new OpusParagraph { Kind = OpusParagraphKind.Quote };
        if (para.TryGetProperty("text", out var qt) && qt.TryGetProperty("nodes", out var qn))
        {
            quote.TextNodes = ParseNodes(qn);
        }

        return quote;
    }

    private static int DetectHeadingLevel(List<OpusTextNode> nodes)
    {
        var maxFont = 0;
        var anyBold = false;
        foreach (var n in nodes)
        {
            if (n.FontSize > maxFont)
            {
                maxFont = n.FontSize;
            }

            if (n.Bold)
            {
                anyBold = true;
            }
        }

        if (anyBold && maxFont >= 24)
        {
            return 2;
        }

        if (anyBold && maxFont >= 22)
        {
            return 3;
        }

        return 0;
    }

    internal static List<OpusTextNode> ParseNodes(JsonElement nodes)
    {
        var result = new List<OpusTextNode>( );
        foreach (var node in EnumerateArrayOrEmpty(nodes))
        {
            if (node.TryGetProperty("rich", out var rich) && rich.ValueKind == JsonValueKind.Object)
            {
                var text = rich.TryGetProperty("orig_text", out var ot) ? (ot.GetString( ) ?? "")
                           : (rich.TryGetProperty("text", out var rt) ? (rt.GetString( ) ?? "") : "");
                var url = rich.TryGetProperty("jump_url", out var ju) ? (ju.GetString( ) ?? "") : "";
                result.Add(new OpusTextNode { Text = text, Url = string.IsNullOrEmpty(url) ? null : url });
                continue;
            }

            if (node.TryGetProperty("formula", out var formula) && formula.ValueKind == JsonValueKind.Object)
            {
                var latex = formula.TryGetProperty("latex_content", out var lc) ? (lc.GetString( ) ?? "") : "";
                result.Add(new OpusTextNode { IsFormula = true, FormulaLatex = latex });
                continue;
            }

            if (node.TryGetProperty("word", out var word) && word.ValueKind == JsonValueKind.Object)
            {
                var text = word.TryGetProperty("words", out var w) ? (w.GetString( ) ?? "") : "";
                var bold = word.TryGetProperty("style", out var st) && st.ValueKind == JsonValueKind.Object
                           && st.TryGetProperty("bold", out var b) && b.ValueKind == JsonValueKind.True;
                var fontSize = word.TryGetProperty("font_size", out var fs) && fs.ValueKind == JsonValueKind.Number ? fs.GetInt32( ) : 0;
                result.Add(new OpusTextNode { Text = text, Bold = bold, FontSize = fontSize });
            }
        }

        return result;
    }

    private static OpusParagraph ParseImageParagraph(JsonElement para, JsonElement pics)
    {
        var p = new OpusParagraph { Kind = OpusParagraphKind.Image };
        var captionFromText = para.TryGetProperty("text", out var t) && t.TryGetProperty("nodes", out var tn) && tn.ValueKind == JsonValueKind.Array && tn.GetArrayLength( ) > 0
            ? (tn[0].TryGetProperty("word", out var w) && w.TryGetProperty("words", out var ws) ? (ws.GetString( ) ?? "") : "")
            : "";

        foreach (var picEl in pics.EnumerateArray( ))
        {
            var url = picEl.TryGetProperty("url", out var u) ? (u.GetString( ) ?? "") : "";
            var caption = picEl.TryGetProperty("caption", out var cap) ? (cap.GetString( ) ?? captionFromText) : captionFromText;
            var width = picEl.TryGetProperty("width", out var wd) && wd.ValueKind == JsonValueKind.Number ? wd.GetInt32( ) : 0;
            var height = picEl.TryGetProperty("height", out var ht) && ht.ValueKind == JsonValueKind.Number ? ht.GetInt32( ) : 0;
            p.Images.Add(new OpusImage { Url = url, Caption = caption, Width = width, Height = height });
        }

        return p;
    }

    private static OpusParagraph ParseCodeParagraph(JsonElement code)
    {
        var content = code.TryGetProperty("content", out var c) ? (c.GetString( ) ?? "") : "";
        var lang = code.TryGetProperty("lang", out var l) ? (l.GetString( ) ?? "") : "";
        return new OpusParagraph { Kind = OpusParagraphKind.Code, Code = content, CodeLang = lang };
    }

    private static OpusParagraph ParseListParagraph(JsonElement list, JsonElement items)
    {
        var p = new OpusParagraph { Kind = OpusParagraphKind.List };
        var style = list.TryGetProperty("style", out var st) && st.ValueKind == JsonValueKind.Number ? st.GetInt32( ) : 2;
        p.ListStyle = style == 1 ? OpusListStyle.Ordered : OpusListStyle.Unordered;

        foreach (var item in items.EnumerateArray( ))
        {
            var li = new OpusListItem
            {
                Level = item.TryGetProperty("level", out var lv) && lv.ValueKind == JsonValueKind.Number ? lv.GetInt32( ) : 1,
                Order = item.TryGetProperty("order", out var od) && od.ValueKind == JsonValueKind.Number ? od.GetInt32( ) : 1,
            };
            if (item.TryGetProperty("nodes", out var nodes))
            {
                li.Nodes = ParseNodes(nodes);
            }

            p.ListItems.Add(li);
        }

        return p;
    }

    private static OpusParagraph ParseListFormatParagraph(JsonElement para, JsonElement lf)
    {
        var p = new OpusParagraph { Kind = OpusParagraphKind.List, ListStyle = OpusListStyle.Unordered };
        var li = new OpusListItem
        {
            Level = lf.TryGetProperty("level", out var lv) && lv.ValueKind == JsonValueKind.Number ? lv.GetInt32( ) : 1,
            Order = lf.TryGetProperty("order", out var ord) && ord.ValueKind == JsonValueKind.Number ? ord.GetInt32( ) : 1,
        };
        if (para.TryGetProperty("text", out var text) && text.TryGetProperty("nodes", out var nodes))
        {
            li.Nodes = ParseNodes(nodes);
        }

        p.ListItems.Add(li);
        return p;
    }

    private static OpusParagraph ParseLinkCard(JsonElement linkCard)
    {
        var p = new OpusParagraph { Kind = OpusParagraphKind.LinkCard };
        var card = linkCard.TryGetProperty("card", out var c) ? c : default;
        if (card.ValueKind == JsonValueKind.Object)
        {
            // article/view 版卡片无 title，回退 show_text（如角色卡）
            p.LinkTitle = card.TryGetProperty("title", out var t) ? (t.GetString( ) ?? "")
                          : (card.TryGetProperty("show_text", out var st) ? (st.GetString( ) ?? "") : "");
            var url = card.TryGetProperty("jump_url", out var ju) ? (ju.GetString( ) ?? "")
                       : (linkCard.TryGetProperty("jump_url", out var lju) ? (lju.GetString( ) ?? "") : "");
            p.LinkUrl = url;
        }

        return p;
    }
}
