using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

using BBDown.Core;

using static BBDown.Core.Entity.Entity;

namespace BBDown.Tests;

public class DownloadTests
{
    private static Page MakePage(int index = 1, string aid = "114514", string cid = "1919810", string title = "第一话", long pubTime = 0)
    {
        return new Page
        {
            index = index,
            aid = aid,
            cid = cid,
            epid = "",
            title = title,
            dur = 100,
            res = "1920x1080",
            pubTime = pubTime,
        };
    }

    private static Video MakeVideo(string id, string dfn, string codecs, long bandwidth)
    {
        return new Video { id = id, dfn = dfn, baseUrl = "", codecs = codecs, bandwidth = bandwidth };
    }

    private static Audio MakeAudio(string id, string codecs, long bandwidth)
    {
        return new Audio { id = id, dfn = "", baseUrl = "", codecs = codecs, bandwidth = bandwidth, dur = 100 };
    }

    // 服务器不支持 Range 时不应该再退避重试，Parallel.ForEachAsync 会把分片异常裹一层
    [Fact]
    public void IsRangeUnsupported_SeesThroughAggregateException( )
    {
        Assert.True(PageDownload.IsRangeUnsupported(new NotSupportedException( )));
        Assert.True(PageDownload.IsRangeUnsupported(new AggregateException(new IOException( ), new NotSupportedException( ))));
        Assert.False(PageDownload.IsRangeUnsupported(new IOException( )));
        Assert.False(PageDownload.IsRangeUnsupported(new AggregateException(new IOException( ))));
    }

    // 充电权限不会因重试改变，若漏进排除列表用户会白等两轮退避
    [Fact]
    public void ShouldRetry_ChargedPreview_ReturnsFalse( )
    {
        Assert.False(PageDownload.ShouldRetry(new ChargedPreviewException("试看"), CancellationToken.None));
        Assert.True(PageDownload.ShouldRetry(new IOException( ), CancellationToken.None));
    }

    [Theory]
    // 真实场景：02:23:48 的稿件只下发 00:06:29
    [InlineData(true, 8628, 389, true)]
    // 非充电专属稿件一律不判定，避免误伤 timelength 异常的普通视频
    [InlineData(false, 8628, 389, false)]
    // 番剧 / 互动视频的 dur 恒为 0
    [InlineData(true, 0, 389, false)]
    // playurl 未给出时长
    [InlineData(true, 8628, 0, false)]
    // timelength(ms) 与 duration(整秒) 的固有封装误差
    [InlineData(true, 3600, 3588, false)]
    // 短视频：比值虽超阈值，但绝对差不足 30 秒
    [InlineData(true, 60, 53, false)]
    // 比值边界：恰好 90%
    [InlineData(true, 300, 270, false)]
    public void IsTruncatedPreview_AppliesBothConditions(bool exclusive, int full, int actual, bool expected)
    {
        Assert.Equal(expected, PageDownload.IsTruncatedPreview(exclusive, full, actual));
    }

    [Fact]
    public void ApplyPreviewPrefix_MultiPage_PrefixesOnlyLastSegment( )
    {
        Assert.Equal("视频标题/[试看][P01]分P标题.mp4", SavePath.ApplyPreviewPrefix("视频标题/[P01]分P标题.mp4"));
    }

    [Fact]
    public void ApplyPreviewPrefix_SinglePage_PrefixesWholeName( )
    {
        Assert.Equal("[试看]视频标题.mp4", SavePath.ApplyPreviewPrefix("视频标题.mp4"));
    }

    [Fact]
    public void ResolveSavePathFormat_SinglePageUsesSingleDefault( )
    {
        var o = new DownloadOptions( );
        Assert.Equal(SavePath.SinglePageDefaultSavePath, SavePath.Resolve(o, 1, false, false));
    }

    [Fact]
    public void ResolveSavePathFormat_MultiPageUsesMultiDefault( )
    {
        var o = new DownloadOptions( );
        Assert.Equal(SavePath.MultiPageDefaultSavePath, SavePath.Resolve(o, 3, false, false));
    }

    [Fact]
    public void ResolveSavePathFormat_UnfinishedBangumiTreatedAsMultiPage( )
    {
        var o = new DownloadOptions( );
        Assert.Equal(SavePath.MultiPageDefaultSavePath, SavePath.Resolve(o, 1, true, false));
    }

