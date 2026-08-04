using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core.Util;

using static BBDown.Core.Logger;
using static BBDown.Core.Util.HTTPUtil;
using static BBDown.Core.Util.JsonUtil;

namespace BBDown.Core.Opus;

public static class OpusFetcher
{
    // htmlNewStyle 会让 opus/detail 直出专栏正文与 rid_str；不带该 feature 时只回退 fallback.id
    private const string OpusFeatures =
        "itemOpusStyle,opusBigCover,onlyfansVote,decorationCard,forwardListHidden,ugcDelete,onlyfansQaCard,htmlNewStyle";

    public static async Task<OpusDocument> FetchAsync(OpusTarget target, AppConfig cfg, CancellationToken ct = default)
    {
        try
        {
            var cvId = target.CvId;
            if (string.IsNullOrEmpty(cvId))
            {
                var detailUrl = $"{BiliApi.OpusDetail}?timezone_offset=-480&id={target.OpusId}&features={OpusFeatures}";
                using var detailDoc = JsonDocument.Parse(await GetWebSourceAsync(detailUrl, cfg, null, ct));
                var data = GetApiData(detailDoc.RootElement, "专栏信息");
                cvId = TryGetCvId(data) ?? "";
                if (string.IsNullOrEmpty(cvId))
                {
                    LogWarn("该 opus 不是专栏文章，将按图文动态导出。");
                    return ParseOpusDetail(data, $"{BiliApi.OpusPage}/{target.OpusId}");
                }
            }

            var viewUrl = $"{BiliApi.ArticleView}?id={cvId}";
            using var viewDoc = JsonDocument.Parse(await GetWebSourceAsync(viewUrl, cfg, null, ct));
            var viewData = GetApiData(viewDoc.RootElement, "专栏正文");
            return ParseArticleView(viewData, cvId, $"{BiliApi.ReadPage}/cv{cvId}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw Friendly(ex);
        }
    }

    /// <summary>
    /// 从 opus/detail 的返回里取出 cv id：优先 <c>data.fallback.id</c>（type==2 为专栏），否则 <c>data.item.basic.rid_str</c>。
    /// 都取不到说明不是专栏，返回 null。
    /// </summary>
    internal static string? TryGetCvId(JsonElement data)
    {
        if (data.TryGetProperty("fallback", out var fallback) && fallback.ValueKind == JsonValueKind.Object)
        {
            if (fallback.TryGetProperty("type", out var ftype) && ftype.ValueKind == JsonValueKind.Number && ftype.GetInt32( ) == 2
                && fallback.TryGetProperty("id", out var fid))
            {
                return fid.ValueKind == JsonValueKind.Number ? fid.GetRawText( ) : (fid.GetString( ) ?? "");
            }
        }

        if (data.TryGetProperty("item", out var item) && item.ValueKind == JsonValueKind.Object
            && item.TryGetProperty("basic", out var basic) && basic.TryGetProperty("rid_str", out var rid) && rid.ValueKind == JsonValueKind.String)
        {
            return rid.GetString( );
        }

        return null;
    }

    internal static OpusDocument ParseArticleView(JsonElement data, string cvId, string sourceUrl)
    {
        var doc = new OpusDocument
        {
            CvId = cvId,
            SourceUrl = sourceUrl,
            Title = data.TryGetProperty("title", out var t) ? (t.GetString( ) ?? "") : "",
            Summary = data.TryGetProperty("summary", out var sm) ? (sm.GetString( ) ?? "") : "",
            OpusId = data.TryGetProperty("dyn_id_str", out var d) ? (d.GetString( ) ?? "") : "",
        };

        if (data.TryGetProperty("author", out var author) && author.ValueKind == JsonValueKind.Object)
        {
            doc.AuthorName = author.TryGetProperty("name", out var n) ? (n.GetString( ) ?? "") : "";
            doc.AuthorMid = author.TryGetProperty("mid", out var m)
                ? m.ValueKind == JsonValueKind.Number ? m.GetRawText( ) : (m.GetString( ) ?? "")
                : "";
        }

        doc.PublishTime = data.TryGetProperty("publish_time", out var pt) && pt.ValueKind == JsonValueKind.Number
            ? pt.GetInt64( )
            : (data.TryGetProperty("ctime", out var ct2) && ct2.ValueKind == JsonValueKind.Number ? ct2.GetInt64( ) : 0);

        if (data.TryGetProperty("tags", out var tags) && tags.ValueKind == JsonValueKind.Array)
        {
            foreach (var tag in tags.EnumerateArray( ))
            {
                var name = tag.TryGetProperty("name", out var tn) ? (tn.GetString( ) ?? "")
                           : (tag.TryGetProperty("show_text", out var st) ? (st.GetString( ) ?? "") : "");
                if (!string.IsNullOrEmpty(name))
                {
                    doc.Tags.Add(name);
                }
            }
        }

        // 首选 opus.content.paragraphs（已是段落化结构）
        if (data.TryGetProperty("opus", out var opus) && opus.ValueKind == JsonValueKind.Object
            && opus.TryGetProperty("content", out var opusContent) && opusContent.ValueKind == JsonValueKind.Object
            && opusContent.TryGetProperty("paragraphs", out var paras) && paras.ValueKind == JsonValueKind.Array
            && paras.GetArrayLength( ) > 0)
        {
            doc.Paragraphs = ParseParagraphs(paras);
            return doc;
        }

        // 退化为 data.content
        if (data.TryGetProperty("content", out var content))
        {
            var type = data.TryGetProperty("type", out var ty) && ty.ValueKind == JsonValueKind.Number ? ty.GetInt32( ) : 0;
            if (type == 0 && content.ValueKind == JsonValueKind.String)
            {
                LogWarn("旧版专栏（HTML），Markdown 转换为尽力而为。");
                doc.Paragraphs.Add(new OpusParagraph
                {
                    Kind = OpusParagraphKind.Text,
                    TextNodes = [new OpusTextNode { Text = OpusHtmlToMarkdown.Convert(content.GetString( ) ?? "") }],
                });
                return doc;
            }

            if (content.ValueKind == JsonValueKind.String)
            {
                LogWarn("专栏正文为 Quill Delta 格式，暂以纯文本尽力导出。");
                var text = ExtractQuillText(content.GetString( ) ?? "");
                doc.Paragraphs.Add(new OpusParagraph
                {
                    Kind = OpusParagraphKind.Text,
                    TextNodes = [new OpusTextNode { Text = text }],
                });
                return doc;
            }
        }

        throw new InvalidOperationException("专栏正文为空，可能是接口返回结构变化，请带 --debug 重试并反馈");
    }

    internal static OpusDocument ParseOpusDetail(JsonElement data, string sourceUrl)
    {
        var doc = new OpusDocument { SourceUrl = sourceUrl };

        if (data.TryGetProperty("item", out var item) && item.ValueKind == JsonValueKind.Object)
        {
            if (item.TryGetProperty("basic", out var basic) && basic.ValueKind == JsonValueKind.Object)
            {
                doc.Title = basic.TryGetProperty("title", out var t) ? (t.GetString( ) ?? "") : "";
                doc.OpusId = basic.TryGetProperty("comment_id_str", out var cid) ? (cid.GetString( ) ?? "") : "";
                doc.AuthorMid = basic.TryGetProperty("uid", out var uid) && uid.ValueKind == JsonValueKind.Number
                    ? uid.GetRawText( )
                    : (basic.TryGetProperty("uid", out var us) ? (us.GetString( ) ?? "") : "");
            }

            if (item.TryGetProperty("modules", out var modules) && modules.ValueKind == JsonValueKind.Array)
            {
                foreach (var module in modules.EnumerateArray( ))
                {
                    var moduleType = module.TryGetProperty("type", out var mt) ? (mt.GetString( ) ?? "") : "";
                    if (moduleType == "MODULE_TYPE_CONTENT"
                        && module.TryGetProperty("module_content", out var mc)
                        && mc.TryGetProperty("paragraphs", out var paras) && paras.ValueKind == JsonValueKind.Array)
                    {
                        doc.Paragraphs = ParseParagraphs(paras);
                    }
                    else if (moduleType == "MODULE_TYPE_AUTHOR" && module.TryGetProperty("module_author", out var ma))
                    {
                        doc.AuthorName = ma.TryGetProperty("name", out var n) ? (n.GetString( ) ?? "") : "";
                        if (string.IsNullOrEmpty(doc.AuthorMid))
                        {
                            doc.AuthorMid = ma.TryGetProperty("mid", out var m) && m.ValueKind == JsonValueKind.Number
                                ? m.GetRawText( )
                                : (ma.TryGetProperty("mid", out var ms) ? (ms.GetString( ) ?? "") : "");
                        }
                    }
                }
            }
        }

        if (doc.Paragraphs.Count == 0)
        {
            throw new InvalidOperationException("图文动态正文为空，可能是接口返回结构变化，请带 --debug 重试并反馈");
        }

        return doc;
    }

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

    // 结构探测优先于枚举硬编码：两套 schema（opus/detail 与 article/view）的字段与枚举含义不一致，
    // 先看存在哪个子对象，再决定渲染方式。
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

        if (para.TryGetProperty("para_type", out var ptDiv) && ptDiv.ValueKind == JsonValueKind.Number && ptDiv.GetInt32( ) == 3)
        {
            return new OpusParagraph { Kind = OpusParagraphKind.Divider };
        }

        if (para.TryGetProperty("para_type", out var ptQuote) && ptQuote.ValueKind == JsonValueKind.Number && ptQuote.GetInt32( ) == 4)
        {
            var quote = new OpusParagraph { Kind = OpusParagraphKind.Quote };
            if (para.TryGetProperty("text", out var qt) && qt.TryGetProperty("nodes", out var qn))
            {
                quote.TextNodes = ParseNodes(qn);
            }

            return quote;
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
            p.LinkTitle = card.TryGetProperty("title", out var t) ? (t.GetString( ) ?? "") : "";
            var url = card.TryGetProperty("jump_url", out var ju) ? (ju.GetString( ) ?? "")
                       : (linkCard.TryGetProperty("jump_url", out var lju) ? (lju.GetString( ) ?? "") : "");
            p.LinkUrl = url;
        }

        return p;
    }

