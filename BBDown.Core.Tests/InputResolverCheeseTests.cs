using System;
using System.Threading.Tasks;

namespace BBDown.Core.Tests;

public class InputResolverCheeseTests
{
    // cheese 解析为纯字符串处理，不触网，故可在无网络环境下断言内部 id 形式
    public static TheoryData<string, ResourceId> CheeseCases => new( )
    {
        { "https://www.bilibili.com/cheese/play/ep790", new ResourceId.CheeseEp(790) },
        { "https://m.bilibili.com/cheese/play/ep790", new ResourceId.CheeseEp(790) },
        { "https://www.bilibili.com/cheese/play/ss61", new ResourceId.CheeseSeason(61) },
        { "https://m.bilibili.com/cheese/play/ss61", new ResourceId.CheeseSeason(61) },
        { "cheese/ep790", new ResourceId.CheeseEp(790) },
        { "cheese/ss61", new ResourceId.CheeseSeason(61) },
    };

    [Theory]
    [MemberData(nameof(CheeseCases))]
    public async Task ResolveIdAsync_CheeseInput_ResolvesToCheesePrefix(string input, ResourceId expected)
    {
        var result = await InputResolver.ResolveIdAsync(input, AppConfig.Empty, TestContext.Current.CancellationToken);
        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task ResolveIdAsync_CheeseSs_KeepsSeasonMarkerForFetcher( )
    {
        // ss 形式必须保留为 CheeseSeason，CheeseInfoFetcher 才能按 season_id 直接拉取整季、避免二次请求
        var result = await InputResolver.ResolveIdAsync("https://www.bilibili.com/cheese/play/ss61", AppConfig.Empty, TestContext.Current.CancellationToken);
        Assert.IsType<ResourceId.CheeseSeason>(result);
    }

    // 不带 ep / ss 的课程地址早前会落到「按 ep_id 解析」的兜底分支，报出指向 ep 的误导文案
    [Fact]
    public async Task ResolveIdAsync_CheeseWithoutEpOrSs_NamesBothForms( )
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(( ) => InputResolver.ResolveIdAsync(
            "https://www.bilibili.com/cheese/play/xxx", AppConfig.Empty, TestContext.Current.CancellationToken));

        Assert.Contains("ep 或 ss", ex.Message, StringComparison.Ordinal);
    }
}
