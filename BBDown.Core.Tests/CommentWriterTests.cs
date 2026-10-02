using System.IO;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core.Comment;

namespace BBDown.Core.Tests;

public class CommentWriterTests
{
    private static CommentDocument SampleDocument( )
    {
        var doc = new CommentDocument
        {
            Type = 12,
            Oid = "5806746",
            Title = "测试专栏",
            Sort = "hot",
            AllCount = 1,
            FetchedAt = 1700000000,
        };
        doc.Comments.Add(new CommentItem { Rpid = "1", Uname = "甲", Message = "内容" });
        return doc;
    }

    [Fact]
    public async Task WriteAsync_BothFormats_ProduceFilesWithCommentSuffix( )
    {
        using var dir = new TempDir( );
        var token = TestContext.Current.CancellationToken;
        var basePath = Path.Combine(dir.FullPath, "标题_5806746.md");

        var paths = await CommentWriter.WriteAsync(
            basePath, SampleDocument( ), [CommentFormat.Json, CommentFormat.Txt], fullReplies: false, token);

        var jsonPath = Path.ChangeExtension(basePath, ".comments.json");
        var txtPath = Path.ChangeExtension(basePath, ".comments.txt");
        Assert.Equal([jsonPath, txtPath], paths);
        Assert.Contains("测试专栏", await File.ReadAllTextAsync(jsonPath, token));
        Assert.Contains("\"Oid\": \"5806746\"", await File.ReadAllTextAsync(jsonPath, token));
        Assert.Contains("# cv5806746 |", await File.ReadAllTextAsync(txtPath, token));
        Assert.Contains("甲", await File.ReadAllTextAsync(txtPath, token));
    }

    [Fact]
    public async Task WriteAsync_MdBaseName_ChangeExtensionKeepsBase( )
    {
        using var dir = new TempDir( );
        var basePath = Path.Combine(dir.FullPath, "a.b.c.md");

        // 基底已带多段扩展名时换后缀只替换最后一段，产物与 md 同名不同后缀
        var paths = await CommentWriter.WriteAsync(
            basePath, SampleDocument( ), [CommentFormat.Json], fullReplies: false, TestContext.Current.CancellationToken);

        Assert.Equal([Path.Combine(dir.FullPath, "a.b.c.comments.json")], paths);
    }

    [Fact]
    public async Task WriteAsync_EmptyFormats_WritesNothing( )
    {
        using var dir = new TempDir( );
        var basePath = Path.Combine(dir.FullPath, "x.md");

        var paths = await CommentWriter.WriteAsync(
            basePath, SampleDocument( ), [ ], fullReplies: false, TestContext.Current.CancellationToken);

        Assert.Empty(paths);
        Assert.Empty(Directory.GetFiles(dir.FullPath));
    }
}
