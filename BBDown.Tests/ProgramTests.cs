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
    public void DetermineApiType_DefaultsToWeb( )
    {
        var o = new DownloadOptions( );
        Assert.Equal("WEB", VideoInfo.DetermineApiType(o));
    }

    [Fact]
    public void DetermineApiType_TvApi( )
    {
        var o = new DownloadOptions { UseTvApi = true };
        Assert.Equal("TV", VideoInfo.DetermineApiType(o));
    }

    [Fact]
    public void DetermineApiType_AppApi( )
    {
        var o = new DownloadOptions { UseAppApi = true };
        Assert.Equal("APP", VideoInfo.DetermineApiType(o));
    }

    [Fact]
    public void DetermineApiType_IntlApi( )
    {
        var o = new DownloadOptions { UseIntlApi = true };
        Assert.Equal("INTL", VideoInfo.DetermineApiType(o));
    }

    [Fact]
    public void ParseEncodingPriority_NullInput_YieldsEmpty( )
    {
        var o = new DownloadOptions { EncodingPriority = null };
        var (encodingPriority, firstEncoding) = WorkSetup.ParseEncodingPriority(o);
        Assert.Empty(encodingPriority);
        Assert.Equal("", firstEncoding);
    }

    [Fact]
    public void ParseEncodingPriority_ParsesPriorityAndFirst( )
    {
        var o = new DownloadOptions { EncodingPriority = "avc,hevc,av1" };
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
        var o = new DownloadOptions { EncodingPriority = "avc，hevc, av1 " };
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
        var o = new DownloadOptions { EncodingPriority = "avc,avc,hevc" };
        var (encodingPriority, _) = WorkSetup.ParseEncodingPriority(o);
        Assert.Equal(2, encodingPriority.Count);
        Assert.Equal((byte) 0, encodingPriority["AVC"]);
        Assert.Equal((byte) 1, encodingPriority["HEVC"]);
    }

    [Fact]
    public void ParseDfnPriority_NullInput_YieldsEmpty( )
    {
        var o = new DownloadOptions { DfnPriority = null };
        Assert.Empty(WorkSetup.ParseDfnPriority(o));
    }

    [Fact]
    public void ParseDfnPriority_ParsesPriority( )
    {
        var o = new DownloadOptions { DfnPriority = "1080p,720p,360p" };
        var dfn = WorkSetup.ParseDfnPriority(o);
        Assert.Equal(3, dfn.Count);
        Assert.Equal(0, dfn["1080P"]);
        Assert.Equal(1, dfn["720P"]);
        Assert.Equal(2, dfn["360P"]);
    }

    [Fact]
    public void ParseDownloadDanmakuFormats_EmptyInput_UsesDefault( )
    {
        var o = new DownloadOptions { DownloadDanmakuFormats = null };
        var formats = WorkSetup.ParseDownloadDanmakuFormats(o);
        Assert.Equal(DanmakuFormatInfo.DefaultFormats, formats);
    }

    [Fact]
    public void ParseDownloadDanmakuFormats_ParsesExplicit( )
    {
        var o = new DownloadOptions { DownloadDanmakuFormats = "xml,ass" };
        var formats = WorkSetup.ParseDownloadDanmakuFormats(o);
        Assert.Contains(DanmakuFormat.Xml, formats);
        Assert.Contains(DanmakuFormat.Ass, formats);
    }

    [Fact]
    public void ParseDownloadDanmakuFormats_InvalidFallsBackToDefault( )
    {
        var o = new DownloadOptions { DownloadDanmakuFormats = "xml,bogus" };
        var formats = WorkSetup.ParseDownloadDanmakuFormats(o);
        Assert.Equal(DanmakuFormatInfo.DefaultFormats, formats);
    }

    // 评论与弹幕是两个互不相干的特性，解析逻辑各自独立：下列断言不引用任何 Danmaku 类型
    [Fact]
    public void ParseCommentFormats_EmptyInput_UsesDefault( )
    {
        var o = new DownloadOptions { CommentFormats = null };
        Assert.Equal(CommentFormatInfo.DefaultFormats, WorkSetup.ParseCommentFormats(o));
    }

    [Fact]
    public void ParseCommentFormats_ParsesExplicit( )
    {
        var o = new DownloadOptions { CommentFormats = "json,txt" };
        var formats = WorkSetup.ParseCommentFormats(o);
        Assert.Contains(CommentFormat.Json, formats);
        Assert.Contains(CommentFormat.Txt, formats);
    }

    [Fact]
    public void ParseCommentFormats_CaseInsensitiveAndDeduped( )
    {
        var o = new DownloadOptions { CommentFormats = "TXT,json,txt" };
        var formats = WorkSetup.ParseCommentFormats(o);
        Assert.Equal([CommentFormat.Txt, CommentFormat.Json], formats); // 去重后保持首次出现顺序
    }

    [Fact]
    public void ParseCommentFormats_InvalidFallsBackToDefault( )
    {
        var o = new DownloadOptions { CommentFormats = "json,bogus" };
        Assert.Equal(CommentFormatInfo.DefaultFormats, WorkSetup.ParseCommentFormats(o));
    }

    [Fact]
    public void HandleConflictingOptions_InteractiveForcesShowStreams( )
    {
        var o = new DownloadOptions { Interactive = true, HideStreams = true };
        WorkSetup.HandleConflictingOptions(o);
        Assert.False(o.HideStreams);
    }

    [Fact]
    public void HandleConflictingOptions_AudioOnlyAndVideoOnlyBothCleared( )
    {
        var o = new DownloadOptions { AudioOnly = true, VideoOnly = true };
        WorkSetup.HandleConflictingOptions(o);
        Assert.False(o.AudioOnly);
        Assert.False(o.VideoOnly);
    }

    [Fact]
    public void HandleConflictingOptions_NoSubClearsSubOnly( )
    {
        var o = new DownloadOptions { NoSub = true, SubOnly = true };
        WorkSetup.HandleConflictingOptions(o);
        Assert.False(o.SubOnly);
    }

    [Fact]
    public void ResolveToolPaths_ExplicitPathsWin( )
    {
        using var fake = new FakeExecutable( );
        var o = new DownloadOptions { FFmpegPath = fake.Path, Mp4boxPath = fake.Path, UseAria2c = true, Aria2cPath = fake.Path };

        var tools = WorkSetup.ResolveToolPaths(o);

        Assert.Equal(fake.Path, tools.Ffmpeg);
        Assert.Equal(fake.Path, tools.Mp4box);
        Assert.Equal(fake.Path, tools.Aria2c);
    }

    [Fact]
    public void ResolveToolPaths_WithoutAria2cLeavesPathNull( )
    {
        using var fake = new FakeExecutable( );
        var o = new DownloadOptions { FFmpegPath = fake.Path, UseAria2c = false };

        Assert.Null(WorkSetup.ResolveToolPaths(o).Aria2c);
    }

    [Fact]
    public void ResolveToolPaths_MissingFFmpegThrowsUnlessSkipMux( )
    {
        var o = new DownloadOptions { FFmpegPath = Path.Combine(Path.GetTempPath( ), "bbdown-not-here-" + Guid.NewGuid( ).ToString("N")) };

        // 不混流时不需要 ffmpeg；需要混流却找不到必须立刻炸，而不是下载完才失败
        o.SkipMux = true;
        WorkSetup.ResolveToolPaths(o);

        o.SkipMux = false;
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

        var first = WorkSetup.ResolveToolPaths(new DownloadOptions { FFmpegPath = a.Path, Mp4boxPath = a.Path });
        var second = WorkSetup.ResolveToolPaths(new DownloadOptions { FFmpegPath = b.Path, Mp4boxPath = b.Path });

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
