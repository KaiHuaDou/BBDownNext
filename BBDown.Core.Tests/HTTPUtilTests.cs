using BBDown.Core.Util;

namespace BBDown.Core.Tests;

public class HTTPUtilTests
{
    [Theory]
    [InlineData("https://www.bilibili.com/bangumi/play/ep123456", true)]
    [InlineData("https://www.bilibili.com/bangumi/play/ss12345", true)]
    [InlineData("https://www.bilibili.com/cheese/play/ep123456", true)]
    [InlineData("https://www.bilibili.com/bangumi/play/ep123456/", true)]
    public void IsBangumiPlayPage_MatchesEpAndSsSegments(string url, bool expected)
    {
        Assert.Equal(expected, HTTPUtil.IsBangumiPlayPage(url));
    }

    // 旧实现是裸 Contains("/ep") || Contains("/ss")，这些都会被误判
    [Theory]
    [InlineData("https://api.bilibili.com/x/player/pagelist?bvid=BV1x")]
    [InlineData("https://api.bilibili.com/pgc/view/web/season?ep_id=123456")]
    [InlineData("https://api.bilibili.com/x/web-interface/view/episodes")]
    [InlineData("https://cdn.example.com/ssl/video.m4s")]
    [InlineData("https://cdn.example.com/upgcxcode/ep/xx.m4s")]
    [InlineData("not a url")]
    public void IsBangumiPlayPage_IgnoresIncidentalMatches(string url)
    {
        Assert.False(HTTPUtil.IsBangumiPlayPage(url));
    }

    [Theory]
    [InlineData("https://cn-cdn.bilivideo.com/v.m4s?platform=android", true)]
    [InlineData("https://cn-cdn.bilivideo.com/v.m4s?platform=android_tv_yst", true)]
    [InlineData("https://cn-cdn.bilivideo.com/v.m4s?deadline=1&platform=android&os=upos", true)]
    [InlineData("https://cn-cdn.bilivideo.com/v.m4s?platform=pc", false)]
    [InlineData("https://cn-cdn.bilivideo.com/v.m4s", false)]
    // 参数值里出现 platform=android 字样不算，必须是 platform 参数本身
    [InlineData("https://cn-cdn.bilivideo.com/v.m4s?trace=platform%3Dandroid", false)]
    public void IsAndroidPlatformUrl_ReadsPlatformQueryParam(string url, bool expected)
    {
        Assert.Equal(expected, HTTPUtil.IsAndroidPlatformUrl(url));
    }
}