    [Fact]
    public void ResolveSavePathFormat_FinishedSinglePageBangumiUsesSingleDefault( )
    {
        var o = new DownloadOptions( );
        Assert.Equal(SavePath.SinglePageDefaultSavePath, SavePath.Resolve(o, 1, true, true));
    }

    [Fact]
    public void ResolveSavePathFormat_UserPatternsWin( )
    {
        var o = new DownloadOptions { FilePattern = "<aid>", MultiFilePattern = "<aid>/<cid>" };
        Assert.Equal("<aid>", SavePath.Resolve(o, 1, false, false));
        Assert.Equal("<aid>/<cid>", SavePath.Resolve(o, 2, false, false));
    }

    [Fact]
    public void BuildEpisodeTitle_SinglePageReturnsEmpty( )
    {
        Assert.Equal("", PageDownload.BuildEpisodeTitle(MakePage( ), 1, false, false));
    }

    [Fact]
    public void BuildEpisodeTitle_MultiPageReturnsPageTitle( )
    {
        Assert.Equal("第一话", PageDownload.BuildEpisodeTitle(MakePage( ), 5, false, false));
    }

    [Fact]
    public void BuildEpisodeTitle_UnfinishedBangumiReturnsPageTitle( )
    {
        Assert.Equal("第一话", PageDownload.BuildEpisodeTitle(MakePage( ), 1, true, false));
    }

    [Fact]
    public void ShouldDeleteCover_SinglePage( )
    {
        var p = MakePage( );
        Assert.True(PageDownload.ShouldDeleteCover(p, [p]));
    }

    [Fact]
    public void ShouldDeleteCover_LastPageOfSameAid( )
    {
        var p1 = MakePage(1);
        var p2 = MakePage(2, cid: "2");
        Assert.True(PageDownload.ShouldDeleteCover(p2, [p1, p2]));
    }

    [Fact]
    public void ShouldDeleteCover_MiddlePageOfSameAidKeepsCover( )
    {
        var p1 = MakePage(1);
        var p2 = MakePage(2, cid: "2");
        Assert.False(PageDownload.ShouldDeleteCover(p1, [p1, p2]));
    }

    [Fact]
    public void ShouldDeleteCover_DifferentAidAlwaysDeletes( )
    {
        var p1 = MakePage(1, aid: "1");
        var p2 = MakePage(2, aid: "2");
        Assert.True(PageDownload.ShouldDeleteCover(p1, [p1, p2]));
    }

    [Fact]
    public void ToAudioOnlyPath_ReplacesExtension( )
    {
        Assert.Equal("out/video.m4a", MuxFinish.ToAudioOnlyPath("out/video.mp4"));
    }

    [Fact]
    public void ToAudioOnlyPath_DoesNotAssumeFourCharExtension( )
    {
        Assert.Equal("out/video.m4a", MuxFinish.ToAudioOnlyPath("out/video.MP4"));
        Assert.Equal("out/video.m4a", MuxFinish.ToAudioOnlyPath("out/video.mkv"));
        Assert.Equal("out/v.1.0.m4a", MuxFinish.ToAudioOnlyPath("out/v.1.0.mp4"));
    }

    [Theory]
    [InlineData("HEVC")]
    [InlineData("AV1")]
    public void IsCodecUnsupported_RejectsNonAvcOnFlv(string codecs)
    {
        Assert.True(FlvDownload.IsCodecUnsupported(MakeVideo(codecs)));
    }

    [Theory]
    [InlineData("AVC")]
    [InlineData("UNKNOWN")]
    [InlineData("")]
    public void IsCodecUnsupported_AllowsAvcAndUnknown(string codecs)
    {
        Assert.False(FlvDownload.IsCodecUnsupported(MakeVideo(codecs)));
    }

    [Fact]
    public void IsCodecUnsupported_AllowsNullTrack( )
    {
        Assert.False(FlvDownload.IsCodecUnsupported(null));
    }

    private static Video MakeVideo(string codecs)
    {
        return new Video { id = "80", dfn = "1080P", baseUrl = "", codecs = codecs };
    }

