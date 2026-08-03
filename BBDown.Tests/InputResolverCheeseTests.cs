using System.Threading.Tasks;
using BBDown;
using BBDown.Core;
using Xunit;

namespace BBDown.Tests;

public class InputResolverCheeseTests
{
    // cheese 解析为纯字符串处理，不触网（ss 形式不再预先请求接口取首集 ep），
    // 故可在无网络环境下断言内部 id 形态。
    [Theory]
    [InlineData("https://www.bilibili.com/cheese/play/ep790", "cheese:790")]
    [InlineData("https://m.bilibili.com/cheese/play/ep790", "cheese:790")]
    [InlineData("https://www.bilibili.com/cheese/play/ss61", "cheese:ss61")]
    [InlineData("https://m.bilibili.com/cheese/play/ss61", "cheese:ss61")]
    [InlineData("cheese/ep790", "cheese:790")]
    [InlineData("cheese/ss61", "cheese:ss61")]
    public async Task GetAvIdAsync_CheeseInput_ResolvesToCheesePrefix(string input, string expected)
    {
        var result = await InputResolver.GetAvIdAsync(input, AppConfig.Empty);
        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task GetAvIdAsync_CheeseSs_KeepsSeasonMarkerForFetcher()
    {
        // ss 形态必须保留 "ss" 前缀，CheeseInfoFetcher 才能按 season_id 直接拉取整季、避免二次请求。
        var result = await InputResolver.GetAvIdAsync("https://www.bilibili.com/cheese/play/ss61", AppConfig.Empty);
        Assert.StartsWith("cheese:ss", result);
    }
}
