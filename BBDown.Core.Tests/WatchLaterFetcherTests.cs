using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

using BBDown.Core.Fetcher;

namespace BBDown.Core.Tests;

[Collection<HttpStubCollectionDefinition>]
public class WatchLaterFetcherTests
{
    // 单 P 条目 + 多 P 条目混合：多 P 条目会回填 wbi/view 接口展开分 P
    private const string ToviewJson = """
    {
      "code": 0,
      "message": "0",
      "data": {
        "count": 2,
        "list": [
          {
            "aid": 1001,
            "videos": 1,
            "cid": 2001,
            "title": "视频A",
            "duration": 120,
            "pubdate": 1690000000,
            "pic": "http://cover/a.jpg",
            "desc": "视频A简介",
            "owner": { "mid": 12345, "name": "UP主A" }
          },
          {
            "aid": 1002,
            "videos": 2,
            "cid": 2002,
            "title": "视频B",
            "duration": 300,
            "pubdate": 1690001000,
            "pic": "http://cover/b.jpg",
            "desc": "视频B简介",
            "owner": { "mid": 67890, "name": "UP主B" }
          }
        ]
      }
    }
    """;

    // NormalInfoFetcher 回填用的 view 桩
    private const string ViewJson = """
    {
      "code": 0,
      "message": "0",
      "data": {
        "aid": 1002,
        "bvid": "BV1xx",
        "title": "视频B",
        "desc": "视频B简介",
        "pic": "http://cover/b.jpg",
        "pubdate": 1690001000,
        "cid": 2002,
        "owner": { "mid": 67890, "name": "UP主B" },
        "rights": { "is_stein_gate": 0 },
        "pages": [
          { "cid": 2002, "page": 1, "part": "P1", "duration": 100 },
          { "cid": 2003, "page": 2, "part": "P2", "duration": 200 }
        ]
      }
    }
    """;

    [Fact]
    public async Task Fetch_SingleAndMultiPage_FlattensToPages( )
    {
        var info = await HttpStub.WithResponder(request =>
        {
            var body = request.RequestUri!.ToString( ).Contains("/wbi/view")
                ? ViewJson
                : ToviewJson;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
        }, ( ) => WatchLaterFetcher.FetchAsync(IdPrefix.WatchLater, AppConfig.Empty));

        Assert.Equal("稍后再看", info.Title);
        Assert.Equal(3, info.PagesInfo.Count);

        var first = info.PagesInfo[0];
        Assert.Equal("1001", first.aid);
        Assert.Equal("2001", first.cid);
        Assert.Equal("视频A", first.title);
        Assert.Equal(120, first.dur);
        Assert.Equal("UP主A", first.ownerName);

        var second = info.PagesInfo[1];
        Assert.Equal("1002", second.aid);
        Assert.Equal("2002", second.cid);
        Assert.Equal("视频B_P1_P1", second.title);

        var third = info.PagesInfo[2];
        Assert.Equal("1002", third.aid);
        Assert.Equal("2003", third.cid);
        Assert.Equal("视频B_P2_P2", third.title);
    }

    [Fact]
    public async Task Fetch_EmptyList_Throws( )
    {
        const string json = """
        { "code": 0, "message": "0", "data": { "count": 0, "list": [] } }
        """;
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(( ) =>
            HttpStub.WithJsonResponse(json, ( ) => WatchLaterFetcher.FetchAsync(IdPrefix.WatchLater, AppConfig.Empty)));
        Assert.Contains("为空", ex.Message);
    }

    [Fact]
    public async Task Fetch_NotLoggedIn_ThrowsWithCookieHint( )
    {
        const string json = """
        { "code": -101, "message": "账号未登录" }
        """;
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(( ) =>
            HttpStub.WithJsonResponse(json, ( ) => WatchLaterFetcher.FetchAsync(IdPrefix.WatchLater, AppConfig.Empty)));
        Assert.Contains("SESSDATA", ex.Message);
    }
}
