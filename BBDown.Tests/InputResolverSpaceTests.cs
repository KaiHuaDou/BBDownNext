using System.Threading.Tasks;

using BBDown.Core;

namespace BBDown.Tests;

public class InputResolverSpaceTests
{
    // 空间投稿解析为纯字符串处理，不触网（mid 由 UidRegex 从 URL 抽取，裸数字/space 简写直接构造），
    // 故可在无网络环境下断言内部 id 形态。
    [Theory]
    [InlineData("https://space.bilibili.com/402787936", "spaceMid:402787936")]
    [InlineData("https://space.bilibili.com/402787936/", "spaceMid:402787936")]
    [InlineData("https://space.bilibili.com/402787936/upload/video", "spaceMid:402787936")]
    [InlineData("https://space.bilibili.com/402787936/video?tid=0", "spaceMid:402787936")]
    [InlineData("402787936", "ep:402787936")]
    [InlineData("space402787936", "spaceMid:402787936")]
    public async Task GetAvIdAsync_SpaceInput_ResolvesToSpaceMidPrefix(string input, string expected)
    {
        var result = await InputResolver.GetAvIdAsync(input, AppConfig.Empty);
        Assert.Equal(expected, result);
    }

    // 回归护栏：合集/系列/收藏夹的 space 子路径必须仍走各自分支，不被新的空间兜底吞掉
    [Theory]
    [InlineData("https://space.bilibili.com/392959666/lists/1560264?type=season", "listBizId:1560264")]
    [InlineData("https://space.bilibili.com/392959666/lists/1560264?type=series", "seriesBizId:1560264")]
    [InlineData("https://space.bilibili.com/3/favlist?fid=12345", "favId:12345:3")]
    public async Task GetAvIdAsync_SpaceSubPages_StillRouteToOwnFetchers(string input, string expected)
    {
        var result = await InputResolver.GetAvIdAsync(input, AppConfig.Empty);
        Assert.Equal(expected, result);
    }
}
