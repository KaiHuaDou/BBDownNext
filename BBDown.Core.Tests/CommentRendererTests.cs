using System.Collections.Generic;
using System.Text.Encodings.Web;
using System.Text.Json;

using BBDown.Core.Comment;

using Xunit;

namespace BBDown.Core.Tests;

public class CommentRendererTests
{
    private static CommentDocument SampleDoc(bool withReply = true)
    {
        var doc = new CommentDocument
        {
            Aid = "170001",
            Bvid = "BV1xx",
            Title = "测试标题",
            Sort = "hot",
            AllCount = 50,
            FetchedAt = 1700000000,
        };

        var comment = new CommentItem
        {
            Rpid = "1",
            Uname = "甲",
            Level = 5,
            Ctime = 1700000100,
            Like = 123,
            // 声称 2 条但内联只拿到 1 条，才会显示「加 --full-comment 抓取全部」提示
            ReplyCount = withReply ? 2 : 0,
            Location = "IP属地：河北",
            Message = "第一行\n第二行",
        };
        if (withReply)
        {
            comment.Replies.Add(new CommentItem { Rpid = "2", Uname = "乙", Level = 3, Ctime = 1700000200, Like = 5, Message = "回复内容" });
        }

        doc.Comments.Add(comment);
        return doc;
    }

    [Fact]
    public void Render_IncludesHeadlineAndIndentsReplies( )
    {
        var text = CommentRenderer.Render(SampleDoc( ), fullReplies: false);
        Assert.Contains("甲", text);
        Assert.Contains("└", text);
        Assert.Contains("乙", text);
    }

    [Fact]
    public void Render_OmitsLocationWhenEmpty( )
    {
        var doc = SampleDoc(false);
        doc.Comments[0].Location = "";
        var text = CommentRenderer.Render(doc, fullReplies: false);
        Assert.DoesNotContain("IP属地", text);
    }

    [Fact]
    public void Render_MultiLineMessageIndented( )
    {
        var text = CommentRenderer.Render(SampleDoc(false), fullReplies: false);
        Assert.Contains("\n    第一行", text);
        Assert.Contains("\n    第二行", text);
    }

    [Fact]
    public void Render_FullRepliesOmitsHint( )
    {
        const string hint = "加 --full-comment 抓取全部";
        Assert.Contains(hint, CommentRenderer.Render(SampleDoc(true), fullReplies: false));
        Assert.DoesNotContain(hint, CommentRenderer.Render(SampleDoc(true), fullReplies: true));
    }

    [Fact]
    public void JsonContext_KeepsChineseUnescaped( )
    {
        var context = new CommentJsonContext(new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
        var json = JsonSerializer.Serialize(SampleDoc(false), context.CommentDocument);
        Assert.Contains("测试标题", json); // 未被转义成 \u4e2d\u6587
        Assert.DoesNotContain("\\u6d4b", json); // 确认没有走默认转义
        Assert.Contains("\"Aid\"", json);
    }
}
