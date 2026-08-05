using BBDown.Core.Live;

namespace BBDown.Core.Tests;

public class LiveInputResolverTests
{
    [Theory]
    [InlineData("https://live.bilibili.com/1754632560", "1754632560")]
    [InlineData("http://live.bilibili.com/1754632560", "1754632560")]
    [InlineData("//live.bilibili.com/1754632560", "1754632560")]
    [InlineData("live.bilibili.com/1754632560", "1754632560")]
    [InlineData("https://m.live.bilibili.com/1754632560", "1754632560")]
    [InlineData("https://live.bilibili.com/1754632560/", "1754632560")]
    [InlineData("https://live.bilibili.com/1754632560?spm_id_from=333.1007", "1754632560")]
    [InlineData("https://live.bilibili.com/1754632560#chat", "1754632560")]
    [InlineData("  https://live.bilibili.com/1754632560  ", "1754632560")]
    [InlineData("HTTPS://LIVE.BILIBILI.COM/1754632560", "1754632560")]
    public void TryParse_LiveUrl_ReturnsRoomId(string input, string expected)
    {
        Assert.True(LiveInputResolver.TryParse(input, out var target));
        Assert.Equal(expected, target.RoomId);
    }

    [Theory]
    [InlineData("https://live.bilibili.com/h5/3", "3")]
    [InlineData("https://live.bilibili.com/blanc/3", "3")]
    [InlineData("https://live.bilibili.com/blackboard/3", "3")]
    [InlineData("https://live.bilibili.com/blanc/3?liteVersion=true", "3")]
    public void TryParse_PathPrefix_IsStripped(string input, string expected)
    {
        Assert.True(LiveInputResolver.TryParse(input, out var target));
        Assert.Equal(expected, target.RoomId);
    }

    [Theory]
    [InlineData("live:1754632560", "1754632560")]
    [InlineData("LIVE:1754632560", "1754632560")]
    public void TryParse_LivePrefix_ReturnsRoomId(string input, string expected)
    {
        Assert.True(LiveInputResolver.TryParse(input, out var target));
        Assert.Equal(expected, target.RoomId);
    }

    // 宿主必须精确比对，任何形式的「包含」判定都会被这些输入攻破
    [Theory]
    [InlineData("https://evil.com/live.bilibili.com/12345")]
    [InlineData("https://evil.com/?x=live.bilibili.com/12345")]
    [InlineData("https://live.bilibili.com.evil.com/12345")]
    [InlineData("https://live.bilibili.com@evil.com/12345")]
    [InlineData("https://notlive.bilibili.com/12345")]
    public void TryParse_HostSpoofing_ReturnsFalse(string input)
    {
        Assert.False(LiveInputResolver.TryParse(input, out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("12345")]
    [InlineData("av123456")]
    [InlineData("BV1L411t7SU")]
    [InlineData("ep123456")]
    [InlineData("ss12345")]
    [InlineData("cv51908655")]
    [InlineData("https://www.bilibili.com/video/BV1L411t7SU")]
    [InlineData("https://www.bilibili.com/12345")]
    [InlineData("https://b23.tv/abcdefg")]
    [InlineData("https://live.bilibili.com/")]
    [InlineData("https://live.bilibili.com/p/eden/area-tags")]
    [InlineData("https://live.bilibili.com/abc")]
    [InlineData("live:")]
    [InlineData("live:abc")]
    public void TryParse_NonLiveInput_ReturnsFalse(string input)
    {
        Assert.False(LiveInputResolver.TryParse(input, out _));
    }

    // 0 是 short_id 的「无短号」占位值，不是合法房间
    [Theory]
    [InlineData("https://live.bilibili.com/0")]
    [InlineData("live:0")]
    [InlineData("live:000")]
    public void TryParse_ZeroRoomId_ReturnsFalse(string input)
    {
        Assert.False(LiveInputResolver.TryParse(input, out _));
    }
}
