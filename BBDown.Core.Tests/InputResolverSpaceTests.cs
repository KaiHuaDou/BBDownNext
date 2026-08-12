using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core;
using BBDown.Core.Util;

namespace BBDown.Core.Tests;

// 裸数字用例经 FixAvidAsync 的 HEAD 探测替换进程级 AppHttpClient，挂串行集合防并行踩踏
[Collection<HttpStubCollectionDefinition>]
public class InputResolverSpaceTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // HEAD 固定 200 不重定向：FixAvidAsync 看到的最终 URL 即原地址（不含 /ep），数字保持原样
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request });
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

    // 空间投稿解析为纯字符串处理，不触网（mid 由 UidRegex 从 URL 抽取，space 简写直接构造），
    // 故可在无网络环境下断言内部 id 形态。
    public static TheoryData<string, ResourceId> SpaceCases => new( )
    {
        { "https://space.bilibili.com/402787936", new ResourceId.Space(402787936) },
        { "https://space.bilibili.com/402787936/", new ResourceId.Space(402787936) },
        { "https://space.bilibili.com/402787936/upload/video", new ResourceId.Space(402787936) },
        { "https://space.bilibili.com/402787936/video?tid=0", new ResourceId.Space(402787936) },
        { "space402787936", new ResourceId.Space(402787936) },
    };

    [Theory]
    [MemberData(nameof(SpaceCases))]
    public async Task ResolveIdAsync_SpaceInput_ResolvesCorrectly(string input, ResourceId expected)
    {
        var result = await InputResolver.ResolveIdAsync(input, AppConfig.Empty, TestContext.Current.CancellationToken);
        Assert.Equal(expected, result);
    }

    // 裸数字按 av 号识别（ResolveShorthandAsync 的数字分支）；HEAD 探测经 stub 保持 Av，不触网
    [Fact]
    public async Task ResolveIdAsync_BareDigits_KeepsAv( )
    {
        var result = await WithStubClient(( ) => InputResolver.ResolveIdAsync("402787936", AppConfig.Empty));
        Assert.Equal(new ResourceId.Av(402787936), result);
    }

    // 回归护栏：合集/系列/收藏夹的 space 子路径必须仍走各自分支，不被新的空间兜底吞掉
    public static TheoryData<string, ResourceId> SpaceSubPageCases => new( )
    {
        { "https://space.bilibili.com/392959666/lists/1560264?type=season", new ResourceId.MediaList(1560264) },
        { "https://space.bilibili.com/392959666/lists/1560264?type=series", new ResourceId.Series(1560264) },
        { "https://space.bilibili.com/3/favlist?fid=12345", new ResourceId.Fav(12345, 3) },
    };

    [Theory]
    [MemberData(nameof(SpaceSubPageCases))]
    public async Task ResolveIdAsync_SpaceSubPages_StillRouteToOwnFetchers(string input, ResourceId expected)
    {
        var result = await InputResolver.ResolveIdAsync(input, AppConfig.Empty, TestContext.Current.CancellationToken);
        Assert.Equal(expected, result);
    }
}
