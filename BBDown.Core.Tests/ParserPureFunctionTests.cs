using System.Linq;
using System.Text.Json;

using BBDown.Core.PlayUrl;

namespace BBDown.Core.Tests;

public class ParserPureFunctionTests
{
    [Theory]
    [InlineData(true, true, false, "https://tv.host/pgc/player/api/playurltv?")]
    [InlineData(true, false, false, "https://tv.host/x/tv/playurl?")]
    [InlineData(false, true, false, "https://web.host/pgc/player/web/v2/playurl?")]
    [InlineData(false, false, false, "https://web.host/x/player/wbi/playurl?")]
    [InlineData(false, true, true, "https://web.host/pugv/player/web/v2/playurl?")]
    public void BuildPlayUrlPrefix_CoversAllApiCombinations(bool tvApi, bool bangumi, bool cheese, string expected)
    {
        Assert.Equal(expected, PlayUrlClient.BuildPrefix(tvApi, bangumi, cheese, "tv.host", "web.host"));
    }

    // --host 指定 BiliPlus 代理时，普通稿件的 playurl 也必须走代理，
    // 否则代理只对番剧生效，普通稿件仍直连官方
    [Fact]
    public void BuildPlayUrlPrefix_WebPlayUrlHonorsCustomHost( )
    {
        Assert.StartsWith("https://biliplus.example/", PlayUrlClient.BuildPrefix(false, false, false, BiliApi.TvHost, "biliplus.example"));
    }

    [Theory]
    [InlineData("""{"result":{"video_info":{}}}""", "video_info")]
    [InlineData("""{"result":{"dash":{}}}""", "result")]
    [InlineData("""{"data":{"dash":{}}}""", "data")]
    [InlineData("""{"dash":{}}""", null)]
    public void ResolveDataNodeName_PicksPayloadNode(string json, string? expected)
    {
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(expected, PlayUrlResponse.ResolveDataNodeName(doc.RootElement));
    }

    [Fact]
    public void GetRootNode_UnwrapsNestedVideoInfo( )
    {
        using var doc = JsonDocument.Parse("""{"result":{"video_info":{"quality":80}}}""");
        var root = PlayUrlResponse.GetRootNode(doc.RootElement, "video_info");
        Assert.Equal(80, root.GetProperty("quality").GetInt32( ));
    }

    [Fact]
    public void GetRootNode_NullNodeName_ReturnsDataItself( )
    {
        using var doc = JsonDocument.Parse("""{"quality":64}""");
        Assert.Equal(64, PlayUrlResponse.GetRootNode(doc.RootElement, null).GetProperty("quality").GetInt32( ));
    }

    [Theory]
    [InlineData("""{"code":-10403,"message":"大会员专享限制"}""", true)]
    [InlineData("""{"code":-10403,"msg":"大会员专享限制"}""", true)]
    [InlineData("""{"code":0,"message":"0"}""", false)]
    [InlineData("""{"message":"大会员专享限制的说明"}""", false)]
    [InlineData("""{"data":{"message":"大会员专享限制"}}""", false)]
    public void IsVipRestricted_OnlyMatchesTopLevelMessage(string json, bool expected)
    {
        Assert.Equal(expected, PlayUrlResponse.IsVipRestricted(json));
    }

    // 网页源码兜底路径会把 HTML 传进来，不能因为解析失败就崩
    [Theory]
    [InlineData("")]
    [InlineData("<html>大会员专享限制</html>")]
    [InlineData("[1,2,3]")]
    public void IsVipRestricted_NonJsonOrNonObject_ReturnsFalse(string input)
    {
        Assert.False(PlayUrlResponse.IsVipRestricted(input));
    }

    [Fact]
    public void ReadDashDuration_PrefersTimelengthOverDashDuration( )
    {
        using var doc = JsonDocument.Parse("""{"timelength":125000,"dash":{"duration":999}}""");
        Assert.Equal(125, DashTrackReader.ReadDuration(doc.RootElement));
    }

    [Fact]
    public void ReadDashDuration_FallsBackToDashDuration( )
    {
        using var doc = JsonDocument.Parse("""{"dash":{"duration":300}}""");
        Assert.Equal(300, DashTrackReader.ReadDuration(doc.RootElement));
    }

    [Fact]
    public void ReadDashDuration_MissingFields_ReturnsZero( )
    {
        using var doc = JsonDocument.Parse("""{"dash":{}}""");
        Assert.Equal(0, DashTrackReader.ReadDuration(doc.RootElement));
    }

    [Fact]
    public void ReadAcceptedDfns_PrefersTvQnExtras( )
    {
        using var doc = JsonDocument.Parse("""{"qn_extras":[{"qn":"120"},{"qn":"80"}],"accept_quality":[64]}""");
        Assert.Equal(["120", "80"], FlvTrackReader.ReadAcceptedDfns(doc.RootElement).ToList( ));
    }

    [Fact]
    public void ReadAcceptedDfns_FallsBackToAcceptQualityAndSkipsEmpty( )
    {
        using var doc = JsonDocument.Parse("""{"accept_quality":[80,"",64]}""");
        Assert.Equal(["80", "64"], FlvTrackReader.ReadAcceptedDfns(doc.RootElement).ToList( ));
    }

    [Fact]
    public void ReadAcceptedDfns_NoQualityInfo_ReturnsEmpty( )
    {
        using var doc = JsonDocument.Parse("""{"durl":[]}""");
        Assert.Empty(FlvTrackReader.ReadAcceptedDfns(doc.RootElement));
    }

    // 番剧 playurl 必须请求 fnval=12240（含 8192 智能修复位），UGC 端点带该位会 -400 故不能全局硬改
    [Fact]
    public void BuildWebQuery_BangumiRequestsFnvalPgc( )
    {
        // IsEpisode 认的是带冒号的 "ep:" 内部前缀, 写成 "ep123" 会悄悄落到 UGC 分支
        var req = new PlayUrlRequest("ep:123", "1", "2", "123", TvApi: false, IntlApi: false, AppApi: false, Encoding: "", AppConfig.Empty);
        Assert.Contains("fnval=12240", PlayUrlClient.BuildWebQuery(req, "0"));
    }

    [Fact]
    public void BuildWebQuery_UgcKeepsFnval4048( )
    {
        var req = new PlayUrlRequest("BV1xx", "1", "2", "", TvApi: false, IntlApi: false, AppApi: false, Encoding: "", AppConfig.Empty);
        Assert.Contains("fnval=4048", PlayUrlClient.BuildWebQuery(req, "0"));
    }

    // TV 端点实测不提供 qn=100（智能修复），恒为 4048
    [Fact]
    public void BuildTvQuery_AlwaysFnval4048( )
    {
        var req = new PlayUrlRequest("ep:123", "1", "2", "123", TvApi: true, IntlApi: false, AppApi: false, Encoding: "", AppConfig.Empty);
        Assert.Contains("fnval=4048", PlayUrlClient.BuildTvQuery(req, "0"));
    }
}
