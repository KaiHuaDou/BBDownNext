using System;

namespace BBDown.Tests;

public class UtilsTests
{
    [Theory]
    [InlineData(0, "0 bytes")]
    [InlineData(1023, "1023 bytes")]
    [InlineData(1024, "1.00 KB")]
    [InlineData(1536, "1.50 KB")]
    [InlineData(1024 * 1024, "1.00 MB")]
    [InlineData(1024L * 1024 * 1024, "1.00 GB")]
    [InlineData(3L * 1024 * 1024 * 1024 + 512L * 1024 * 1024, "3.50 GB")]
    public void FormatFileSize_PicksUnitByMagnitude(double size, string expected)
    {
        Assert.Equal(expected, Utils.FormatFileSize(size));
    }

    [Fact]
    public void FormatFileSize_NegativeThrows( )
    {
        Assert.Throws<ArgumentOutOfRangeException>(( ) => Utils.FormatFileSize(-1));
    }

    [Theory]
    [InlineData(0, "00m00s")]
    [InlineData(59, "00m59s")]
    [InlineData(60, "01m00s")]
    [InlineData(3599, "59m59s")]
    [InlineData(3600, "1h00m00s")]
    [InlineData(86399, "23h59m59s")]
    // 超过 24 小时不进位到「天」，小时数继续累加
    [InlineData(90000, "25h00m00s")]
    public void FormatTime_RelativeForm(int seconds, string expected)
    {
        Assert.Equal(expected, Utils.FormatTime(seconds));
    }

    [Theory]
    [InlineData(0, "00:00:00")]
    [InlineData(3661, "01:01:01")]
    [InlineData(90000, "25:00:00")]
    public void FormatTime_AbsoluteForm(int seconds, string expected)
    {
        Assert.Equal(expected, Utils.FormatTime(seconds, absolute: true));
    }

    [Theory]
    [InlineData("p", "https://www.bilibili.com/video/BV1xx?p=3", "3")]
    [InlineData("p", "https://www.bilibili.com/video/BV1xx?from=search&p=12&spm=1", "12")]
    [InlineData("from", "https://www.bilibili.com/video/BV1xx?from=search&p=12", "search")]
    [InlineData("p", "https://www.bilibili.com/video/BV1xx", "")]
    [InlineData("q", "https://www.bilibili.com/video/BV1xx?p=3", "")]
    public void GetQueryString_ReadsNamedParameter(string name, string url, string expected)
    {
        Assert.Equal(expected, Utils.GetQueryString(name, url));
    }

    [Theory]
    [InlineData("https://cdn.example.com/a/b/video.m4s", "video")]
    [InlineData("video.mp4", "video")]
    [InlineData("/a/b/name.with.dots.ts", "name.with.dots")]
    public void RSubString_TakesFileNameWithoutExtension(string input, string expected)
    {
        Assert.Equal(expected, Account.RSubString(input));
    }

    // 无扩展名时 LastIndexOf('.') 返回 -1，直接越界
    [Fact]
    public void RSubString_NoExtensionThrows( )
    {
        Assert.ThrowsAny<ArgumentException>(( ) => Account.RSubString("https://cdn.example.com/a/b/video"));
    }

    // wbi 签名用的固定置换表，长度 32，索引最大 58
    [Fact]
    public void GetMixinKey_AppliesFixedPermutation( )
    {
        // 用 0-9a-z... 这类可辨识字符构造 64 位原始 key，便于直接核对置换结果
        const string orig = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ01";
        var mixin = Account.GetMixinKey(orig);

        Assert.Equal(32, mixin.Length);
        int[] table =
        [
            46, 47, 18, 2, 53, 8, 23, 32, 15, 50, 10, 31, 58, 3, 45, 35,
            27, 43, 5, 49, 33, 9, 42, 19, 29, 28, 14, 39, 12, 38, 41, 13
        ];
        for (var i = 0; i < table.Length; i++)
        {
            Assert.Equal(orig[table[i]], mixin[i]);
        }
    }

    [Fact]
    public void FormatTimeStamp_ZeroReturnsLiteralNull( )
    {
        // 0 视作「无时间戳」，固定输出字面量 "null" 而非 1970 年
        Assert.Equal("null", Utils.FormatTimeStamp(0, "yyyy-MM-dd HH:mm:ss"));
    }

    [Fact]
    public void FormatTimeStamp_FormatsLocalTimePerFormat( )
    {
        const long ts = 1700000000L;
        var expected = DateTimeOffset.FromUnixTimeSeconds(ts).ToLocalTime( ).ToString("yyyy-MM-dd HH:mm:ss");
        Assert.Equal(expected, Utils.FormatTimeStamp(ts, "yyyy-MM-dd HH:mm:ss"));
    }
}
