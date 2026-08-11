using System.Threading.Tasks;

using BBDown.Core;

namespace BBDown.Tests;

public class InputResolverSpaceTests
{
    // 空间投稿解析为纯字符串处理，不触网（mid 由 UidRegex 从 URL 抽取，裸数字/space 简写直接构造），
    // 故可在无网络环境下断言内部 id 形态。
    public static TheoryData<string, ResourceId> SpaceCases => new( )
    {
        { "https://space.bilibili.com/402787936", new ResourceId.Space(402787936) },
        { "https://space.bilibili.com/402787936/", new ResourceId.Space(402787936) },
        { "https://space.bilibili.com/402787936/upload/video", new ResourceId.Space(402787936) },
        { "https://space.bilibili.com/402787936/video?tid=0", new ResourceId.Space(402787936) },
        { "402787936", new ResourceId.Av(402787936) },
        { "space402787936", new ResourceId.Space(402787936) },
    };

    [Theory]
    [MemberData(nameof(SpaceCases))]
    public async Task ResolveIdAsync_SpaceInput_ResolvesCorrectly(string input, ResourceId expected)
    {
        var result = await InputResolver.ResolveIdAsync(input, AppConfig.Empty);
        Assert.Equal(expected, result);
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
        var result = await InputResolver.ResolveIdAsync(input, AppConfig.Empty);
        Assert.Equal(expected, result);
    }
}
