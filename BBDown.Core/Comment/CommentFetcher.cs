using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core.Util;

using static BBDown.Core.Logger;

namespace BBDown.Core.Comment;

/// <summary>
/// 抓取视频稿件的评论区（<c>/x/v2/reply/wbi/main</c>，游标分页 + WBI 签名）。
/// 抓取失败一律降级为「拿到多少算多少」，只有签名错误会抛出——那属于程序缺陷而非站点状态。
/// </summary>
public static class CommentFetcher
{
    private const int PageDelayMs = 350;
    private const int SubReplyDelayMs = 250;
    private const int SubReplyPageSize = 20;
    // 单条评论的楼中楼上限：热门视频里个别楼层能堆到数万条，抓全会让整个任务失去响应
    private const int MaxSubReplies = 500;
    // 网页评论区固定携带，缺失时部分账号会被判为非法来源
    private const string WebLocation = "1315875";

    public static async Task<CommentDocument> FetchAsync(string oid, int limit, bool sortHot, bool fullReplies, AppConfig cfg, CancellationToken ct = default)
    {
        var mode = sortHot ? 3 : 2;
        var document = new CommentDocument
        {
            Aid = oid,
            Sort = sortHot ? "hot" : "time",
            FetchedAt = DateTimeOffset.Now.ToUnixTimeSeconds( )
        };

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var offset = "";
        var firstPage = true;

        while (document.Comments.Count < limit)
        {
            var query = SignUtil.WbiSignedQuery(
                [
                    new("type", "1"),
                    new("oid", oid),
                    new("mode", mode.ToString(CultureInfo.InvariantCulture)),
                    new("plat", "1"),
                    new("seek_rpid", ""),
                    new("web_location", WebLocation),
                    new("pagination_str", PaginationStr(offset)),
                ],
                cfg);

            using var response = await HTTPUtil.GetJsonAsync($"{BiliApi.ReplyWbiMain}?{query}", cfg, ct);
            var (code, message) = JsonUtil.ReadApiError(response.RootElement);
            switch (code)
            {
                case 0:
                    break;
                case 12002:
                    LogWarn("该视频的评论区已关闭");
                    return document;
                case -404:
                    LogWarn("找不到该视频的评论区");
                    return document;
                case -403:
                    throw new InvalidOperationException("评论接口拒绝了 WBI 签名，请提交 issue");
                case -352 or -412:
                    LogWarn($"评论接口被风控拦截，已抓取 {document.Comments.Count} 条");
                    return document;
                default:
                    throw new InvalidOperationException($"获取评论失败(code={code})：{message}");
            }

            var data = Child(response.RootElement, "data");
            if (!JsonUtil.TryGetArray(data, "replies", out var replies))
            {
                // code 为 0 但 replies 为 null：风控下发 v_voucher 时的形态，继续翻页只会空转
                LogWarn($"评论接口未返回列表（可能触发风控），已抓取 {document.Comments.Count} 条");
                return document;
            }

            var cursor = Child(data, "cursor");
            if (firstPage)
            {
                document.AllCount = (int) ReadNumber(cursor, "all_count");
                LogDebug("评论区置顶节点：{0}", Child(data, "top").ToString( ));
                Take(Child(Child(data, "top"), "upper"), top: true);
                foreach (var pinned in JsonUtil.EnumerateArrayOrEmpty(Child(data, "top_replies")))
                {
                    Take(pinned, top: true);
                }

                firstPage = false;
            }

            var before = document.Comments.Count;
            foreach (var reply in replies.EnumerateArray( ))
            {
                if (document.Comments.Count >= limit)
                {
                    break;
                }

                Take(reply, top: false);
            }

            if (document.Comments.Count >= limit)
            {
                break;
            }

            var nextOffset = ReadString(Child(cursor, "pagination_reply"), "next_offset");
            if (nextOffset.Length == 0)
            {
                nextOffset = BuildOffset(mode, ReadNumber(cursor, "next"));
            }

            // is_end 在懒加载接口下的可靠性未经实测，故再叠三重兜底防止空转
            if (ReadBool(cursor, "is_end")
                || document.Comments.Count == before
                || nextOffset == offset)
            {
                break;
            }

            offset = nextOffset;
            await Task.Delay(PageDelayMs, ct);
        }

        if (fullReplies)
        {
            await FetchSubRepliesAsync(oid, document, cfg, ct);
        }

        return document;

        void Take(JsonElement node, bool top)
        {
            if (document.Comments.Count >= limit)
            {
                return;
            }

            var item = MapItem(node);
            if (item == null || !seen.Add(item.Rpid))
            {
                return;
            }

            item.Top = top;
            document.Comments.Add(item);
        }
    }

