using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace BBDown.Core.Tests;

// md / ss 解析为番剧季号（整季入口）：md 经 pgc/review/user 映射 media_id→season_id
// ss 经 pgc/view/web/season 直接取 season_id。两者都产出内部 id Season(season_id)
// 与 BangumiInfoFetcher 的整季形式一致。因触网需替换进程级静态 AppHttpClient，挂串行集合
[Collection<HttpStubCollectionDefinition>]
public class BangumiMdTests
{
    // 仅保留映射所需的字段：result.media.season_id
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

    // ss 季号解析经 pgc/view/web/season 取 season_id；入口是 season_id 而非 media_id
    private const string SeasonJson = """
    {
      "code": 0,
      "message": "success",
      "result": {
        "season_id": 2539
      }
    }
    """;

    [Theory]
    [InlineData("https://www.bilibili.com/bangumi/media/md2539")]
    [InlineData("https://www.bilibili.com/bangumi/media/md2539/")]            // 尾斜杠
    [InlineData("https://www.bilibili.com/bangumi/media/md2539?from=search")] // 带查询串
    [InlineData("md2539")]                                                    // 简写
    public async Task ResolveIdAsync_BangumiMd_ResolvesToEpSs(string input)
    {
        var reviewUserCalls = 0;
        var result = await HttpStub.WithResponder(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/pgc/review/user")
            {
                reviewUserCalls++;
                return HttpStub.Json(ReviewUserJson);
            }

            // 未匹配任何已知映射接口一律 500：顺带断言只发生了预期的那一次 HTTP
            return new HttpResponseMessage(HttpStatusCode.InternalServerError);
        }, ( ) => InputResolver.ResolveIdAsync(input, AppConfig.Empty));

        Assert.Equal(new ResourceId.Season(2539), result);
        Assert.Equal(1, reviewUserCalls); // 只应请求一次 review/user
    }

    [Theory]
    [InlineData("https://www.bilibili.com/bangumi/play/ss2539")]
    [InlineData("https://www.bilibili.com/bangumi/play/ss2539/")]      // 尾斜杠
    [InlineData("ss2539")]                                            // 简写
    public async Task ResolveIdAsync_BangumiSs_ResolvesToEpSs(string input)
    {
        // ss 与 md 必须产出完全一致的内部 id：整季形式 Season(season_id)
        // 从而两者走同一条 Fetcher 整季路径，无特判
        var seasonCalls = 0;
        var reviewUserCalls = 0;
        var result = await HttpStub.WithResponder(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/pgc/review/user")
            {
                reviewUserCalls++;
                return HttpStub.Json(ReviewUserJson);
            }

            if (request.RequestUri.AbsolutePath == "/pgc/view/web/season")
            {
                seasonCalls++;
                return HttpStub.Json(SeasonJson);
            }

            return new HttpResponseMessage(HttpStatusCode.InternalServerError);
        }, ( ) => InputResolver.ResolveIdAsync(input, AppConfig.Empty));

        Assert.Equal(new ResourceId.Season(2539), result);
        Assert.Equal(1, seasonCalls);       // 只应请求一次 season
        Assert.Equal(0, reviewUserCalls);
    }

    [Fact]
    public async Task ResolveIdAsync_BangumiMd_ApiError_ThrowsReadableMessage( )
    {
        // 接口报错时应抛带 code/message 的可读异常（ApiException），而非 KeyNotFoundException
        var ex = await Assert.ThrowsAsync<ApiException>(( ) => HttpStub.WithResponder(
            _ => HttpStub.Json("""{"code":-400,"message":"请求错误"}"""),
            ( ) => InputResolver.ResolveIdAsync("md2539", AppConfig.Empty)));

        Assert.Equal(-400, ex.Code);
        Assert.Contains("-400", ex.Message);
        Assert.Contains("请求错误", ex.Message);
    }
}
