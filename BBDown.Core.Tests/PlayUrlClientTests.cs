using BBDown.Core.PlayUrl;

namespace BBDown.Core.Tests;

/// <summary>
/// 大会员网页兜底的播放页地址构造（纯函数部分）。
/// 真实抓取与 window.__playinfo__ 抠取依赖网络，一律不测（见 AGENTS.md「测试范围约定」）。
/// </summary>
public class PlayUrlClientTests
{
    [Fact]
    public void BuildWebPageUrl_OfficialHost_KeepsOriginalForm( )
    {
        Assert.Equal("https://www.bilibili.com/bangumi/play/ep123",
            PlayUrlClient.BuildWebPageUrl(false, "123", BiliApi.MainHost));
    }

    [Fact]
    public void BuildWebPageUrl_CheeseUsesCheesePath( )
    {
        Assert.Equal("https://www.bilibili.com/cheese/play/ep123",
            PlayUrlClient.BuildWebPageUrl(true, "123", BiliApi.MainHost));
    }

    // 镜像站（--ep-host）同样提供该路径：硬编码官方域名会让镜像站用户被重定向回可能不可达的官方站
    [Theory]
    [InlineData(false, "https://mirror.example.com/bangumi/play/ep123")]
    [InlineData(true, "https://mirror.example.com/cheese/play/ep123")]
    public void BuildWebPageUrl_FollowsConfiguredEpHost(bool cheese, string expected)
    {
        Assert.Equal(expected, PlayUrlClient.BuildWebPageUrl(cheese, "123", "mirror.example.com"));
    }
}
