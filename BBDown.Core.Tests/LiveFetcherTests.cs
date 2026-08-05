using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core.Live;
using BBDown.Core.Util;

namespace BBDown.Core.Tests;

public class LiveFetcherParseTests
{
    // 取自 getRoomPlayInfo 真实响应（room_id=22632424，未登录），仅裁剪 extra 长度与无关字段
    private const string PlayInfoJson = """
    {
      "room_id": 22632424,
      "short_id": 0,
      "uid": 672353429,
      "live_status": 1,
      "encrypted": false,
      "pwd_verified": true,
      "playurl_info": {
        "conf_json": "",
        "playurl": {
          "cid": 22632424,
          "stream": [
            {
              "protocol_name": "http_stream",
              "format": [
                {
                  "format_name": "flv",
                  "codec": [
                    {
                      "codec_name": "avc",
                      "current_qn": 250,
                      "accept_qn": [10000, 400, 250],
                      "base_url": "/live-bvc/341908/live_b4av85_2500.flv?",
                      "drm": false,
                      "url_info": [
                        { "host": "https://cn-jsyz-ct-03-19.bilivideo.com", "extra": "expires=1785913252&pt=web", "stream_ttl": 0 },
                        { "host": "https://d1--cn-gotcha07b.bilivideo.com", "extra": "expires=1785913252&len=0", "stream_ttl": 0 }
                      ]
                    },
                    {
                      "codec_name": "hevc",
                      "current_qn": 250,
                      "accept_qn": [10000, 400, 250],
                      "base_url": "/live-bvc/992023/live_b4av85_minihevc.flv?",
                      "drm": false,
                      "url_info": [
                        { "host": "https://cn-jsyz-ct-03-18.bilivideo.com", "extra": "expires=1785913252&pt=web", "stream_ttl": 0 }
                      ]
                    }
                  ]
                }
              ]
            }
          ]
        }
      }
    }
    """;

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void ParsePlayInfo_RealResponse_ReturnsAllCandidates( )
    {
        var info = LiveFetcher.ParsePlayInfo(Parse(PlayInfoJson), LiveQuality.Original);

        Assert.NotNull(info);
        Assert.Equal(3, info.Candidates.Count);
        Assert.Equal(250, info.ActualQn);
        Assert.Equal([10000, 400, 250], info.AcceptQn);
    }

    // base_url 自带尾部 ?，三段直接相连，中间不能再插入分隔符
    [Fact]
    public void ParsePlayInfo_ConcatenatesUrlWithoutExtraSeparator( )
    {
        var info = LiveFetcher.ParsePlayInfo(Parse(PlayInfoJson), LiveQuality.Original);

        Assert.Equal(
            "https://cn-jsyz-ct-03-19.bilivideo.com/live-bvc/341908/live_b4av85_2500.flv?expires=1785913252&pt=web",
            info!.Candidates[0].Url);
    }

    // avc 兼容性更好，即便接口把 hevc 排在前面也要优先
    [Fact]
    public void ParsePlayInfo_PrefersAvcOverHevc( )
    {
        var info = LiveFetcher.ParsePlayInfo(Parse(PlayInfoJson), LiveQuality.Original);

        Assert.Equal("avc", info!.Candidates[0].CodecName);
        Assert.Equal("avc", info.Candidates[1].CodecName);
        Assert.Equal("hevc", info.Candidates[2].CodecName);
    }

    [Fact]
    public void ParsePlayInfo_MultipleHosts_BecomeSeparateCandidates( )
    {
        var info = LiveFetcher.ParsePlayInfo(Parse(PlayInfoJson), LiveQuality.Original);

        Assert.Equal("https://cn-jsyz-ct-03-19.bilivideo.com", info!.Candidates[0].Host);
        Assert.Equal("https://d1--cn-gotcha07b.bilivideo.com", info.Candidates[1].Host);
    }

    // 未登录时接口恒返回 250 却仍在 accept_qn 里列出 10000，降级只能比对 current_qn
    [Fact]
    public void ParsePlayInfo_LowerCurrentQn_IsDegraded( )
    {
        var info = LiveFetcher.ParsePlayInfo(Parse(PlayInfoJson), LiveQuality.Original);

        Assert.True(info!.Degraded);
        Assert.Contains(LiveQuality.Original, info.AcceptQn);
    }

