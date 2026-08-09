using System;
using System.IO;

namespace BBDown.Tests;

public class ProgramTests
{
    // 退出码 2 是强断言：没有任何分 P 因真实故障失败，唯一原因是充电权限
    [Fact]
    public void IsChargedPreviewOnly_BareException_ReturnsTrue( )
    {
        Assert.True(Program.IsChargedPreviewOnly(new ChargedPreviewException("试看")));
    }

    [Fact]
    public void IsChargedPreviewOnly_AllInnerAreCharged_ReturnsTrue( )
    {
        var agg = new AggregateException(new ChargedPreviewException("P1"), new ChargedPreviewException("P2"));
        Assert.True(Program.IsChargedPreviewOnly(agg));
    }

    [Fact]
    public void IsChargedPreviewOnly_MixedWithRealFailure_ReturnsFalse( )
    {
        var agg = new AggregateException(new ChargedPreviewException("P1"), new IOException("网络炸了"));
        Assert.False(Program.IsChargedPreviewOnly(agg));
    }

    [Fact]
    public void IsChargedPreviewOnly_EmptyAggregate_ReturnsFalse( )
    {
        Assert.False(Program.IsChargedPreviewOnly(new AggregateException( )));
    }

    [Fact]
    public void IsChargedPreviewOnly_UnrelatedException_ReturnsFalse( )
    {
        Assert.False(Program.IsChargedPreviewOnly(new IOException( )));
    }

    [Fact]
    public void ParseEncodingPriority_NullInput_YieldsEmpty( )
    {
        var o = new DownloadRequest { EncodingPriority = null };
        var (encodingPriority, firstEncoding) = WorkSetup.ParseEncodingPriority(o);
        Assert.Empty(encodingPriority);
        Assert.Equal("", firstEncoding);
    }

    [Fact]
    public void ParseEncodingPriority_ParsesPriorityAndFirst( )
    {
        var o = new DownloadRequest { EncodingPriority = "avc,hevc,av1" };
        var (encodingPriority, firstEncoding) = WorkSetup.ParseEncodingPriority(o);
        Assert.Equal(3, encodingPriority.Count);
        Assert.Equal((byte) 0, encodingPriority["AVC"]);
        Assert.Equal((byte) 1, encodingPriority["HEVC"]);
        Assert.Equal((byte) 2, encodingPriority["AV1"]);
        Assert.Equal("AVC", firstEncoding);
    }

    [Fact]
    public void ParseEncodingPriority_CleansChineseCommaAndTrims( )
    {
        var o = new DownloadRequest { EncodingPriority = "avc，hevc, av1 " };
        var (encodingPriority, firstEncoding) = WorkSetup.ParseEncodingPriority(o);
        Assert.Equal(3, encodingPriority.Count);
        Assert.Equal((byte) 0, encodingPriority["AVC"]);
        Assert.Equal((byte) 1, encodingPriority["HEVC"]);
        Assert.Equal((byte) 2, encodingPriority["AV1"]);
        Assert.Equal("AVC", firstEncoding);
    }

    [Fact]
    public void ParseEncodingPriority_DedupesRepeatedEncoding( )
    {
        var o = new DownloadRequest { EncodingPriority = "avc,avc,hevc" };
        var (encodingPriority, _) = WorkSetup.ParseEncodingPriority(o);
        Assert.Equal(2, encodingPriority.Count);
        Assert.Equal((byte) 0, encodingPriority["AVC"]);
        Assert.Equal((byte) 1, encodingPriority["HEVC"]);
    }

    [Fact]
    public void ParseDfnPriority_NullInput_YieldsEmpty( )
    {
        var o = new DownloadRequest { DfnPriority = null };
        Assert.Empty(WorkSetup.ParseDfnPriority(o));
    }

    [Fact]
    public void ParseDfnPriority_ParsesPriority( )
    {
        var o = new DownloadRequest { DfnPriority = "1080p,720p,360p" };
        var dfn = WorkSetup.ParseDfnPriority(o);
        Assert.Equal(3, dfn.Count);
        Assert.Equal(0, dfn["1080P"]);
        Assert.Equal(1, dfn["720P"]);
        Assert.Equal(2, dfn["360P"]);
    }

    [Fact]
    public void ParseDownloadDanmakuFormats_EmptyInput_UsesDefault( )
    {
        var o = new DownloadRequest { DownloadDanmakuFormats = null };
        var formats = WorkSetup.ParseDownloadDanmakuFormats(o);
        Assert.Equal(DanmakuFormatInfo.DefaultFormats, formats);
    }

    [Fact]
    public void ParseDownloadDanmakuFormats_ParsesExplicit( )
    {
        var o = new DownloadRequest { DownloadDanmakuFormats = "xml,ass" };
        var formats = WorkSetup.ParseDownloadDanmakuFormats(o);
        Assert.Contains(DanmakuFormat.Xml, formats);
        Assert.Contains(DanmakuFormat.Ass, formats);
    }

