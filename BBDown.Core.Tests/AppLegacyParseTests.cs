using System.Linq;
using System.Text.Json;

using BBDown.Core.Entity;
using BBDown.Core.Protobuf;

namespace BBDown.Core.Tests;

// 基准线: 锁定 PlayViewReply -> ConvertToDashJson -> 通用 JSON 解析器 这条旧链路的输出。
// Parser.BuildAppParsedResult 接管后本文件连同 ConvertToDashJson 一并删除。
public class AppLegacyParseTests
{
    internal static ParsedResult ParseViaLegacyJson(PlayViewReply reply, bool isEpisode, string aid = "114514", string cid = "1919810")
    {
        var json = AppHelper.ConvertToDashJson(reply);
        using var doc = JsonDocument.Parse(json);
        var data = doc.RootElement;
        var root = Parser.GetRootNode(data, Parser.ResolveDataNodeName(data));
        var pDur = Parser.ReadDashDuration(root);

        ParsedResult result = new( ) { WebJsonString = json };
        Parser.CollectDashVideoTracks(result, root, pDur, false, true);
        Parser.CollectDashAudioTracks(result, root, pDur, false);
        if (isEpisode)
        {
            Parser.CollectDubbingTracks(result, data, pDur, aid, cid);
            Parser.AppendBangumiViewPoints(result, root);
        }

        return result;
    }

    [Fact]
    public void Ugc_VideoTracks( )
    {
        var result = ParseViaLegacyJson(PlayViewReplyFixtures.Ugc( ), false);

        Assert.Equal(2, result.VideoTracks.Count);

        var hevc = result.VideoTracks[0];
        Assert.Equal("120", hevc.id);
        Assert.Equal("4K 超清", hevc.dfn);
        Assert.Equal("HEVC", hevc.codecs);
        Assert.Equal(8000, hevc.bandwidth);
        Assert.Equal(754, hevc.dur);
        Assert.Equal("https://upos-sz-mirror08c.bilivideo.com/v120.m4s", hevc.baseUrl);
        Assert.Null(hevc.res);
        Assert.Null(hevc.fps);
        // 旧链路的 AudioInfoWitCodecId 不带 size 字段, 真实体积在序列化时被丢弃
        Assert.Equal(0, hevc.size);

        var avc = result.VideoTracks[1];
        Assert.Equal("80", avc.id);
        Assert.Equal("AVC", avc.codecs);
        Assert.Equal(4000, avc.bandwidth);
        Assert.Equal("https://upos-sz-mirror08c.bilivideo.com/v80.m4s", avc.baseUrl);
    }

    [Fact]
    public void Ugc_AudioTracks_AppendsHiResAndDolbyAfterDashAudio( )
    {
        var result = ParseViaLegacyJson(PlayViewReplyFixtures.Ugc( ), false);

        Assert.Equal(["30280", "30216", "30251", "30250"], result.AudioTracks.Select(a => a.id));
        Assert.Equal(["M4A", "M4A", "FLAC", "E-AC-3"], result.AudioTracks.Select(a => a.codecs));
        Assert.Equal([320, 64, 1000, 448], result.AudioTracks.Select(a => a.bandwidth));
        Assert.All(result.AudioTracks, a => Assert.Equal(754, a.dur));
        // Audio.Equals 不比较 baseUrl, 必须单独断言
        Assert.Equal([
            "https://upos-sz-mirror08c.bilivideo.com/a30280.m4s",
            "https://upos-sz-mirror08c.bilivideo.com/a30216.m4s",
            "https://upos-sz-mirror08c.bilivideo.com/a30251.m4s",
            "https://upos-sz-mirror08c.bilivideo.com/a30250.m4s"
        ], result.AudioTracks.Select(a => a.baseUrl));
    }

    [Fact]
    public void Ugc_NoDubbingOrViewPoints( )
    {
        var result = ParseViaLegacyJson(PlayViewReplyFixtures.Ugc( ), false);

        Assert.Empty(result.BackgroundAudioTracks);
        Assert.Empty(result.RoleAudioList);
        Assert.Empty(result.ExtraPoints);
        Assert.Empty(result.Clips);
    }

    [Fact]
    public void Bangumi_ViewPointsFillGapWithMainContent( )
    {
        var result = ParseViaLegacyJson(PlayViewReplyFixtures.Bangumi( ), true);

        Assert.Equal(["片头", "正片", "片尾"], result.ExtraPoints.Select(p => p.title));
        Assert.Equal([0, 90, 1350], result.ExtraPoints.Select(p => p.start));
        Assert.Equal([90, 1350, 1420], result.ExtraPoints.Select(p => p.end));
    }

    [Fact]
    public void Bangumi_BackgroundAudio( )
    {
        var result = ParseViaLegacyJson(PlayViewReplyFixtures.Bangumi( ), true);

        var bg = Assert.Single(result.BackgroundAudioTracks);
        Assert.Equal("30280", bg.id);
        Assert.Equal("M4A", bg.codecs);
        Assert.Equal(320, bg.bandwidth);
        Assert.Equal(1420, bg.dur);
        Assert.Equal("https://upos-sz-mirror08c.bilivideo.com/bg.m4s", bg.baseUrl);
    }

    [Fact]
    public void Bangumi_RoleAudio_EditionFallbackIsDeadCode( )
    {
        var result = ParseViaLegacyJson(PlayViewReplyFixtures.Bangumi( ), true);

        Assert.Equal(2, result.RoleAudioList.Count);

        var cn = result.RoleAudioList[0];
        Assert.Equal("中文配音", cn.title);
        Assert.Equal("张三", cn.personName);
        Assert.Equal("114514/114514.1919810.1001.m4a", cn.path);
        var cnAudio = Assert.Single(cn.audio);
        Assert.Equal("30280", cnAudio.id);
        Assert.Equal("https://upos-sz-mirror08c.bilivideo.com/cn.m4s", cnAudio.baseUrl);

        // proto2 的 optional string 读出的是 "" 而不是 null, 旧代码的
        // `Title ?? AudioId` / `PersonName ?? Edition` 永远不会回退 -> 配音名丢失
        var jp = result.RoleAudioList[1];
        Assert.Equal("", jp.title);
        Assert.Equal("", jp.personName);
        Assert.Equal("114514/114514.1919810.1002.m4a", jp.path);
        Assert.Equal("https://upos-sz-mirror08c.bilivideo.com/jp.m4s", Assert.Single(jp.audio).baseUrl);
    }

    [Fact]
    public void Empty_ThrowsNullReferenceBecauseVideoInfoIsUnguarded( )
    {
        // videoInfo 缺失时旧代码直接 resp.VideoInfo.StreamList 空引用
        Assert.ThrowsAny<System.NullReferenceException>(( ) => AppHelper.ConvertToDashJson(PlayViewReplyFixtures.Empty( )));
    }
}