    [Fact]
    public void ParsePlayInfo_MatchingQn_IsNotDegraded( )
    {
        var info = LiveFetcher.ParsePlayInfo(Parse(PlayInfoJson), 250);

        Assert.False(info!.Degraded);
    }

    // 带 DRM 的轨道下载下来也放不了，须跳过并落到下一个编码
    private const string DrmAvcJson = """
    {
      "live_status": 1,
      "playurl_info": { "playurl": { "stream": [ { "protocol_name": "http_stream", "format": [ { "format_name": "flv", "codec": [
        { "codec_name": "avc", "current_qn": 250, "accept_qn": [250], "base_url": "/a.flv?", "drm": true,
          "url_info": [ { "host": "https://cdn.test", "extra": "k=1" } ] },
        { "codec_name": "hevc", "current_qn": 250, "accept_qn": [250], "base_url": "/b.flv?", "drm": false,
          "url_info": [ { "host": "https://cdn.test", "extra": "k=2" } ] }
      ] } ] } ] } }
    }
    """;

    [Fact]
    public void ParsePlayInfo_DrmCodec_IsSkipped( )
    {
        var info = LiveFetcher.ParsePlayInfo(Parse(DrmAvcJson), 250);

        Assert.NotNull(info);
        Assert.Equal("hevc", Assert.Single(info.Candidates).CodecName);
    }

    // 全部轨道都带 DRM 时不能返回空壳，要当作「拿不到流」
    [Fact]
    public void ParsePlayInfo_AllDrm_ReturnsNull( )
    {
        var json = DrmAvcJson.Replace("\"drm\": false", "\"drm\": true", StringComparison.Ordinal);

        Assert.Null(LiveFetcher.ParsePlayInfo(Parse(json), 250));
    }

    [Theory]
    [InlineData("\"live_status\": 1", "\"live_status\": 0")]
    [InlineData("\"live_status\": 1", "\"live_status\": 2")]
    public void ParsePlayInfo_NotLiving_ReturnsNull(string from, string to)
    {
        var json = PlayInfoJson.Replace(from, to, StringComparison.Ordinal);

        Assert.Null(LiveFetcher.ParsePlayInfo(Parse(json), LiveQuality.Original));
    }

    [Theory]
    [InlineData("""{ "live_status": 1 }""")]
    [InlineData("""{ "live_status": 1, "playurl_info": null }""")]
    [InlineData("""{ "live_status": 1, "playurl_info": { "playurl": { "stream": [] } } }""")]
    public void ParsePlayInfo_NoPlayUrl_ReturnsNull(string json)
    {
        Assert.Null(LiveFetcher.ParsePlayInfo(Parse(json), LiveQuality.Original));
    }

    // hls / fmp4 的分片语义与 BBDown 的连续字节流录制模型不兼容，必须过滤掉
    [Fact]
    public void ParsePlayInfo_NonFlvStream_IsFiltered( )
    {
        var json = PlayInfoJson
            .Replace("\"protocol_name\": \"http_stream\"", "\"protocol_name\": \"http_hls\"", StringComparison.Ordinal)
            .Replace("\"format_name\": \"flv\"", "\"format_name\": \"fmp4\"", StringComparison.Ordinal);

        Assert.Null(LiveFetcher.ParsePlayInfo(Parse(json), LiveQuality.Original));
    }

    [Theory]
    [InlineData("https://cdn.test", "/a/b.flv?", "k=v", "https://cdn.test/a/b.flv?k=v")]
    [InlineData("https://cdn.test/", "/a/b.flv?", "k=v", "https://cdn.test/a/b.flv?k=v")]
    [InlineData("https://cdn.test", "/a/b.flv?", "", "https://cdn.test/a/b.flv?")]
    [InlineData("https://cdn.test", "/a/b.flv", "k=v", "https://cdn.test/a/b.flv?k=v")]
    [InlineData("https://cdn.test", "/a/b.flv?x=1", "k=v", "https://cdn.test/a/b.flv?x=1&k=v")]
    [InlineData("https://cdn.test", "/a/b.flv?x=1&", "k=v", "https://cdn.test/a/b.flv?x=1&k=v")]
    public void BuildStreamUrl_JoinsSegments(string host, string baseUrl, string extra, string expected)
    {
        Assert.Equal(expected, LiveFetcher.BuildStreamUrl(host, baseUrl, extra));
    }
}

