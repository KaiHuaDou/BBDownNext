using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core;
using BBDown.Core.Util;

namespace BBDown.Core.Tests;

// watchlater 链接解析：带 bvid/oid 参数时只下载该单个视频（bvid 本地解码，oid 直接作 aid），
// 否则整个列表。纯字符串路径不触网；带参数路径结果仍是纯数字，会经 FixAvidAsync 的 HEAD
// 检查（同 video/bv 分支），需替换进程级 AppHttpClient，故挂串行集合。
[Collection<HttpStubCollectionDefinition>]
public class InputResolverWatchLaterTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // HEAD 固定 200 且不重定向：FixAvidAsync 看到的最终 URL 即原地址（不含 /ep），数字保持原样
            var response = new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request };
            return Task.FromResult(response);
        }
    }

    private static async Task<T> WithStubClient<T>(Func<Task<T>> act)
    {
        var original = HTTPUtil.AppHttpClient;
        using var handler = new StubHandler( );
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

    // 纯 "watchlater" 关键字（忽略大小写）→ 整个列表（纯字符串解析，不触网）
    [Theory]
    [InlineData("watchlater")]
    [InlineData("WATCHLATER")]
    [InlineData("WatchLater")]
    [InlineData("wAtChLaTeR")]
    public async Task ResolveIdAsync_WatchLaterKeyword_ResolvesToListPrefix(string input)
    {
        var result = await InputResolver.ResolveIdAsync(input, AppConfig.Empty, TestContext.Current.CancellationToken);
        Assert.Equal(new ResourceId.WatchLater( ), result);
    }

    // 不带 bvid/oid 的稍后再看地址 → 整个列表（纯字符串解析，不触网）
    [Theory]
    [InlineData("https://www.bilibili.com/watchlater/")]
    [InlineData("https://www.bilibili.com/watchlater")]
    [InlineData("https://www.bilibili.com/watchlater/#/list")]
    [InlineData("https://www.bilibili.com/list/watchlater")]
    public async Task ResolveIdAsync_WatchLaterUrl_ResolvesToListPrefix(string input)
    {
        var result = await InputResolver.ResolveIdAsync(input, AppConfig.Empty, TestContext.Current.CancellationToken);
        Assert.Equal(new ResourceId.WatchLater( ), result);
    }

    // 分享链接带 bvid/oid → 单个视频的 aid（数字）；bvid 本地解码，oid 直接作 aid，两者同指
    [Theory]
    [InlineData("https://www.bilibili.com/list/watchlater?spm_id_from=333.881.0.0&watchlater_cfg=%7B%22viewed%22%3A0,%22key%22%3A%22%22,%22asc%22%3Afalse%7D&oid=116802390592375&bvid=BV1cijQ6tEbM&vd_source=7943974d6eba133f292e98d4aadcd9e5")]
    [InlineData("https://www.bilibili.com/list/watchlater?bvid=BV1cijQ6tEbM")]
    [InlineData("https://www.bilibili.com/list/watchlater?oid=116802390592375")]
    [InlineData("https://www.bilibili.com/list/watchlater?watchlater_cfg=%7B%22viewed%22%3A0%7D&oid=116802390592375")]
    public async Task ResolveIdAsync_WatchLaterUrlWithVideoParams_ResolvesToSingleVideo(string input)
    {
        var result = await WithStubClient(( ) => InputResolver.ResolveIdAsync(input, AppConfig.Empty));
        Assert.Equal(new ResourceId.Av(116802390592375), result);
    }

    // 带 bvid 与 oid 冲突时以 bvid 为准（本地解码，与 video/bv 分支一致）
    [Fact]
    public async Task ResolveIdAsync_WatchLaterUrl_BvidTakesPrecedenceOverOid( )
    {
        var result = await WithStubClient(( ) => InputResolver.ResolveIdAsync(
            "https://www.bilibili.com/list/watchlater?oid=111&bvid=BV1cijQ6tEbM", AppConfig.Empty));
        Assert.Equal(new ResourceId.Av(116802390592375), result);
    }
}
