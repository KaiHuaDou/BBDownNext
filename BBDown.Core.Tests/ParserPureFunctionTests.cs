using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

using static BBDown.Core.Entity.Entity;

namespace BBDown.Core.Tests;

public class ParserPureFunctionTests
{
    [Fact]
    public void BuildUrlList_MergesBaseAndBackupUrls( )
    {
        using var doc = JsonDocument.Parse("""{"base_url":"http://main","backup_url":["http://b1","http://b2"]}""");
        var list = Parser.BuildUrlList(doc.RootElement);
        Assert.Equal(["http://main", "http://b1", "http://b2"], list);
    }

    [Fact]
    public void BuildUrlList_NoBackupUrl( )
    {
        using var doc = JsonDocument.Parse("""{"base_url":"http://main"}""");
        Assert.Equal(["http://main"], Parser.BuildUrlList(doc.RootElement));
    }

    [Fact]
    public void BuildUrlList_NullBackupUrl( )
    {
        using var doc = JsonDocument.Parse("""{"base_url":"http://main","backup_url":null}""");
        Assert.Equal(["http://main"], Parser.BuildUrlList(doc.RootElement));
    }

    [Fact]
    public void PickBaseUrl_PrefersUrlWithoutPort( )
    {
        // 带端口的 url（P2P/mcdn）被跳过，选第一个不带端口的
        var list = new List<string> { "https://xy1.mcdn.bilivideo.cn:8082/v.m4s", "https://upos-sz.bilivideo.com/v.m4s" };
        Assert.Equal("https://upos-sz.bilivideo.com/v.m4s", Parser.PickBaseUrl(list));
    }

    [Fact]
    public void PickBaseUrl_AllHavePorts_FallsBackToFirst( )
    {
        var list = new List<string> { "https://a:8082/v.m4s", "https://b:4483/v.m4s" };
        Assert.Equal("https://a:8082/v.m4s", Parser.PickBaseUrl(list));
    }

    [Theory]
    [InlineData(true, true, false, "https://tv.host/pgc/player/api/playurltv?")]
    [InlineData(true, false, false, "https://tv.host/x/tv/playurl?")]
    [InlineData(false, true, false, "https://web.host/pgc/player/web/v2/playurl?")]
    [InlineData(false, false, false, "https://web.host/x/player/wbi/playurl?")]
    [InlineData(false, true, true, "https://web.host/pugv/player/web/v2/playurl?")]
    public void BuildPlayUrlPrefix_CoversAllApiCombinations(bool tvApi, bool bangumi, bool cheese, string expected)
    {
        Assert.Equal(expected, Parser.BuildPlayUrlPrefix(tvApi, bangumi, cheese, "tv.host", "web.host"));
    }

    // --host 指定 BiliPlus 代理时，普通稿件的 playurl 也必须走代理，
    // 否则代理只对番剧生效，普通稿件仍直连官方
    [Fact]
    public void BuildPlayUrlPrefix_WebPlayUrlHonorsCustomHost( )
    {
        Assert.StartsWith("https://biliplus.example/", Parser.BuildPlayUrlPrefix(false, false, false, BiliApi.TvHost, "biliplus.example"));
    }

    [Theory]
    [InlineData("""{"result":{"video_info":{}}}""", "video_info")]
    [InlineData("""{"result":{"dash":{}}}""", "result")]
    [InlineData("""{"data":{"dash":{}}}""", "data")]
    [InlineData("""{"dash":{}}""", null)]
    public void ResolveDataNodeName_PicksPayloadNode(string json, string? expected)
    {
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(expected, Parser.ResolveDataNodeName(doc.RootElement));
    }

    [Fact]
    public void GetRootNode_UnwrapsNestedVideoInfo( )
    {
        using var doc = JsonDocument.Parse("""{"result":{"video_info":{"quality":80}}}""");
        var root = Parser.GetRootNode(doc.RootElement, "video_info");
        Assert.Equal(80, root.GetProperty("quality").GetInt32( ));
    }

    [Fact]
    public void GetRootNode_NullNodeName_ReturnsDataItself( )
    {
        using var doc = JsonDocument.Parse("""{"quality":64}""");
        Assert.Equal(64, Parser.GetRootNode(doc.RootElement, null).GetProperty("quality").GetInt32( ));
    }

