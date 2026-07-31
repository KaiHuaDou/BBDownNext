using System;

using BBDown.Core.Util;

namespace BBDown.Core.Tests;

public class BilibiliBvConverterTests
{
    [Theory]
    [InlineData(626497566L, "BV1qt4y1X7TW")] // 本仓库冒烟视频，线上已验证的映射
    [InlineData(170001L, "BV17x411w7KC")]
    public void Encode_KnownPairs(long avid, string bvid)
    {
        Assert.Equal(bvid, BilibiliBvConverter.Encode(avid));
    }

    [Theory]
    [InlineData("qt4y1X7TW", 626497566L)]
    [InlineData("7x411w7KC", 170001L)]
    public void Decode_KnownPairs(string bvidWithoutPrefix, long avid)
    {
        // Decode 的入参不含 "BV1" 前缀（调用方传 input[3..]）
        Assert.Equal(avid, BilibiliBvConverter.Decode(bvidWithoutPrefix));
    }

    [Theory]
    [InlineData(1L)]
    [InlineData(170001L)]
    [InlineData(626497566L)]
    [InlineData(112233445566L)]
    public void RoundTrip(long avid)
    {
        Assert.Equal(avid, BilibiliBvConverter.Decode(BilibiliBvConverter.Encode(avid)[3..]));
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-5L)]
    public void Encode_InvalidAvid_Throws(long avid)
    {
        Assert.Throws<InvalidOperationException>(( ) => BilibiliBvConverter.Encode(avid));
    }

    [Fact]
    public void Decode_WrongLength_Throws( )
    {
        Assert.Throws<InvalidOperationException>(( ) => BilibiliBvConverter.Decode("short"));
    }
}