[Collection<HttpStubCollectionDefinition>]
public class LiveFetcherHttpTests
{
    private const string RoomInitJson = """
    {"code":0,"msg":"ok","message":"ok","data":{"room_id":23058,"short_id":3,"uid":11153765,
    "is_hidden":false,"is_locked":false,"is_portrait":false,"live_status":1,"hidden_till":0,
    "lock_till":0,"encrypted":false,"pwd_verified":false,"live_time":1700000000,"room_shield":1}}
    """;

    private const string RoomBaseInfoJson = """
    {"code":0,"message":"OK","ttl":1,"data":{"by_uids":{},"by_room_ids":{"23058":{
    "room_id":23058,"uid":11153765,"live_status":1,"title":"哔哩哔哩音悦台","uname":"3号直播间",
    "cover":"https://i0.hdslb.com/bfs/live/cover.jpg","short_id":3}}}}
    """;

    private sealed class RoutingHandler(Func<string, string> route) : HttpMessageHandler
    {
        private readonly Func<string, string> route = route;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(route(request.RequestUri!.AbsoluteUri), Encoding.UTF8, "application/json")
            });
        }
    }

    private static async Task<T> WithRoutingClient<T>(Func<string, string> route, Func<Task<T>> act)
    {
        var original = HTTPUtil.AppHttpClient;
        using var handler = new RoutingHandler(route);
        using var client = new HttpClient(handler, disposeHandler: false);
        HTTPUtil.AppHttpClient = client;
        try
        {
            return await act( );
        }
        finally
        {
            HTTPUtil.AppHttpClient = original;
        }
    }

    // 短号 3 必须先经 room_init 换成真实房间号 23058，后续接口才查得到
    [Fact]
    public async Task FetchRoomAsync_ShortId_ResolvesToRealRoomId( )
    {
        var requested = new List<string>( );
        var info = await WithRoutingClient(
            url =>
            {
                requested.Add(url);
                return url.Contains("room_init", StringComparison.Ordinal) ? RoomInitJson : RoomBaseInfoJson;
            },
            ( ) => LiveFetcher.FetchRoomAsync(new LiveTarget("3"), new AppConfig( ), TestContext.Current.CancellationToken));

        Assert.Equal("23058", info.RoomId);
        Assert.Equal("3", info.ShortId);
        Assert.Equal("11153765", info.Uid);
        Assert.Equal("3号直播间", info.Uname);
        Assert.Equal("哔哩哔哩音悦台", info.Title);
        Assert.True(info.IsLiving);
        Assert.Contains(requested, u => u.Contains("room_ids=23058", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FetchRoomAsync_NotLiving_IsLivingFalse( )
    {
        var info = await WithRoutingClient(
            url => url.Contains("room_init", StringComparison.Ordinal)
                ? RoomInitJson.Replace("\"live_status\":1", "\"live_status\":0", StringComparison.Ordinal)
                : RoomBaseInfoJson,
            ( ) => LiveFetcher.FetchRoomAsync(new LiveTarget("3"), new AppConfig( ), TestContext.Current.CancellationToken));

        Assert.False(info.IsLiving);
    }

    [Fact]
    public async Task FetchRoomAsync_ApiError_Throws( )
    {
        await Assert.ThrowsAsync<InvalidOperationException>(( ) => WithRoutingClient(
            _ => """{"code":1,"message":"房间不存在","data":null}""",
            ( ) => LiveFetcher.FetchRoomAsync(new LiveTarget("999999999"), new AppConfig( ), TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task FetchPlayInfoAsync_PassesQnAndRoomId( )
    {
        string? seen = null;
        await WithRoutingClient(
            url =>
            {
                seen = url;
                return """{"code":0,"data":{"live_status":0}}""";
            },
            ( ) => LiveFetcher.FetchPlayInfoAsync("23058", 400, new AppConfig( ), TestContext.Current.CancellationToken));

        Assert.NotNull(seen);
        Assert.Contains("room_id=23058", seen, StringComparison.Ordinal);
        Assert.Contains("qn=400", seen, StringComparison.Ordinal);
        Assert.Contains("protocol=0", seen, StringComparison.Ordinal);
        Assert.Contains("format=0", seen, StringComparison.Ordinal);
    }
}
