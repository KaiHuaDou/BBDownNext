using Xunit;

namespace BBDown.Core.Tests;

public class ConfigTests
{
    [Theory]
    [InlineData("127", "8K 超高清")]
    [InlineData("120", "4K 超清")]
    [InlineData("80", "1080P 高清")]
    [InlineData("16", "360P 流畅")]
    [InlineData("5", "144P 流畅")]
    public void GetQualityName_MapsKnownQn(string qn, string expected)
    {
        Assert.Equal(expected, Config.GetQualityName(qn));
    }

    // B 站新增 qn 时旧版本不应崩，只降级为提示原始值
    [Theory]
    [InlineData("999")]
    [InlineData("")]
    [InlineData("abc")]
    public void GetQualityName_UnknownQnDoesNotThrow(string qn)
    {
        Assert.Equal($"未知清晰度(qn={qn})", Config.GetQualityName(qn));
    }

    [Fact]
    public void MaxQn_IsHighestQuality()
    {
        Assert.Equal("127", Config.MaxQn);
    }
}
