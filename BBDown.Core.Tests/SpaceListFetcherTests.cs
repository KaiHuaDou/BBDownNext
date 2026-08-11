using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core.Fetcher;
using BBDown.Core.Util;

namespace BBDown.Core.Tests;

// HTTP 桩测试必须串行：它们会替换进程级静态 AppHttpClient，并行会互相踩踏
[Collection<HttpStubCollectionDefinition>]
public class SpaceListFetcherTests
{
    // 单页空间投稿：4 条，其中 1 条课堂（预剔除）、1 条 view 必失败（跳过），其余 1+2 P 正常展开
    private const string ArcSearchJson = """
    {
      "code": 0,
      "data": {
        "list": {
          "vlist": [
            { "aid": 1001, "title": "视频A", "description": "简介A", "pic": "http://cover/a.jpg", "created": 1690000000, "author": "UP主名", "mid": "402787936", "is_lesson_video": 0, "is_live_playback": 0, "is_charging_arc": false },
            { "aid": 1002, "title": "视频B", "description": "简介B", "pic": "http://cover/b.jpg", "created": 1690001000, "author": "UP主名", "mid": "402787936", "is_lesson_video": 0, "is_live_playback": 0, "is_charging_arc": false },
            { "aid": 1003, "title": "课堂视频C", "description": "简介C", "pic": "http://cover/c.jpg", "created": 1690002000, "author": "UP主名", "mid": "402787936", "is_lesson_video": 1, "is_live_playback": 0, "is_charging_arc": false },
            { "aid": 1004, "title": "失败视频D", "description": "简介D", "pic": "http://cover/d.jpg", "created": 1690003000, "author": "UP主名", "mid": "402787936", "is_lesson_video": 0, "is_live_playback": 0, "is_charging_arc": false }
          ]
        },
        "page": { "count": 4, "pn": 1, "ps": 30 }
      }
    }
    """;

    private const string View1001 = """
    {
      "code": 0,
      "data": {
        "title": "视频A", "desc": "descA", "pic": "http://p/a.jpg",
        "owner": { "mid": "402787936", "name": "UP主名" },
        "pubdate": 1690000000, "bvid": "BV1aaaa", "cid": 2001,
        "rights": { "is_stein_gate": 0 },
        "pages": [ { "page": 1, "cid": 2001, "part": "P1", "duration": 120, "dimension": { "width": 1920, "height": 1080 } } ]
      }
    }
    """;

    private const string View1002 = """
    {
      "code": 0,
      "data": {
        "title": "视频B", "desc": "descB", "pic": "http://p/b.jpg",
        "owner": { "mid": "402787936", "name": "UP主名" },
        "pubdate": 1690001000, "bvid": "BV1bbbb", "cid": 2002,
        "rights": { "is_stein_gate": 0 },
        "pages": [
          { "page": 1, "cid": 2002, "part": "子标题1", "duration": 100, "dimension": { "width": 1920, "height": 1080 } },
          { "page": 2, "cid": 2003, "part": "子标题2", "duration": 110, "dimension": { "width": 1920, "height": 1080 } }
        ]
      }
    }
    """;

    // view 返回 -404：模拟已删除 / 无权等，应被跳过而非抛异常
    private const string ViewFail = """
    { "code": -404, "message": "啥都木有", "data": null }
    """;

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(responder(request));
        }
    }

    private static HttpResponseMessage Ok(string body)
    {
        return new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    private static async Task<T> WithRoutedStub<T>(Func<HttpRequestMessage, HttpResponseMessage> responder, Func<Task<T>> act)
    {
        var original = HTTPUtil.AppHttpClient;
        using var handler = new StubHttpMessageHandler(responder);
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

    private static string? GetQueryValue(string url, string key)
    {
        var q = url.Contains('?') ? url[(url.IndexOf('?') + 1)..] : "";
        foreach (var part in q.Split('&'))
        {
            var kv = part.Split('=', 2);
            if (kv.Length == 2 && kv[0] == key)
            {
                return kv[1];
            }
        }

        return null;
    }

    [Fact]
    public async Task SpaceList_ParsesAndFlattens_WithSkipAndLessonFilter( )
    {
        var requestedViewAids = new List<string>( );
        HttpResponseMessage responder(HttpRequestMessage req)
        {
            var url = req.RequestUri!.AbsoluteUri;
            if (url.Contains("/x/space/wbi/arc/search"))
            {
                return Ok(ArcSearchJson);
            }

            if (url.Contains("/x/web-interface/wbi/view"))
            {
                var aid = GetQueryValue(url, "aid");
                if (aid is not null)
                {
                    requestedViewAids.Add(aid);
                }

                return aid switch
                {
                    "1001" => Ok(View1001),
                    "1002" => Ok(View1002),
                    _ => Ok(ViewFail)
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        var info = await WithRoutedStub(responder, ( ) => SpaceListFetcher.FetchAsync(402787936, AppConfig.Empty));

        // 课堂(1003)被预剔除，不发起 view 请求；失败(1004)被跳过但不抛异常
        Assert.DoesNotContain("1003", requestedViewAids);
        Assert.Contains("1001", requestedViewAids);
        Assert.Contains("1002", requestedViewAids);
        Assert.Contains("1004", requestedViewAids);

        Assert.Equal("UP主名", info.Title);
        Assert.Equal("", info.Pic);                       // Pic 必须为空，逐分 P 才用各自 cover
        Assert.Equal(1690000000, info.PubTime);

        // 1001(单P) + 1002(两P) = 3 个待下载分 P
        Assert.Equal(3, info.PagesInfo.Count);

        var first = info.PagesInfo[0];
        Assert.Equal("视频A", first.Title);              // 单 P 直接用外层标题
        Assert.Equal("1001", first.Aid);
        Assert.Equal("2001", first.Cid);
        Assert.Equal(120, first.Dur);
        Assert.Equal("UP主名", first.OwnerName);
        Assert.Equal("402787936", first.OwnerMid);

        var second = info.PagesInfo[1];
        Assert.Equal("视频B_P1_子标题1", second.Title);   // 多 P 拼接外层标题 + 分 P 序号 + 子标题
        Assert.Equal("2002", second.Cid);

        var third = info.PagesInfo[2];
        Assert.Equal("视频B_P2_子标题2", third.Title);
        Assert.Equal("2003", third.Cid);

        // 失败稿件 1004 不应出现在结果中
        Assert.DoesNotContain(info.PagesInfo, p => p.Aid == "1004");
    }

    [Fact]
    public async Task SpaceList_RiskControlled_ThrowsWithHint( )
    {
        var riskJson = """{"code":0,"data":{"is_risk":true,"gaia_res_type":1,"gaia_data":{}}}""";
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(( ) =>
            WithRoutedStub(_ => Ok(riskJson), ( ) => SpaceListFetcher.FetchAsync(402787936, AppConfig.Empty)));
        Assert.Contains("风控", ex.Message);
    }
}