    [Theory]
    [InlineData("""{"code":-10403,"message":"大会员专享限制"}""", true)]
    [InlineData("""{"code":-10403,"msg":"大会员专享限制"}""", true)]
    [InlineData("""{"code":0,"message":"0"}""", false)]
    [InlineData("""{"message":"大会员专享限制的说明"}""", false)]
    [InlineData("""{"data":{"message":"大会员专享限制"}}""", false)]
    public void IsVipRestricted_OnlyMatchesTopLevelMessage(string json, bool expected)
    {
        Assert.Equal(expected, Parser.IsVipRestricted(json));
    }

    // 网页源码兜底路径会把 HTML 传进来，不能因为解析失败就崩
    [Theory]
    [InlineData("")]
    [InlineData("<html>大会员专享限制</html>")]
    [InlineData("[1,2,3]")]
    public void IsVipRestricted_NonJsonOrNonObject_ReturnsFalse(string input)
    {
        Assert.False(Parser.IsVipRestricted(input));
    }

    [Fact]
    public void ReadDashDuration_PrefersTimelengthOverDashDuration( )
    {
        using var doc = JsonDocument.Parse("""{"timelength":125000,"dash":{"duration":999}}""");
        Assert.Equal(125, Parser.ReadDashDuration(doc.RootElement));
    }

    [Fact]
    public void ReadDashDuration_FallsBackToDashDuration( )
    {
        using var doc = JsonDocument.Parse("""{"dash":{"duration":300}}""");
        Assert.Equal(300, Parser.ReadDashDuration(doc.RootElement));
    }

    [Fact]
    public void ReadDashDuration_MissingFields_ReturnsZero( )
    {
        using var doc = JsonDocument.Parse("""{"dash":{}}""");
        Assert.Equal(0, Parser.ReadDashDuration(doc.RootElement));
    }

    [Fact]
    public void ReadAcceptedDfns_PrefersTvQnExtras( )
    {
        using var doc = JsonDocument.Parse("""{"qn_extras":[{"qn":"120"},{"qn":"80"}],"accept_quality":[64]}""");
        Assert.Equal(["120", "80"], Parser.ReadAcceptedDfns(doc.RootElement).ToList( ));
    }

    [Fact]
    public void ReadAcceptedDfns_FallsBackToAcceptQualityAndSkipsEmpty( )
    {
        using var doc = JsonDocument.Parse("""{"accept_quality":[80,"",64]}""");
        Assert.Equal(["80", "64"], Parser.ReadAcceptedDfns(doc.RootElement).ToList( ));
    }

    [Fact]
    public void ReadAcceptedDfns_NoQualityInfo_ReturnsEmpty( )
    {
        using var doc = JsonDocument.Parse("""{"durl":[]}""");
        Assert.Empty(Parser.ReadAcceptedDfns(doc.RootElement));
    }

    [Fact]
    public void FillGapsWithMainContent_InsertsMainContentBetweenClips( )
    {
        List<ViewPoint> points =
        [
            new( ) { title = "片头", start = 30, end = 120 },
            new( ) { title = "片尾", start = 1300, end = 1400 }
        ];

        var result = Parser.FillGapsWithMainContent(points);

        Assert.Equal(["正片", "片头", "正片", "片尾"], result.Select(p => p.title));
        Assert.Equal([(0, 30), (30, 120), (120, 1300), (1300, 1400)], result.Select(p => (p.start, p.end)));
    }

    [Fact]
    public void FillGapsWithMainContent_ClipStartsAtZero_NoLeadingMainContent( )
    {
        List<ViewPoint> points = [new( ) { title = "片头", start = 0, end = 90 }];
        var result = Parser.FillGapsWithMainContent(points);
        Assert.Equal(["片头"], result.Select(p => p.title));
    }

    [Fact]
    public void FillGapsWithMainContent_EmptyInput_ReturnsEmpty( )
    {
        Assert.Empty(Parser.FillGapsWithMainContent([]));
    }

    [Theory]
    [InlineData("mp4a.40.2", "M4A")]
    [InlineData("mp4a.40.5", "M4A")]
    [InlineData("ec-3", "E-AC-3")]
    [InlineData("fLaC", "FLAC")]
    [InlineData("opus", "opus")]
    public void NormalizeAudioCodec_MapsKnownCodecs(string raw, string expected)
    {
        Assert.Equal(expected, Parser.NormalizeAudioCodec(raw));
    }

    [Theory]
    [InlineData("13", "AV1")]
    [InlineData("12", "HEVC")]
    [InlineData("7", "AVC")]
    [InlineData("99", "UNKNOWN")]
    public void GetVideoCodec_MapsCodecId(string code, string expected)
    {
        Assert.Equal(expected, Parser.GetVideoCodec(code));
    }
}