    private static string ExtractQuillText(string deltaJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(deltaJson);
            var sb = new StringBuilder( );
            if (doc.RootElement.TryGetProperty("ops", out var ops) && ops.ValueKind == JsonValueKind.Array)
            {
                foreach (var op in ops.EnumerateArray( ))
                {
                    if (op.TryGetProperty("insert", out var ins) && ins.ValueKind == JsonValueKind.String)
                    {
                        sb.Append(ins.GetString( ));
                    }
                }
            }

            return sb.ToString( );
        }
        catch
        {
            return deltaJson;
        }
    }

    private static Exception Friendly(Exception ex)
    {
        if (ex is InvalidOperationException ioe && ioe.Message.Contains("code="))
        {
            var m = OpusRegexes.CodeInMessage( ).Match(ioe.Message);
            if (m.Success)
            {
                var hint = m.Groups[1].Value switch
                {
                    "-352" => "触发风控(-352)，请稍后重试或使用 BBDown login 登录后再试",
                    "-412" => "请求被拦截(-412)，通常是 buvid3 缺失或请求过于频繁，稍等几分钟后重试",
                    "-404" => "专栏不存在或已被删除",
                    "-403" or "62002" => "专栏已被设为私密或无权访问，请先登录",
                    _ => "",
                };
                if (!string.IsNullOrEmpty(hint))
                {
                    return new InvalidOperationException(hint);
                }
            }
        }

        return ex;
    }
}