    /// <summary>
    /// 逐条把楼中楼抓全（<c>/x/v2/reply/reply</c>，无需签名）。请求量与评论条数成正比，故全程串行并限速。
    /// </summary>
    private static async Task FetchSubRepliesAsync(string oid, CommentDocument document, AppConfig cfg, CancellationToken ct)
    {
        var targets = document.Comments.FindAll(c => c.ReplyCount > c.Replies.Count);
        if (targets.Count == 0)
        {
            return;
        }

        Log($"开始抓取 {targets.Count} 条评论的楼中楼回复，可能耗时较长...");
        foreach (var comment in targets)
        {
            // 主接口内联的是热门前几条，与完整列表重叠，先清空再重建避免重复
            comment.Replies.Clear( );
            for (var pn = 1; comment.Replies.Count < comment.ReplyCount && comment.Replies.Count < MaxSubReplies; pn++)
            {
                var url = $"{BiliApi.ReplyReply}?type=1&oid={oid}&root={comment.Rpid}&ps={SubReplyPageSize}&pn={pn}";
                using var response = await HTTPUtil.GetJsonAsync(url, cfg, ct);
                var (code, message) = JsonUtil.ReadApiError(response.RootElement);
                if (code != 0)
                {
                    LogWarn($"抓取 rpid={comment.Rpid} 的楼中楼失败(code={code})：{message}");
                    break;
                }

                var replies = JsonUtil.ArrayAtPath(response.RootElement, "data", "replies");
                if (replies is not { Count: > 0 })
                {
                    // 该接口不返回 is_end，只能靠空页收尾
                    break;
                }

                foreach (var reply in replies)
                {
                    var item = MapItem(reply);
                    if (item != null)
                    {
                        comment.Replies.Add(item);
                    }
                }

                await Task.Delay(SubReplyDelayMs, ct);
            }
        }
    }

    /// <summary>
    /// 游标参数的外层包装：<c>offset</c> 的值本身是一段 JSON 文本，必须作为字符串值嵌套，
    /// 手拼引号会在 next_offset 含转义字符时产出非法 JSON。
    /// </summary>
    internal static string PaginationStr(string offset)
    {
        var buffer = new ArrayBufferWriter<byte>( );
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject( );
            writer.WriteString("offset", offset);
            writer.WriteEndObject( );
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>
    /// 服务端未给 next_offset 时自行拼游标。
    /// </summary>
    /// <remarks>
    /// mode=2 的内层键是大写的 <c>Data</c>，见 bilibili-API-collect/docs/comment/list.md（文档明确标注非笔误）。
    /// </remarks>
    internal static string BuildOffset(int mode, long next)
    {
        var byTime = mode == 2;
        var buffer = new ArrayBufferWriter<byte>( );
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject( );
            writer.WriteNumber("type", byTime ? 3 : 1);
            writer.WriteNumber("direction", 1);
            writer.WriteStartObject(byTime ? "Data" : "data");
            writer.WriteNumber(byTime ? "cursor" : "pn", next);
            writer.WriteEndObject( );
            writer.WriteEndObject( );
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static CommentItem? MapItem(JsonElement node)
    {
        var rpid = ReadId(node, "rpid_str", "rpid");
        if (rpid.Length == 0)
        {
            return null;
        }

        var member = Child(node, "member");
        var content = Child(node, "content");
        var item = new CommentItem
        {
            Rpid = rpid,
            Mid = ReadId(node, "mid_str", "mid"),
            Uname = ReadString(member, "uname"),
            Level = (int) ReadNumber(Child(member, "level_info"), "current_level"),
            Ctime = ReadNumber(node, "ctime"),
            Like = (int) ReadNumber(node, "like"),
            ReplyCount = (int) ReadNumber(node, "rcount"),
            UpLiked = ReadBool(Child(node, "up_action"), "like"),
            Location = ReadString(Child(node, "reply_control"), "location"),
            Message = ReadString(content, "message")
        };

        foreach (var picture in JsonUtil.EnumerateArrayOrEmpty(Child(content, "pictures")))
        {
            var source = ReadString(picture, "img_src");
            if (source.Length != 0)
            {
                item.Pictures.Add(source);
            }
        }

        foreach (var reply in JsonUtil.EnumerateArrayOrEmpty(Child(node, "replies")))
        {
            var child = MapItem(reply);
            if (child != null)
            {
                item.Replies.Add(child);
            }
        }

        return item;
    }

    // 大数 id 在 JSON 里同时有字符串与数字两种形态，字符串形态才是权威值（数字形态存在精度风险）
    private static string ReadId(JsonElement parent, string stringName, string numberName)
    {
        var text = ReadString(parent, stringName);
        if (text.Length != 0)
        {
            return text;
        }

        var number = ReadNumber(parent, numberName);
        return number == 0 ? "" : number.ToString(CultureInfo.InvariantCulture);
    }

    private static JsonElement Child(JsonElement parent, string name)
    {
        return parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var child) ? child : default;
    }

    private static string ReadString(JsonElement parent, string name)
    {
        return parent.ValueKind == JsonValueKind.Object
               && parent.TryGetProperty(name, out var value)
               && value.ValueKind == JsonValueKind.String
            ? value.GetString( )!
            : "";
    }

    // TryGetInt64 在 ValueKind 非 Number 时抛异常而不是返回 false，必须先判类型
    private static long ReadNumber(JsonElement parent, string name)
    {
        return parent.ValueKind == JsonValueKind.Object
               && parent.TryGetProperty(name, out var value)
               && value.ValueKind == JsonValueKind.Number
               && value.TryGetInt64(out var number)
            ? number
            : 0;
    }

    private static bool ReadBool(JsonElement parent, string name)
    {
        return parent.ValueKind == JsonValueKind.Object
               && parent.TryGetProperty(name, out var value)
               && value.ValueKind == JsonValueKind.True;
    }
}
