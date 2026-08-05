using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core.Fetcher;
using BBDown.Core.Util;

namespace BBDown.Core.Tests;

// 与 HttpStubFetcherTests 共用同一串行集合：两者都替换进程级静态 AppHttpClient
[Collection<HttpStubCollectionDefinition>]
public class BangumiInfoFetcherTests
{
    // 整季（md 输入被解析为 ep:ss{season_id} 后进入此形态）：按 season_id 拉取整季正片，
    // Index 留空 → 全量下载；section（OP/ED/PV）不计入正片。
    private const string SeasonJson = """
    {
      "code": 0,
      "message": "success",
      "result": {
        "cover": "http://i0.hdslb.com/bfs/cover.jpg",
        "title": "魔法少女小圆",
        "evaluate": "简介",
        "publish": { "pub_time": "2011-01-06 00:00:00", "is_finish": 1 },
        "is_finish": 0,
        "episodes": [
          { "id": 63470, "aid": 1358885, "cid": 16471668, "title": "1", "long_title": "似乎在梦里见过，那样...", "badge": "", "pub_time": 1406891700, "dimension": { "width": 640, "height": 360, "rotate": 0 } },
          { "id": 63471, "aid": 1358886, "cid": 16471669, "title": "2", "long_title": "那是早已司空见惯的景色", "badge": "", "pub_time": 1406891701, "dimension": { "width": 640, "height": 360, "rotate": 0 } },
          { "id": 63472, "aid": 1358887, "cid": 16471670, "title": "3", "long_title": "我也想要一枚灵魂宝石", "badge": "", "pub_time": 1406891702, "dimension": { "width": 640, "height": 360, "rotate": 0 } }
        ],
        "section": [
          { "title": "主题曲", "episodes": [ { "id": 99999, "aid": 1, "cid": 1, "title": "OP", "long_title": "OP", "badge": "", "pub_time": 1, "dimension": { "width": 640, "height": 360, "rotate": 0 } } ] }
        ]
      }
    }
    """;

    private sealed class BangumiStubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(responder(request));
        }
    }

    private static async Task<T> WithStubClient<T>(string body, Func<Task<T>> act)
    {
        var original = HTTPUtil.AppHttpClient;
        using var handler = new BangumiStubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        });
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

    [Fact]
    public async Task FetchAsync_SsForm_PullsWholeSeasonWithEmptyIndex( )
    {
        var info = await WithStubClient(SeasonJson, ( ) => BangumiInfoFetcher.FetchAsync("ep:ss2539", AppConfig.Empty));

        Assert.Equal("魔法少女小圆", info.Title);
        Assert.Equal(3, info.PagesInfo.Count);          // 仅正片，不含 section 里的 OP
        Assert.Equal("", info.Index);                    // Index 留空 → 全量下载
        Assert.Equal("63470", info.PagesInfo[0].epid);   // 首集即 ep63470
        Assert.True(info.IsBangumi);
    }

    [Fact]
    public async Task FetchAsync_EpForm_LocatesSingleEpisodeAndSetsIndex( )
    {
        // ep 形态回归：按 ep_id 拉整季、定位到目标集的 Index，且 section 扫描仍生效
        var info = await WithStubClient(SeasonJson, ( ) => BangumiInfoFetcher.FetchAsync("ep:63471", AppConfig.Empty));

        Assert.Equal(3, info.PagesInfo.Count);
        Assert.Equal("2", info.Index);                   // 第 2 集的 index
        Assert.Equal("63471", info.PagesInfo.Find(p => p.epid == "63471")!.epid);
    }

    [Fact]
    public async Task FetchAsync_SsForm_ApiError_ThrowsInvalidOpNotBangumiNotFound( )
    {
        // ss 形态接口无 result 时，必须抛 InvalidOperationException（而非 BangumiNotFoundException），
        // 否则会触发 FetcherRegistry 的课程误回退。
        var body = "{\"code\":-404,\"message\":\"番剧不存在\"}";
        await Assert.ThrowsAsync<InvalidOperationException>(( ) =>
            WithStubClient(body, ( ) => BangumiInfoFetcher.FetchAsync("ep:ss2539", AppConfig.Empty)));
    }
}
