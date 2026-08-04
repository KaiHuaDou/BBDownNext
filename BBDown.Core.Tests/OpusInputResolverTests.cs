using BBDown.Core.Opus;

namespace BBDown.Core.Tests;

public class OpusInputResolverTests
{
    [Theory]
    [InlineData("https://www.bilibili.com/opus/1230485246732926996", "1230485246732926996")]
    [InlineData("http://www.bilibili.com/opus/1230485246732926996", "1230485246732926996")]
    [InlineData("//www.bilibili.com/opus/1230485246732926996", "1230485246732926996")]
    [InlineData("https://m.bilibili.com/opus/1230485246732926996", "1230485246732926996")]
    [InlineData("https://www.bilibili.com/opus/1230485246732926996?spm_id_from=333.1387", "1230485246732926996")]
    [InlineData("https://www.bilibili.com/opus/1230485246732926996#comment", "1230485246732926996")]
    [InlineData("  https://www.bilibili.com/opus/1230485246732926996  ", "1230485246732926996")]
    public void TryParse_OpusUrl_ReturnsOpusId(string input, string expected)
    {
        Assert.True(OpusInputResolver.TryParse(input, out var target));
        Assert.Equal(expected, target.OpusId);
        Assert.False(target.HasCv);
    }

    [Theory]
    [InlineData("https://www.bilibili.com/read/cv51908655", "51908655")]
    [InlineData("https://www.bilibili.com/read/mobile/51908655", "51908655")]
    [InlineData("https://www.bilibili.com/read/cv51908655/?from=search", "51908655")]
    public void TryParse_ReadUrl_ReturnsCvId(string input, string expected)
    {
        Assert.True(OpusInputResolver.TryParse(input, out var target));
        Assert.Equal(expected, target.CvId);
    }

    [Theory]
    [InlineData("cv51908655", "51908655")]
    [InlineData("CV51908655", "51908655")]
    public void TryParse_CvPrefix_ReturnsCvId(string input, string expected)
    {
        Assert.True(OpusInputResolver.TryParse(input, out var target));
        Assert.Equal(expected, target.CvId);
        Assert.False(target.HasOpus);
    }

    [Theory]
    [InlineData("opus1230485246732926996")]
    [InlineData("opus:1230485246732926996")]
    [InlineData("OPUS1230485246732926996")]
    public void TryParse_OpusPrefix_ReturnsOpusId(string input)
    {
        Assert.True(OpusInputResolver.TryParse(input, out var target));
        Assert.Equal("1230485246732926996", target.OpusId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("av123456")]
    [InlineData("BV1L411t7SU")]
    [InlineData("ep123456")]
    [InlineData("ss12345")]
    [InlineData("md12345")]
    [InlineData("https://www.bilibili.com/video/BV1L411t7SU")]
    [InlineData("https://www.bilibili.com/bangumi/play/ep123456")]
    [InlineData("https://b23.tv/abcdefg")]
    [InlineData("opusabc")]
    [InlineData("cvabc")]
    public void TryParse_NonOpusInput_ReturnsFalse(string input)
    {
        Assert.False(OpusInputResolver.TryParse(input, out _));
    }

    // 根命令下的裸数字必须留给视频链路（av 号简写），只有 opus 子命令才放行
    [Fact]
    public void TryParse_BareDigits_RejectedUnlessAllowed( )
    {
        Assert.False(OpusInputResolver.TryParse("51908655", out _));
        Assert.False(OpusInputResolver.TryParse("1230485246732926996", out _));
    }

    [Theory]
    [InlineData("1230485246732926996", "1230485246732926996", "")]
    [InlineData("51908655", "", "51908655")]
    public void TryParse_BareDigits_SplitByLength(string input, string expectedOpus, string expectedCv)
    {
        Assert.True(OpusInputResolver.TryParse(input, out var target, allowBareId: true));
        Assert.Equal(expectedOpus, target.OpusId);
        Assert.Equal(expectedCv, target.CvId);
    }
}
