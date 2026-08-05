using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core;
using BBDown.Core.Util;

namespace BBDown.Tests;

// md / ss 解析为番剧季号（整季入口），属于触网路径；与 ResumeDownloadTests 共用串行集合，
// 因为两者都会替换进程级静态 AppHttpClient，并行会互相踩踏。
// md 经 pgc/review/user 映射 media_id→season_id；ss 经 pgc/view/web/season 直接取 season_id。
// 两者都产出内部 id "ep:ss{season_id}"，与 BangumiInfoFetcher 的整季形态一致。
[Collection("DownloadHttpStub")]
public class BangumiMdTests
{
    // 仅保留映射所需的字段：result.media.season_id。旧实现取 new_ep.id（最新一集）已改为整季。
    private const string ReviewUserJson = """
    {
      "code": 0,
      "message": "success",
      "result": {
        "media": {
          "season_id": 2539,
          "media_id": 2539
        }
      }
    }
    """;

    // ss 季号解析经 pgc/view/web/season 取 season_id；入口是 season_id 而非 media_id。
    private const string SeasonJson = """
    {
      "code": 0,
      "message": "success",
      "result": {
        "season_id": 2539
      }
    }
    """;

    private sealed class StubHandler(string reviewUserBody, string? seasonBody = null) : HttpMessageHandler
    {
        private readonly string reviewUserBody = reviewUserBody;
        private readonly string? seasonBody = seasonBody;

        public int ReviewUserCalls { get; private set; }
        public int SeasonCalls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath == "/pgc/review/user")
            {
                ReviewUserCalls++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(reviewUserBody, Encoding.UTF8, "application/json")
                });
            }

            if (seasonBody is not null && request.RequestUri.AbsolutePath == "/pgc/view/web/season")
            {
                SeasonCalls++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(seasonBody, Encoding.UTF8, "application/json")
                });
            }

            // 未匹配任何已知映射接口一律 500：顺带断言只发生了预期的那一次 HTTP
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        }
    }

    private static async Task<T> WithStubClient<T>(StubHandler handler, Func<Task<T>> act)
    {
        var original = HTTPUtil.AppHttpClient;
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

    [Theory]
    [InlineData("https://www.bilibili.com/bangumi/media/md2539")]
    [InlineData("https://www.bilibili.com/bangumi/media/md2539/")]            // 尾斜杠
    [InlineData("https://www.bilibili.com/bangumi/media/md2539?from=search")] // 带查询串
    [InlineData("md2539")]                                                    // 简写
    public async Task GetAvIdAsync_BangumiMd_ResolvesToEpSs(string input)
    {
        using var handler = new StubHandler(ReviewUserJson);
        var result = await WithStubClient(handler, ( ) => InputResolver.GetAvIdAsync(input, AppConfig.Empty));

        Assert.Equal("ep:ss2539", result);
        Assert.Equal(1, handler.ReviewUserCalls); // 只应请求一次 review/user
    }

    [Theory]
    [InlineData("https://www.bilibili.com/bangumi/play/ss2539")]
    [InlineData("https://www.bilibili.com/bangumi/play/ss2539/")]      // 尾斜杠
    [InlineData("ss2539")]                                            // 简写
    public async Task GetAvIdAsync_BangumiSs_ResolvesToEpSs(string input)
    {
        // ss 与 md 必须产出完全一致的内部 id：整季形态 ep:ss{season_id}，
        // 从而两者走同一条 Fetcher 整季路径，无特判。
        using var handler = new StubHandler(ReviewUserJson, SeasonJson);
        var result = await WithStubClient(handler, ( ) => InputResolver.GetAvIdAsync(input, AppConfig.Empty));

        Assert.Equal("ep:ss2539", result);
        Assert.Equal(1, handler.SeasonCalls);   // 只应请求一次 season
        Assert.Equal(0, handler.ReviewUserCalls);
    }

    [Fact]
    public async Task GetAvIdAsync_BangumiMd_ApiError_ThrowsReadableMessage( )
    {
        // 旧实现会把 "md2539" 直接拼进 media_id 导致 -400，再因缺 result 抛 KeyNotFoundException。
        // 现在应抛带 code/message 的可读异常，而非 KeyNotFoundException。
        using var handler = new StubHandler("{\"code\":-400,\"message\":\"请求错误\"}");
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(( ) =>
            WithStubClient(handler, ( ) => InputResolver.GetAvIdAsync("md2539", AppConfig.Empty)));

        Assert.Contains("-400", ex.Message);
        Assert.Contains("请求错误", ex.Message);
    }
}