    [Fact]
    public void ParseDownloadDanmakuFormats_InvalidFallsBackToDefault( )
    {
        var o = new DownloadRequest { DownloadDanmakuFormats = "xml,bogus" };
        var formats = WorkSetup.ParseDownloadDanmakuFormats(o);
        Assert.Equal(DanmakuFormatInfo.DefaultFormats, formats);
    }

    // 评论与弹幕是两个互不相干的特性，解析逻辑各自独立：下列断言不引用任何 Danmaku 类型
    [Fact]
    public void ParseCommentFormats_EmptyInput_UsesDefault( )
    {
        var o = new DownloadRequest { CommentFormats = null };
        Assert.Equal(CommentFormatInfo.DefaultFormats, WorkSetup.ParseCommentFormats(o));
    }

    [Fact]
    public void ParseCommentFormats_ParsesExplicit( )
    {
        var o = new DownloadRequest { CommentFormats = "json,txt" };
        var formats = WorkSetup.ParseCommentFormats(o);
        Assert.Contains(CommentFormat.Json, formats);
        Assert.Contains(CommentFormat.Txt, formats);
    }

    [Fact]
    public void ParseCommentFormats_CaseInsensitiveAndDeduped( )
    {
        var o = new DownloadRequest { CommentFormats = "TXT,json,txt" };
        var formats = WorkSetup.ParseCommentFormats(o);
        Assert.Equal([CommentFormat.Txt, CommentFormat.Json], formats); // 去重后保持首次出现顺序
    }

    [Fact]
    public void ParseCommentFormats_InvalidFallsBackToDefault( )
    {
        var o = new DownloadRequest { CommentFormats = "json,bogus" };
        Assert.Equal(CommentFormatInfo.DefaultFormats, WorkSetup.ParseCommentFormats(o));
    }

    [Fact]
    public void HandleConflictingOptions_InteractiveForcesShowStreams( )
    {
        var o = new DownloadRequest { InteractiveQuality = true, HideStreams = true };
        var r = WorkSetup.HandleConflictingOptions(o);
        Assert.False(r.HideStreams);
    }

    [Fact]
    public void HandleConflictingOptions_KeepsHideStreamsWhenNotInteractive( )
    {
        var o = new DownloadRequest { HideStreams = true };
        var r = WorkSetup.HandleConflictingOptions(o);
        Assert.True(r.HideStreams);
    }

    [Fact]
    public void ResolveToolPaths_ExplicitPathsWin( )
    {
        using var fake = new FakeExecutable( );
        var o = new DownloadRequest { FFmpegPath = fake.Path, Mp4boxPath = fake.Path, UseAria2c = true, Aria2cPath = fake.Path };

        var tools = WorkSetup.ResolveToolPaths(o);

        Assert.Equal(fake.Path, tools.Ffmpeg);
        Assert.Equal(fake.Path, tools.Mp4box);
        Assert.Equal(fake.Path, tools.Aria2c);
    }

    [Fact]
    public void ResolveToolPaths_WithoutAria2cLeavesPathNull( )
    {
        using var fake = new FakeExecutable( );
        var o = new DownloadRequest { FFmpegPath = fake.Path, UseAria2c = false };

        Assert.Null(WorkSetup.ResolveToolPaths(o).Aria2c);
    }

    [Fact]
    public void ResolveToolPaths_MissingFFmpegThrowsUnlessMuxNone( )
    {
        var o = new DownloadRequest { FFmpegPath = Path.Combine(Path.GetTempPath( ), "bbdown-not-here-" + Guid.NewGuid( ).ToString("N")) };

        // 不混流时不需要 ffmpeg；需要混流却找不到必须立刻炸，而不是下载完才失败
        o = o with { Mux = MuxMode.None };
        WorkSetup.ResolveToolPaths(o);

        o = o with { Mux = MuxMode.Mpeg4 };
        if (Utils.FindExecutable("ffmpeg") == null)
        {
            Assert.Throws<InvalidOperationException>(( ) => WorkSetup.ResolveToolPaths(o));
        }
    }

    // 每次解析都返回独立快照，不写任何进程级静态字段——serve 并发任务互不干扰的前提
    [Fact]
    public void ResolveToolPaths_SnapshotsAreIndependentPerCall( )
    {
        using var a = new FakeExecutable( );
        using var b = new FakeExecutable( );

        var first = WorkSetup.ResolveToolPaths(new DownloadRequest { FFmpegPath = a.Path, Mp4boxPath = a.Path });
        var second = WorkSetup.ResolveToolPaths(new DownloadRequest { FFmpegPath = b.Path, Mp4boxPath = b.Path });

        Assert.Equal(a.Path, first.Ffmpeg);
        Assert.Equal(b.Path, second.Ffmpeg);
        Assert.NotEqual(first.Ffmpeg, second.Ffmpeg);
    }

    private sealed class FakeExecutable : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath( ), "bbdown-tool-" + Guid.NewGuid( ).ToString("N"));

        public FakeExecutable( )
        {
            File.WriteAllText(Path, "");
        }

        public void Dispose( )
        {
            File.Delete(Path);
        }
    }
}