    [Fact]
    public void BuildDownloadConfig_CopiesOptionsAndCookie( )
    {
        var o = new DownloadOptions
        {
            UseAria2c = true,
            Aria2cArgs = "-x16",
            NoForceHttp = false,
            SingleThread = false,
        };
        var cfg = AppConfig.Empty with { Cookie = "SESSDATA=abc" };
        var dc = PageDownload.BuildDownloadConfig(o, cfg, new ToolPaths("ffmpeg", "mp4box", "/opt/aria2c"));

        Assert.True(dc.UseAria2c);
        Assert.Equal("-x16", dc.Aria2cArgs);
        Assert.Equal("/opt/aria2c", dc.Aria2cPath);
        Assert.False(dc.NoForceHttp);
        Assert.False(dc.SingleThread);
        Assert.Equal("SESSDATA=abc", dc.Cookie);
        Assert.Null(dc.OnSample);
    }

    [Fact]
    public void FormatSavePath_ReplacesBasicPlaceholdersAndAppendsExtension( )
    {
        var p = MakePage(index: 3, aid: "114514", cid: "1919810", title: "分P标题");
        var result = SavePath.Format("<videoTitle>/[P<pageNumber>]<pageTitle>", "视频标题", null, null, p, 3, "WEB", 0);
        Assert.Equal("视频标题/[P3]分P标题.mp4", result);
    }

    [Fact]
    public void FormatSavePath_PadsPageNumberWithZero( )
    {
        var p = MakePage(index: 3);
        var result = SavePath.Format("<pageNumberWithZero>", "t", null, null, p, 100, "WEB", 0);
        Assert.Equal("003.mp4", result);
    }

    [Fact]
    public void FormatSavePath_NormalizesBackslashAndKeepsMp4( )
    {
        var p = MakePage( );
        var result = SavePath.Format("<aid>\\<cid>.mp4", "t", null, null, p, 1, "WEB", 0);
        Assert.Equal("114514/1919810.mp4", result);
    }

    [Fact]
    public void FormatSavePath_EmptyTrackPlaceholdersResolveToEmpty( )
    {
        var p = MakePage( );
        var result = SavePath.Format("<aid><dfn><videoCodecs><audioCodecs>", "t", null, null, p, 1, "WEB", 0);
        Assert.Equal("114514.mp4", result);
    }

    [Fact]
    public void FormatSavePath_UsesTrackInfoWhenAvailable( )
    {
        var p = MakePage( );
        var v = MakeVideo("120", "4K 超清", "HEVC", 8000);
        var a = MakeAudio("30280", "mp4a.40.2", 320);
        var result = SavePath.Format("<dfn>-<videoCodecs>-<videoBandwidth>-<audioCodecs>-<audioBandwidth>", "t", v, a, p, 1, "WEB", 0);
        Assert.Equal("4K 超清-HEVC-8000-mp4a.40.2-320.mp4", result);
    }

    [Fact]
    public void FormatSavePath_UnknownPlaceholderIsPreserved( )
    {
        var p = MakePage( );
        var result = SavePath.Format("<aid>-<nope>", "t", null, null, p, 1, "WEB", 0);
        Assert.Equal("114514-<nope>.mp4", result);
    }

    [Fact]
    public void FormatSavePath_ApiTypePlaceholder( )
    {
        var p = MakePage( );
        Assert.Equal("TV.mp4", SavePath.Format("<apiType>", "t", null, null, p, 1, "TV", 0));
    }

    [Fact]
    public void FormatSavePath_CustomDateFormat( )
    {
        var p = MakePage(pubTime: 1600000000);
        var result = SavePath.Format("<videoDate:yyyy>", "t", null, null, p, 1, "WEB", 0);
        Assert.Equal("2020.mp4", result);
    }

    [Fact]
    public void FormatSavePath_StripsSlashFromTitle( )
    {
        var p = MakePage( );
        var result = SavePath.Format("<videoTitle>", "a/b", null, null, p, 1, "WEB", 0);
        Assert.DoesNotContain('/', result);
    }

    // 替换值本身长得像占位符时，按位置替换才不会被后续迭代二次展开
    [Fact]
    public void FormatSavePath_DoesNotReexpandSubstitutedValues( )
    {
        var p = MakePage( );
        var v = MakeVideo("120", "<aid>", "AVC", 1000);
        var result = SavePath.Format("<dfn>-<aid>", "t", v, null, p, 1, "WEB", 0);
        Assert.Equal("<aid>-114514.mp4", result);
    }

    [Fact]
    public void FormatSavePath_KeepsUppercaseExtension( )
    {
        var p = MakePage( );
        Assert.Equal("114514.MP4", SavePath.Format("<aid>.MP4", "t", null, null, p, 1, "WEB", 0));
    }

