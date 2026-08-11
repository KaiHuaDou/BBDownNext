using System.Threading.Tasks;

using BBDown.Core;

namespace BBDown.Tests;

public class InputResolverCheeseTests
{
    // cheese 解析为纯字符串处理，不触网（ss 形式不再预先请求接口取首集 ep），
    // 故可在无网络环境下断言内部 id 形态。
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
        // ss 形态必须保留为 CheeseSeason，CheeseInfoFetcher 才能按 season_id 直接拉取整季、避免二次请求。
        var result = await InputResolver.ResolveIdAsync("https://www.bilibili.com/cheese/play/ss61", AppConfig.Empty, TestContext.Current.CancellationToken);
        Assert.IsType<ResourceId.CheeseSeason>(result);
    }
}
