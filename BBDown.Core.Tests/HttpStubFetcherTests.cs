using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core.Fetcher;
using BBDown.Core.Util;

namespace BBDown.Core.Tests;

// HTTP 桩测试必须串行：它们会替换进程级静态 AppHttpClient，并行会互相踩踏
[CollectionDefinition]
public sealed class HttpStubCollectionDefinition;

[Collection<HttpStubCollectionDefinition>]
public class HttpStubFetcherTests
{
    // 单页收藏夹（2 条视频，均为单 P），只触发一次 HTTP 调用，不依赖 NormalInfoFetcher
    private const string FavJson = """
    {
      "data": {
        "info": {
          "media_count": 2,
          "title": "我的收藏夹",
          "intro": "收藏夹简介",
          "ctime": 1700000000,
          "upper": { "name": "UP主名" }
        },
        "medias": [
          {
            "attr": 0,
            "id": 1001,
            "page": 1,
            "title": "视频A",
            "duration": 120,
            "pubtime": 1690000000,
            "cover": "http://cover/a.jpg",
            "intro": "视频A简介",
            "upper": { "name": "UP主名", "mid": "12345" },
            "ugc": { "first_cid": 2001 }
          },
          {
            "attr": 0,
            "id": 1002,
            "page": 1,
            "title": "视频B",
            "duration": 90,
            "pubtime": 1690001000,
            "cover": "http://cover/b.jpg",
            "intro": "视频B简介",
            "upper": { "name": "UP主名", "mid": "12345" },
            "ugc": { "first_cid": 2002 }
          }
        ]
      }
    }
    """;

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(responder(request));
        }
    }

    private static async Task<T> WithStubClient<T>(HttpStatusCode status, string body, Func<Task<T>> act)
    {
        var original = HTTPUtil.AppHttpClient;
        using var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(status)
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
    public async Task FavList_SinglePage_ParsesMediasWithoutNetwork( )
    {
        var info = await WithStubClient(HttpStatusCode.OK, FavJson,
            ( ) => FavListFetcher.FetchAsync("https://space.bilibili.com/3/favlist?fid=12345:678", AppConfig.Empty));

        Assert.Equal("我的收藏夹", info.Title);
        Assert.Equal(2, info.PagesInfo.Count);

        var first = info.PagesInfo[0];
        Assert.Equal("1001", first.aid);
        Assert.Equal("2001", first.cid);
        Assert.Equal("视频A", first.title);
        Assert.Equal(120, first.dur);
        Assert.Equal("UP主名", first.ownerName);
        Assert.Equal("12345", first.ownerMid);

        var second = info.PagesInfo[1];
        Assert.Equal("1002", second.aid);
        Assert.Equal("2002", second.cid);
    }

    [Fact]
    public async Task FavList_NonSuccessStatus_PropagatesError( )
    {
        // 负向对照：桩返回 404 必须穿透为异常，证明请求确实走了桩而非真实网络
        await Assert.ThrowsAsync<HttpRequestException>(( ) =>
            WithStubClient(HttpStatusCode.NotFound, "",
                ( ) => FavListFetcher.FetchAsync("https://space.bilibili.com/3/favlist?fid=12345:678", AppConfig.Empty)));
    }
}
