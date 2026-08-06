using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

using BBDown.Core.Fetcher;

namespace BBDown.Core.Tests;

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

    [Fact]
    public async Task FavList_SinglePage_ParsesMediasWithoutNetwork( )
    {
        var info = await HttpStub.WithJsonResponse(FavJson,
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
            HttpStub.WithResponder(_ => new HttpResponseMessage(HttpStatusCode.NotFound),
                ( ) => FavListFetcher.FetchAsync("https://space.bilibili.com/3/favlist?fid=12345:678", AppConfig.Empty)));
    }
}
