using System.Collections.Generic;

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

    [Fact]
    public void SanitizeTitle_PrefixesLeadingDot()
    {
        Assert.Equal("_.hidden", Program.SanitizeTitle(".hidden"));
    }

    [Fact]
    public void SanitizeTitle_AppendsFixOnTrailingDot()
    {
        Assert.Equal("title._fix", Program.SanitizeTitle("title."));
    }

    [Fact]
    public void SanitizeTitle_HandlesBothEnds()
    {
        Assert.Equal("_.title._fix", Program.SanitizeTitle(".title."));
    }

    [Fact]
    public void SanitizeTitle_LeavesNormalTitleUntouched()
    {
        Assert.Equal("普通标题", Program.SanitizeTitle("普通标题"));
    }

    [Fact]
    public void SanitizeTitle_IsIdempotent()
    {
        var once = Program.SanitizeTitle(".title.");
        Assert.Equal(once, Program.SanitizeTitle(once));
    }

    [Fact]
    public void ResolveSavePathFormat_SinglePageUsesSingleDefault()
    {
        var o = new MyOption();
        Assert.Equal(Program.SinglePageDefaultSavePath, Program.ResolveSavePathFormat(o, 1, false, false));
    }

    [Fact]
    public void ResolveSavePathFormat_MultiPageUsesMultiDefault()
    {
        var o = new MyOption();
        Assert.Equal(Program.MultiPageDefaultSavePath, Program.ResolveSavePathFormat(o, 3, false, false));
    }

    [Fact]
    public void ResolveSavePathFormat_UnfinishedBangumiTreatedAsMultiPage()
    {
        var o = new MyOption();
        Assert.Equal(Program.MultiPageDefaultSavePath, Program.ResolveSavePathFormat(o, 1, true, false));
    }

    [Fact]
    public void ResolveSavePathFormat_FinishedSinglePageBangumiUsesSingleDefault()
    {
        var o = new MyOption();
        Assert.Equal(Program.SinglePageDefaultSavePath, Program.ResolveSavePathFormat(o, 1, true, true));
    }

    [Fact]
    public void ResolveSavePathFormat_UserPatternsWin()
    {
        var o = new MyOption { FilePattern = "<aid>", MultiFilePattern = "<aid>/<cid>" };
        Assert.Equal("<aid>", Program.ResolveSavePathFormat(o, 1, false, false));
        Assert.Equal("<aid>/<cid>", Program.ResolveSavePathFormat(o, 2, false, false));
    }

    [Fact]
    public void BuildEpisodeTitle_SinglePageReturnsEmpty()
    {
        Assert.Equal("", Program.BuildEpisodeTitle(MakePage(), 1, false, false));
    }

    [Fact]
    public void BuildEpisodeTitle_MultiPageReturnsPageTitle()
    {
        Assert.Equal("第一话", Program.BuildEpisodeTitle(MakePage(), 5, false, false));
    }

    [Fact]
    public void BuildEpisodeTitle_UnfinishedBangumiReturnsPageTitle()
    {
        Assert.Equal("第一话", Program.BuildEpisodeTitle(MakePage(), 1, true, false));
    }

    [Fact]
    public void ShouldDeleteCover_SinglePage()
    {
        var p = MakePage();
        Assert.True(Program.ShouldDeleteCover(p, [p]));
    }

    [Fact]
    public void ShouldDeleteCover_LastPageOfSameAid()
    {
        var p1 = MakePage(1);
        var p2 = MakePage(2, cid: "2");
        Assert.True(Program.ShouldDeleteCover(p2, [p1, p2]));
    }

    [Fact]
    public void ShouldDeleteCover_MiddlePageOfSameAidKeepsCover()
    {
        var p1 = MakePage(1);
        var p2 = MakePage(2, cid: "2");
        Assert.False(Program.ShouldDeleteCover(p1, [p1, p2]));
    }

    [Fact]
    public void ShouldDeleteCover_DifferentAidAlwaysDeletes()
    {
        var p1 = MakePage(1, aid: "1");
        var p2 = MakePage(2, aid: "2");
        Assert.True(Program.ShouldDeleteCover(p1, [p1, p2]));
    }

    [Fact]
    public void ToAudioOnlyPath_ReplacesExtension()
    {
        Assert.Equal("out/video.m4a", Program.ToAudioOnlyPath("out/video.mp4"));
    }

    [Fact]
    public void BuildDownloadConfig_CopiesOptionsAndCookie()
    {
        var o = new MyOption
        {
            UseAria2c = true,
            Aria2cArgs = "-x16",
            ForceHttp = true,
            MultiThread = false,
        };
        var cfg = AppConfig.Empty with { Cookie = "SESSDATA=abc" };
        var dc = Program.BuildDownloadConfig(o, cfg, null);

        Assert.True(dc.UseAria2c);
        Assert.Equal("-x16", dc.Aria2cArgs);
        Assert.True(dc.ForceHttp);
        Assert.False(dc.MultiThread);
        Assert.Equal("SESSDATA=abc", dc.Cookie);
        Assert.Null(dc.RelatedTask);
    }

    [Fact]
    public void FormatSavePath_ReplacesBasicPlaceholdersAndAppendsExtension()
    {
        var p = MakePage(index: 3, aid: "114514", cid: "1919810", title: "分P标题");
        var result = Program.FormatSavePath("<videoTitle>/[P<pageNumber>]<pageTitle>", "视频标题", null, null, p, 3, "WEB", 0);
        Assert.Equal("视频标题/[P3]分P标题.mp4", result);
    }

    [Fact]
    public void FormatSavePath_PadsPageNumberWithZero()
    {
        var p = MakePage(index: 3);
        var result = Program.FormatSavePath("<pageNumberWithZero>", "t", null, null, p, 100, "WEB", 0);
        Assert.Equal("003.mp4", result);
    }

    [Fact]
    public void FormatSavePath_NormalizesBackslashAndKeepsMp4()
    {
        var p = MakePage();
        var result = Program.FormatSavePath("<aid>\\<cid>.mp4", "t", null, null, p, 1, "WEB", 0);
        Assert.Equal("114514/1919810.mp4", result);
    }

    [Fact]
    public void FormatSavePath_EmptyTrackPlaceholdersResolveToEmpty()
    {
        var p = MakePage();
        var result = Program.FormatSavePath("<aid><dfn><videoCodecs><audioCodecs>", "t", null, null, p, 1, "WEB", 0);
        Assert.Equal("114514.mp4", result);
    }

    [Fact]
    public void FormatSavePath_UsesTrackInfoWhenAvailable()
    {
        var p = MakePage();
        var v = MakeVideo("120", "4K 超清", "HEVC", 8000);
        var a = MakeAudio("30280", "mp4a.40.2", 320);
        var result = Program.FormatSavePath("<dfn>-<videoCodecs>-<videoBandwidth>-<audioCodecs>-<audioBandwidth>", "t", v, a, p, 1, "WEB", 0);
        Assert.Equal("4K 超清-HEVC-8000-mp4a.40.2-320.mp4", result);
    }

    [Fact]
    public void FormatSavePath_UnknownPlaceholderIsPreserved()
    {
        var p = MakePage();
        var result = Program.FormatSavePath("<aid>-<nope>", "t", null, null, p, 1, "WEB", 0);
        Assert.Equal("114514-<nope>.mp4", result);
    }

    [Fact]
    public void FormatSavePath_ApiTypePlaceholder()
    {
        var p = MakePage();
        Assert.Equal("TV.mp4", Program.FormatSavePath("<apiType>", "t", null, null, p, 1, "TV", 0));
    }

    [Fact]
    public void FormatSavePath_CustomDateFormat()
    {
        var p = MakePage(pubTime: 1600000000);
        var result = Program.FormatSavePath("<videoDate:yyyy>", "t", null, null, p, 1, "WEB", 0);
        Assert.Equal("2020.mp4", result);
    }

    [Fact]
    public void FormatSavePath_StripsSlashFromTitle()
    {
        var p = MakePage();
        var result = Program.FormatSavePath("<videoTitle>", "a/b", null, null, p, 1, "WEB", 0);
        Assert.DoesNotContain('/', result);
    }

    [Fact]
    public void SortVideoTracks_ByDfnPriorityThenEncoding()
    {
        List<Video> tracks =
        [
            MakeVideo("80", "1080P 高清", "AVC", 1000),
            MakeVideo("120", "4K 超清", "AVC", 5000),
            MakeVideo("120", "4K 超清", "HEVC", 4000),
        ];
        var dfnPriority = new Dictionary<string, int> { ["1080P 高清"] = 0, ["4K 超清"] = 1 };
        var sorted = Program.SortTracks(tracks, dfnPriority, [], videoAscending: false, encodingFirst: false);

        Assert.Equal("1080P 高清", sorted[0].dfn);
        Assert.Equal(3, sorted.Count);
    }

    [Fact]
    public void SortVideoTracks_NoPriorityPrefersHigherIdThenLargerBandwidth()
    {
        List<Video> tracks =
        [
            MakeVideo("80", "1080P 高清", "AVC", 9000),
            MakeVideo("120", "4K 超清", "AVC", 1000),
            MakeVideo("120", "4K 超清", "HEVC", 2000),
        ];
        var sorted = Program.SortTracks(tracks, [], [], videoAscending: false, encodingFirst: false);

        Assert.Equal("120", sorted[0].id);
        Assert.Equal(2000, sorted[0].bandwidth);
        Assert.Equal("80", sorted[2].id);
    }

    [Fact]
    public void SortVideoTracks_AscendingPrefersSmallerBandwidth()
    {
        List<Video> tracks =
        [
            MakeVideo("120", "4K 超清", "AVC", 5000),
            MakeVideo("120", "4K 超清", "AVC", 1000),
        ];
        var sorted = Program.SortTracks(tracks, [], [], videoAscending: true, encodingFirst: false);
        Assert.Equal(1000, sorted[0].bandwidth);
    }

    [Fact]
    public void SortAudioTracks_ByEncodingPriority()
    {
        List<Audio> tracks =
        [
            MakeAudio("30280", "mp4a.40.2", 320),
            MakeAudio("30250", "E-AC-3", 640),
        ];
        var encodingPriority = new Dictionary<string, byte> { ["EAC3"] = 0 };
        var sorted = Program.SortTracks(tracks, encodingPriority, audioAscending: false);
        Assert.Equal("30250", sorted[0].id);
    }

    [Fact]
    public void SortAudioTracks_NoPriorityPrefersLargerBandwidth()
    {
        List<Audio> tracks =
        [
            MakeAudio("30216", "mp4a.40.2", 64),
            MakeAudio("30280", "mp4a.40.2", 320),
        ];
        var sorted = Program.SortTracks(tracks, [], audioAscending: false);
        Assert.Equal(320, sorted[0].bandwidth);
    }

    [Fact]
    public void SortAudioTracks_AscendingPrefersSmallerBandwidth()
    {
        List<Audio> tracks =
        [
            MakeAudio("30280", "mp4a.40.2", 320),
            MakeAudio("30216", "mp4a.40.2", 64),
        ];
        var sorted = Program.SortTracks(tracks, [], audioAscending: true);
        Assert.Equal(64, sorted[0].bandwidth);
    }

    [Fact]
    public void SortVideoTracks_EncodingFirst_WhenRequested()
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
        var sorted = Program.SortTracks(tracks, dfnPriority, encodingPriority, videoAscending: false, encodingFirst: true);

        Assert.Equal("HEVC", sorted[0].codecs);
        Assert.Equal(3, sorted.Count);
    }
}
