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
    public void MaxQn_IsHighestQuality( )
    {
        Assert.Equal("127", Config.MaxQn);
    }

    // 智能修复(qn=100)排在原生 1080P(qn=80)之后：默认不抢占原生画质
    [Fact]
    public void QualityRank_Native1080PIsPreferredOverAiRepair( )
    {
        Assert.True(Config.QualityRank("80") < Config.QualityRank("100"));
    }

    [Fact]
    public void QualityRank_HighestQualityHasRankZero( )
    {
        Assert.Equal(0, Config.QualityRank("127"));
    }

    // 未收录档位按 qn 数值算插入位：比已知最高还高 => 0；介于 100 与 80 之间 => 与 100 并列；非数字 => 末尾
    [Theory]
    [InlineData("130", 0)]
    [InlineData("90", 7)]
    [InlineData("abc", 15)]
    public void QualityRank_UnknownQnInsertsByNumericValue(string qn, int expectedRank)
    {
        Assert.Equal(expectedRank, Config.QualityRank(qn));
    }
}
