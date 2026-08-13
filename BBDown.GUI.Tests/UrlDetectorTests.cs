namespace BBDown.GUI.Tests;

public class UrlDetectorTests
{
    [Theory]
    [InlineData("av123456", "视频（av 号）")]
    [InlineData("ep123456", "番剧（ep 号）")]
    [InlineData("ss2539", "番剧（ss 号）")]
    [InlineData("md123", "番剧（md 号）")]
    [InlineData("opus1234567890", "专栏（opus 号）")]
    [InlineData("cv1234567", "专栏（cv 号）")]
    [InlineData("space123456", "用户空间")]
    [InlineData("123456", "视频（av 号）")]
    public void Describe_KnownId_ReturnsDescription(string input, string expected)
    {
        Assert.Equal(expected, UrlDetector.Describe(input));
    }

    [Fact]
    public void Describe_BvUrl_ReturnsVideoWithBvid( )
    {
        Assert.Equal("视频（BV1xx411c7mD）", UrlDetector.Describe("https://www.bilibili.com/video/BV1xx411c7mD"));
    }

    [Fact]
    public void Describe_WatchLaterUrl_ReturnsWatchLater( )
    {
        Assert.Equal("稍后再看列表", UrlDetector.Describe("https://www.bilibili.com/watchlater"));
    }

    [Fact]
    public void Describe_LiveUrl_ReturnsLive( )
    {
        Assert.Equal("直播地址", UrlDetector.Describe("https://live.bilibili.com/123456"));
    }

    [Fact]
    public void Describe_GenericUrl_ReturnsVideoAddress( )
    {
        Assert.Equal("视频地址", UrlDetector.Describe("https://example.com/video"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("hello world")]
    public void Describe_Unrecognized_ReturnsNull(string input)
    {
        Assert.Null(UrlDetector.Describe(input));
    }
}
