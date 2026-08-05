using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core.Comment;
using BBDown.Core.Util;

using Xunit;

namespace BBDown.Core.Tests;

// 与 HttpStubFetcherTests 共用同一串行集合：它们都会替换进程级静态 AppHttpClient
[Collection<HttpStubCollectionDefinition>]
public class CommentFetcherTests
{
    private const string SinglePageBody = """
    {
      "code": 0,
      "message": "0",
      "data": {
        "cursor": {
          "all_count": 100,
          "is_end": true,
          "next": 0,
          "pagination_reply": { "next_offset": "" }
        },
        "top": {
          "upper": {
            "rpid_str": "999",
            "member": { "uname": "置顶君", "level_info": { "current_level": 6 } },
            "content": { "message": "置顶内容" },
            "ctime": 1700000000,
            "like": 9999,
            "rcount": 0,
            "up_action": { "like": true },
            "reply_control": { "location": "IP属地：北京" }
          },
          "replies": []
        },
        "top_replies": [],
        "replies": [
          {
            "rpid_str": "111",
            "member": { "uname": "甲", "level_info": { "current_level": 5 } },
            "content": { "message": "第一条\n第二行", "pictures": [ { "img_src": "http://img/1.jpg" } ] },
            "ctime": 1700000100,
            "like": 123,
            "rcount": 2,
            "up_action": { "like": false },
            "reply_control": { "location": "IP属地：河北" },
            "replies": [
              { "rpid_str": "1111", "member": { "uname": "乙", "level_info": { "current_level": 3 } }, "content": { "message": "回复1" }, "ctime": 1700000200, "like": 5, "rcount": 0, "up_action": { "like": false }, "reply_control": { "location": "" } }
            ]
          },
          {
            "rpid_str": "222",
            "member": { "uname": "丙", "level_info": { "current_level": 4 } },
            "content": { "message": "无location" },
            "ctime": 1700000300,
            "like": 88,
            "rcount": 0,
            "up_action": { "like": false },
            "reply_control": { }
          },
          {
            "rpid_str": "333",
            "member": { "uname": "丁", "level_info": { "current_level": 2 } },
            "content": { "message": "第三条" },
            "ctime": 1700000400,
            "like": 7,
            "rcount": 0,
            "up_action": { "like": false },
            "reply_control": { "location": "" }
          }
        ]
      }
    }
    """;

    // 翻页：第一页 2 条且未结束，第二页含 1 条重复 + 1 条新，结束
    private const string Page1Body = """
    {
      "code": 0,
      "data": {
        "cursor": { "all_count": 100, "is_end": false, "next": 1, "pagination_reply": { "next_offset": "X" } },
        "top": { "upper": { "rpid_str": "999", "member": { "uname": "置顶", "level_info": { "current_level": 6 } }, "content": { "message": "t" }, "ctime": 1, "like": 1, "rcount": 0, "up_action": { "like": false }, "reply_control": { "location": "" } }, "replies": [] },
        "replies": [
          { "rpid_str": "111", "member": { "uname": "甲", "level_info": { "current_level": 5 } }, "content": { "message": "一" }, "ctime": 1, "like": 1, "rcount": 0, "up_action": { "like": false }, "reply_control": { "location": "" } },
          { "rpid_str": "222", "member": { "uname": "乙", "level_info": { "current_level": 4 } }, "content": { "message": "二" }, "ctime": 1, "like": 1, "rcount": 0, "up_action": { "like": false }, "reply_control": { "location": "" } }
        ]
      }
    }
    """;

    private const string Page2Body = """
    {
      "code": 0,
      "data": {
        "cursor": { "all_count": 100, "is_end": true, "next": 2, "pagination_reply": { "next_offset": "" } },
        "replies": [
          { "rpid_str": "222", "member": { "uname": "乙", "level_info": { "current_level": 4 } }, "content": { "message": "二" }, "ctime": 1, "like": 1, "rcount": 0, "up_action": { "like": false }, "reply_control": { "location": "" } },
          { "rpid_str": "333", "member": { "uname": "丙", "level_info": { "current_level": 3 } }, "content": { "message": "三" }, "ctime": 1, "like": 1, "rcount": 0, "up_action": { "like": false }, "reply_control": { "location": "" } }
        ]
      }
    }
    """;

    private sealed class StubHttpMessageHandler(Func<int, string> bodyForCall) : HttpMessageHandler
    {
        private int calls;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = bodyForCall(calls++);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }

    private static async Task<T> WithStubClient<T>(IReadOnlyList<string> bodies, Func<Task<T>> act)
    {
        var original = HTTPUtil.AppHttpClient;
        using var handler = new StubHttpMessageHandler(i => bodies[Math.Min(i, bodies.Count - 1)]);
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
    public void PaginationStr_WrapsOffsetAsJsonString( )
    {
        Assert.Equal("""{"offset":""}""", CommentFetcher.PaginationStr(""));
        Assert.Equal("""{"offset":"abc"}""", CommentFetcher.PaginationStr("abc"));
    }

    [Fact]
    public void BuildOffset_Mode2UsesUppercaseDataCursor( )
    {
        Assert.Equal("""{"type":3,"direction":1,"Data":{"cursor":123}}""", CommentFetcher.BuildOffset(2, 123));
    }

    [Fact]
    public void BuildOffset_Mode3UsesLowercaseDataPn( )
    {
        Assert.Equal("""{"type":1,"direction":1,"data":{"pn":456}}""", CommentFetcher.BuildOffset(3, 456));
    }

    [Fact]
    public async Task FetchAsync_SinglePage_ReturnsAllRepliesAndStopsAtIsEnd( )
    {
        var doc = await WithStubClient([SinglePageBody], ( ) => CommentFetcher.FetchAsync("170001", 100, sortHot: true, fullReplies: false, AppConfig.Empty, TestContext.Current.CancellationToken));

        Assert.Equal(100, doc.AllCount);
        Assert.Equal("hot", doc.Sort);
        Assert.Equal(4, doc.Comments.Count); // 1 置顶 + 3 条
        Assert.Contains(doc.Comments, c => c.Rpid == "999" && c.Top);
        var first = Assert.Single(doc.Comments.FindAll(c => c.Rpid == "111"));
        Assert.Equal("甲", first.Uname);
        Assert.Equal("IP属地：河北", first.Location);
        Assert.Single(first.Pictures);
        Assert.Single(first.Replies); // 内联楼中楼
        Assert.DoesNotContain(doc.Comments, c => c.Rpid == "222" && c.Location.Length != 0); // 空 location 解析为空串
    }

    [Fact]
    public async Task FetchAsync_Pagination_DedupsByRpid( )
    {
        // limit=5 > 首页 3 条（置顶 + 2），强制翻第二页；第二页含重复的 222 与新的 333
        var doc = await WithStubClient([Page1Body, Page2Body], ( ) => CommentFetcher.FetchAsync("170001", 5, sortHot: true, fullReplies: false, AppConfig.Empty, TestContext.Current.CancellationToken));

        Assert.Equal(4, doc.Comments.Count);
        Assert.Equal(["999", "111", "222", "333"], doc.Comments.ConvertAll(c => c.Rpid));
        Assert.Single(doc.Comments.FindAll(c => c.Rpid == "222")); // 跨页去重，222 只出现一次
    }

    [Fact]
    public async Task FetchAsync_ClosedArea_ReturnsEmpty( )
    {
        var doc = await WithStubClient(["""{"code":12002,"message":"评论区已关闭","data":null}"""], ( ) => CommentFetcher.FetchAsync("170001", 10, sortHot: true, fullReplies: false, AppConfig.Empty, TestContext.Current.CancellationToken));

        Assert.Empty(doc.Comments);
    }

    [Fact]
    public async Task FetchAsync_SignatureRejected_Throws( )
    {
        await Assert.ThrowsAsync<InvalidOperationException>( ( ) => WithStubClient(
            ["""{"code":-403,"message":"请求被拦截"}"""],
            ( ) => CommentFetcher.FetchAsync("170001", 10, sortHot: true, fullReplies: false, AppConfig.Empty, TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task FetchAsync_RepliesNullTreatedAsRiskControl( )
    {
        // code 为 0 但 data.replies 缺失：风控下发 v_voucher 的形态，降级为拿到多少算多少（这里 0 条）
        var doc = await WithStubClient(["""{"code":0,"data":{"cursor":{"all_count":0,"is_end":true,"next":0,"pagination_reply":{"next_offset":""}},"replies":null}}"""],
            ( ) => CommentFetcher.FetchAsync("170001", 10, sortHot: true, fullReplies: false, AppConfig.Empty, TestContext.Current.CancellationToken));

        Assert.Empty(doc.Comments);
    }
}
