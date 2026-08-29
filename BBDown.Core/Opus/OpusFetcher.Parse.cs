using System;
using System.Text;
using System.Text.Json;

using static BBDown.Core.Logger;
using static BBDown.Core.Util.JsonUtil;

namespace BBDown.Core.Opus;

// 文档级解析：article/view（专栏正文）与 opus/detail（图文动态）的 OpusDocument 构造
public static partial class OpusFetcher
{
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
        FillArticleMeta(doc, data);

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
                    TextNodes = [new OpusTextNode { Text = OpusHtmlToMarkdown.Convert(content.GetString( ) ?? ""), IsRawMarkdown = true }],
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

    private static void FillArticleMeta(OpusDocument doc, JsonElement data)
    {
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
                if (tag.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var name = tag.TryGetProperty("name", out var tn) ? (tn.GetString( ) ?? "")
                           : (tag.TryGetProperty("show_text", out var st) ? (st.GetString( ) ?? "") : "");
                if (!string.IsNullOrEmpty(name))
                {
                    doc.Tags.Add(name);
                }
            }
        }
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
                    if (module.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var moduleType = module.TryGetProperty("module_type", out var mt) ? (mt.GetString( ) ?? "") : "";
                    if (moduleType == "MODULE_TYPE_CONTENT"
                        && TryGetObject(module, "module_content", out var mc)
                        && mc.TryGetProperty("paragraphs", out var paras) && paras.ValueKind == JsonValueKind.Array)
                    {
                        doc.Paragraphs = ParseParagraphs(paras);
                    }
                    else if (moduleType == "MODULE_TYPE_AUTHOR" && TryGetObject(module, "module_author", out var ma))
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

        // 极简动态可能没有正文段落：返回仅含元信息的空文档供前端渲染空正文，仅当整体 item 缺失才算结构异常
        if (doc.Paragraphs.Count == 0 && !data.TryGetProperty("item", out _))
        {
            throw new InvalidOperationException("图文动态正文为空，可能是接口返回结构变化，请带 --debug 重试并反馈");
        }

        return doc;
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
                    if (op.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

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
}
