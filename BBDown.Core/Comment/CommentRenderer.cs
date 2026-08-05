using System;
using System.Globalization;
using System.Text;

namespace BBDown.Core.Comment;

/// <summary>
/// 把 <see cref="CommentDocument"/> 渲染成便于阅读的纯文本。纯函数，不触碰 IO。
/// </summary>
public static class CommentRenderer
{
    private const string ReplyIndent = "      ";
    private const string BodyIndent = "    ";

    public static string Render(CommentDocument document, bool fullReplies)
    {
        var text = new StringBuilder( );
        text.Append("# ").AppendLine(document.Title);
        text.Append("# ")
            .Append(document.Bvid.Length == 0 ? $"av{document.Aid}" : $"{document.Bvid} (av{document.Aid})")
            .Append(" | 排序：").Append(document.Sort == "time" ? "最新" : "热度")
            .Append(" | 已抓取 ").Append(document.Comments.Count);
        if (document.AllCount > 0)
        {
            text.Append(" / 全部 ").Append(document.AllCount);
        }

        text.Append(" 条 | 导出于 ").AppendLine(FormatTime(document.FetchedAt));

        var index = 0;
        foreach (var comment in document.Comments)
        {
            index++;
            text.AppendLine( );
            text.Append('[').Append(index.ToString(CultureInfo.InvariantCulture)).Append("] ").AppendLine(Headline(comment));
            AppendMessage(text, comment, BodyIndent);

            foreach (var reply in comment.Replies)
            {
                text.Append(BodyIndent).Append("└ ").AppendLine(Headline(reply));
                AppendMessage(text, reply, ReplyIndent);
            }

            if (!fullReplies && comment.ReplyCount > comment.Replies.Count)
            {
                text.Append(BodyIndent)
                    .Append("（共 ").Append(comment.ReplyCount.ToString(CultureInfo.InvariantCulture))
                    .Append(" 条回复，此处仅显示 ").Append(comment.Replies.Count.ToString(CultureInfo.InvariantCulture))
                    .AppendLine(" 条，加 --full-comment 抓取全部）");
            }
        }

        return text.ToString( );
    }

    private static string Headline(CommentItem comment)
    {
        var line = new StringBuilder(comment.Uname);
        if (comment.Level > 0)
        {
            line.Append(" Lv").Append(comment.Level.ToString(CultureInfo.InvariantCulture));
        }

        // 未登录时服务端不下发 location，此时整段省略而不是留个空壳
        if (comment.Location.Length != 0)
        {
            line.Append(' ').Append(comment.Location);
        }

        line.Append(" · ").Append(FormatTime(comment.Ctime));
        line.Append(" · 赞 ").Append(comment.Like.ToString(CultureInfo.InvariantCulture));
        if (comment.ReplyCount > 0)
        {
            line.Append(" · 回复 ").Append(comment.ReplyCount.ToString(CultureInfo.InvariantCulture));
        }

        if (comment.Top)
        {
            line.Append(" · 置顶");
        }

        if (comment.UpLiked)
        {
            line.Append(" · UP 主觉得很赞");
        }

        return line.ToString( );
    }

    private static void AppendMessage(StringBuilder text, CommentItem comment, string indent)
    {
        foreach (var line in comment.Message.Replace("\r\n", "\n").Split('\n'))
        {
            text.Append(indent).AppendLine(line);
        }

        foreach (var picture in comment.Pictures)
        {
            text.Append(indent).Append("[图片] ").AppendLine(picture);
        }
    }

    private static string FormatTime(long unixSeconds)
    {
        return unixSeconds <= 0
            ? "未知时间"
            : DateTimeOffset.FromUnixTimeSeconds(unixSeconds).ToLocalTime( ).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    }
}
