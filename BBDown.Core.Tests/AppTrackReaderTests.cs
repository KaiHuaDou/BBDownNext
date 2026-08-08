using System.Linq;

using BBDown.Core.Entity;
using BBDown.Core.PlayUrl;
using BBDown.Core.Protobuf;

namespace BBDown.Core.Tests;

public class AppTrackReaderTests
{
    private const string Aid = "114514";
    private const string Cid = "1919810";

    private static ParsedResult Build(PlayViewReply reply, bool isEpisode)
    {
        return AppTrackReader.Build(reply, isEpisode, Aid, Cid);
    }

    // playurl 声明的时长是识别充电专属试看片段的一半判据
    [Fact]
    public void Build_SetsDurationFromTimelength( )
    {
        Assert.Equal(754, Build(PlayViewReplyFixtures.Ugc( ), false).Duration);
    }

    [Fact]
    public void Build_NoVideoInfo_LeavesDurationZero( )
    {
        Assert.Equal(0, Build(new PlayViewReply( ), false).Duration);
    }

    [Fact]
    public void Ugc_VideoTracks_SkipsStreamsWithoutDash( )
    {
        var result = Build(PlayViewReplyFixtures.Ugc( ), false);

        Assert.Equal(2, result.VideoTracks.Count);

        var hevc = result.VideoTracks[0];
        Assert.Equal("120", hevc.Id);
        Assert.Equal("4K 超清", hevc.Dfn);
        Assert.Equal("HEVC", hevc.Codecs);
        Assert.Equal(754, hevc.Dur);
        Assert.Equal("https://upos-sz-mirror08c.bilivideo.com/v120.m4s", hevc.BaseUrl);
        // App 端不下发 res / fps
        Assert.Null(hevc.Res);
        Assert.Null(hevc.Fps);

        var avc = result.VideoTracks[1];
        Assert.Equal("80", avc.Id);
        Assert.Equal("AVC", avc.Codecs);
        Assert.Equal("https://upos-sz-mirror08c.bilivideo.com/v80.m4s", avc.BaseUrl);
    }

    [Fact]
    public void Ugc_VideoBandwidthDerivedFromSizeAndDuration( )
    {
        var result = Build(PlayViewReplyFixtures.Ugc( ), false);

        Assert.Equal([8000, 4000], result.VideoTracks.Select(v => v.Bandwidth));
        Assert.Equal([754_000_000d, 377_000_000d], result.VideoTracks.Select(v => v.Size));
    }

    [Fact]
    public void Ugc_AudioTracks_HiResAndDolbyAppendedAfterDashAudio( )
    {
        var result = Build(PlayViewReplyFixtures.Ugc( ), false);

        Assert.Equal(["30280", "30216", "30251", "30250"], result.AudioTracks.Select(a => a.Id));
        Assert.Equal(["M4A", "M4A", "FLAC", "E-AC-3"], result.AudioTracks.Select(a => a.Codecs));
        Assert.Equal([320, 64, 1000, 448], result.AudioTracks.Select(a => a.Bandwidth));
        Assert.All(result.AudioTracks, a => Assert.Equal(754, a.Dur));
        Assert.Equal([
            "https://upos-sz-mirror08c.bilivideo.com/a30280.m4s",
            "https://upos-sz-mirror08c.bilivideo.com/a30216.m4s",
            "https://upos-sz-mirror08c.bilivideo.com/a30251.m4s",
            "https://upos-sz-mirror08c.bilivideo.com/a30250.m4s"
        ], result.AudioTracks.Select(a => a.BaseUrl));
    }

    [Fact]
    public void Ugc_BackupUrlWithPortIsNotPickedAsBaseUrl( )
    {
        var result = Build(PlayViewReplyFixtures.Ugc( ), false);

        Assert.DoesNotContain(":8082", result.VideoTracks[0].BaseUrl);
        Assert.DoesNotContain(":4483", result.AudioTracks[0].BaseUrl);
    }

    [Fact]
    public void Ugc_NotAnEpisode_NoDubbingOrViewPoints( )
    {
        var result = Build(PlayViewReplyFixtures.Ugc( ), false);

        Assert.Empty(result.BackgroundAudioTracks);
        Assert.Empty(result.RoleAudioList);
        Assert.Empty(result.ExtraPoints);
        Assert.Empty(result.Clips);
    }

    [Fact]
    public void Bangumi_ClipInfoBecomesViewPointsWithMainContentFillingGaps( )
    {
        var result = Build(PlayViewReplyFixtures.Bangumi( ), true);

        Assert.Equal(["片头", "正片", "片尾"], result.ExtraPoints.Select(p => p.Title));
        Assert.Equal([0, 90, 1350], result.ExtraPoints.Select(p => p.Start));
        Assert.Equal([90, 1350, 1420], result.ExtraPoints.Select(p => p.End));
    }

    [Fact]
    public void Bangumi_BackgroundAudio( )
    {
        var result = Build(PlayViewReplyFixtures.Bangumi( ), true);

        var bg = Assert.Single(result.BackgroundAudioTracks);
        Assert.Equal("30280", bg.Id);
        Assert.Equal("M4A", bg.Codecs);
        Assert.Equal(320, bg.Bandwidth);
        Assert.Equal(1420, bg.Dur);
        Assert.Equal("https://upos-sz-mirror08c.bilivideo.com/bg.m4s", bg.BaseUrl);
    }

    [Fact]
    public void Bangumi_RoleAudio( )
    {
        var result = Build(PlayViewReplyFixtures.Bangumi( ), true);

        Assert.Equal(2, result.RoleAudioList.Count);

        var cn = result.RoleAudioList[0];
        Assert.Equal("中文配音", cn.Title);
        Assert.Equal("张三", cn.PersonName);
        Assert.Equal("114514/114514.1919810.1001.m4a", cn.Path);
        Assert.Equal("https://upos-sz-mirror08c.bilivideo.com/cn.m4s", Assert.Single(cn.Audio).BaseUrl);

        // proto2 未设置的 optional string 读出 "" 而非 null, 回退必须判长度
        var jp = result.RoleAudioList[1];
        Assert.Equal("1002", jp.Title);
        Assert.Equal("日语原声", jp.PersonName);
        Assert.Equal("114514/114514.1919810.1002.m4a", jp.Path);
        Assert.Equal("https://upos-sz-mirror08c.bilivideo.com/jp.m4s", Assert.Single(jp.Audio).BaseUrl);
    }

    [Fact]
    public void Bangumi_ParsedAsUgc_DropsDubbingAndViewPoints( )
    {
        var result = Build(PlayViewReplyFixtures.Bangumi( ), false);

        Assert.Single(result.VideoTracks);
        Assert.Single(result.AudioTracks);
        Assert.Empty(result.BackgroundAudioTracks);
        Assert.Empty(result.RoleAudioList);
        Assert.Empty(result.ExtraPoints);
    }

    [Fact]
    public void MissingVideoInfo_ReturnsEmptyResultInsteadOfThrowing( )
    {
        var result = Build(PlayViewReplyFixtures.Empty( ), true);

        Assert.Empty(result.VideoTracks);
        Assert.Empty(result.AudioTracks);
        Assert.Empty(result.BackgroundAudioTracks);
        Assert.Empty(result.RoleAudioList);
        Assert.Empty(result.ExtraPoints);
    }
}
