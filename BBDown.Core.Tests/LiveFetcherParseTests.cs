using System;
using System.Text.Json;

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

    // 带加密标记的轨道下载下来也放不了，须跳过并落到下一个编码
    private const string EncryptedAvcJson = """
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

    private static JsonElement Parse(string json)
    {
        return JsonDocument.Parse(json).RootElement;
    }

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

    [Fact]
    public void ParsePlayInfo_EncryptedCodec_IsSkipped( )
    {
        var info = LiveFetcher.ParsePlayInfo(Parse(EncryptedAvcJson), 250);

        Assert.NotNull(info);
        Assert.Equal("hevc", Assert.Single(info.Candidates).CodecName);
    }

    // 全部轨道都带加密标记时不能返回空壳，要当作「拿不到流」
    [Fact]
    public void ParsePlayInfo_AllEncrypted_ReturnsNull( )
    {
        var json = EncryptedAvcJson.Replace("\"drm\": false", "\"drm\": true", StringComparison.Ordinal);

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