    [Fact]
    public void FormatSavePath_EscapesWindowsReservedTitle( )
    {
        var p = MakePage( );
        Assert.Equal("_CON.mp4", SavePath.Format("<videoTitle>", "CON", null, null, p, 1, "WEB", 0));
    }

    [Fact]
    public void SortVideoTracks_ByDfnPriorityThenEncoding( )
    {
        List<Video> tracks =
        [
            MakeVideo("80", "1080P 高清", "AVC", 1000),
            MakeVideo("120", "4K 超清", "AVC", 5000),
            MakeVideo("120", "4K 超清", "HEVC", 4000),
        ];
        var dfnPriority = new Dictionary<string, int> { ["1080P 高清"] = 0, ["4K 超清"] = 1 };
        var sorted = TrackSelect.SortTracks(tracks, dfnPriority, [], videoAscending: false, encodingFirst: false);

        Assert.Equal("1080P 高清", sorted[0].dfn);
        Assert.Equal(3, sorted.Count);
    }

    [Fact]
    public void SortVideoTracks_NoPriorityPrefersHigherIdThenLargerBandwidth( )
    {
        List<Video> tracks =
        [
            MakeVideo("80", "1080P 高清", "AVC", 9000),
            MakeVideo("120", "4K 超清", "AVC", 1000),
            MakeVideo("120", "4K 超清", "HEVC", 2000),
        ];
        var sorted = TrackSelect.SortTracks(tracks, [], [], videoAscending: false, encodingFirst: false);

        Assert.Equal("120", sorted[0].id);
        Assert.Equal(2000, sorted[0].bandwidth);
        Assert.Equal("80", sorted[2].id);
    }

    [Fact]
    public void SortVideoTracks_AscendingPrefersSmallerBandwidth( )
    {
        List<Video> tracks =
        [
            MakeVideo("120", "4K 超清", "AVC", 5000),
            MakeVideo("120", "4K 超清", "AVC", 1000),
        ];
        var sorted = TrackSelect.SortTracks(tracks, [], [], videoAscending: true, encodingFirst: false);
        Assert.Equal(1000, sorted[0].bandwidth);
    }

    [Fact]
    public void SortAudioTracks_ByEncodingPriority( )
    {
        List<Audio> tracks =
        [
            MakeAudio("30280", "mp4a.40.2", 320),
            MakeAudio("30250", "E-AC-3", 640),
        ];
        var encodingPriority = new Dictionary<string, byte> { ["EAC3"] = 0 };
        var sorted = TrackSelect.SortTracks(tracks, encodingPriority, audioAscending: false);
        Assert.Equal("30250", sorted[0].id);
    }

    [Fact]
    public void SortAudioTracks_NoPriorityPrefersLargerBandwidth( )
    {
        List<Audio> tracks =
        [
            MakeAudio("30216", "mp4a.40.2", 64),
            MakeAudio("30280", "mp4a.40.2", 320),
        ];
        var sorted = TrackSelect.SortTracks(tracks, [], audioAscending: false);
        Assert.Equal(320, sorted[0].bandwidth);
    }

    [Fact]
    public void SortAudioTracks_AscendingPrefersSmallerBandwidth( )
    {
        List<Audio> tracks =
        [
            MakeAudio("30280", "mp4a.40.2", 320),
            MakeAudio("30216", "mp4a.40.2", 64),
        ];
        var sorted = TrackSelect.SortTracks(tracks, [], audioAscending: true);
        Assert.Equal(64, sorted[0].bandwidth);
    }

    [Fact]
    public void SortVideoTracks_EncodingFirst_WhenRequested( )
    {
        List<Video> tracks =
        [
            MakeVideo("80", "1080P 高清", "AVC", 1000),
            MakeVideo("120", "4K 超清", "AVC", 5000),
            MakeVideo("120", "4K 超清", "HEVC", 4000),
        ];
        var dfnPriority = new Dictionary<string, int> { ["1080P 高清"] = 0, ["4K 超清"] = 1 };
        var encodingPriority = new Dictionary<string, byte> { ["HEVC"] = 0, ["AVC"] = 1 };
        //编码优先时, 先按编码(HEVC 在前), 再按清晰度
        var sorted = TrackSelect.SortTracks(tracks, dfnPriority, encodingPriority, videoAscending: false, encodingFirst: true);

        Assert.Equal("HEVC", sorted[0].codecs);
        Assert.Equal(3, sorted.Count);
    }
}
