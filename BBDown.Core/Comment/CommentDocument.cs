using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace BBDown.Core.Comment;

/// <summary>
/// 一份评论区的导出结果。JSON 与 TXT 两种产物都由它渲染而来
/// </summary>
public sealed class CommentDocument
{
    /// <summary>评论区类型代码：1 视频稿件 / 12 专栏 / 17 纯文字动态，见 bilibili-API-collect comment 类型表</summary>
    public int Type { get; set; }
    /// <summary>评论区对象 id：视频为 avid、专栏为 cvid、图文动态为相簿 id</summary>
    public string Oid { get; set; } = "";
    public string Bvid { get; set; } = "";
    public string Title { get; set; } = "";
    /// <summary>hot 或 time，与 --comment-sort 一致</summary>
    public string Sort { get; set; } = "";
    /// <summary>服务端声称的评论总数（含楼中楼），未取到时为 0</summary>
    public int AllCount { get; set; }
    public long FetchedAt { get; set; }
    public List<CommentItem> Comments { get; } = [];
}

/// <summary>
/// 一条评论。楼中楼与主评论结构一致，故自递归复用同一类型
/// </summary>
public sealed class CommentItem
{
    public string Rpid { get; set; } = "";
    public string Mid { get; set; } = "";
    public string Uname { get; set; } = "";
    public int Level { get; set; }
    public long Ctime { get; set; }
    public int Like { get; set; }
    /// <summary>服务端声称的楼中楼总数，可能大于 <see cref="Replies"/> 的实际条数</summary>
    public int ReplyCount { get; set; }
    public bool UpLiked { get; set; }
    public bool Top { get; set; }
    /// <summary>形如「IP 属地：河北」；未登录时服务端不下发，为空串</summary>
    public string Location { get; set; } = "";
    public string Message { get; set; } = "";
    public List<string> Pictures { get; } = [];
    public List<CommentItem> Replies { get; } = [];
}

[JsonSerializable(typeof(CommentDocument))]
public sealed partial class CommentJsonContext : JsonSerializerContext;
