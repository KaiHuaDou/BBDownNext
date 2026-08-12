using System;
using System.Collections.Specialized;

namespace BBDown.Core.Tests;

public class LoginSignTests
{
    [Theory]
    [InlineData("abc", "secret", "33e7cb694fb6fb2f848af6774d9ff138")]
    [InlineData("a=1", "sec", "66af090a3f7b90241b548ea0371db311")]
    [InlineData("", "secret", "5ebe2294ecd0e0f08eab7690d2a6ee69")]
    [InlineData("x", "59b43e04ad6965f34319062b478f83dd", "b87ec59fee04ef877bbee79f1b0e55ff")]
    [InlineData("foo=1&wts=1", "59b43e04ad6965f34319062b478f83dd", "5638bbf60cf4ecf5a72ebcfe2eb57b84")]
    public void GetSign_WithExplicitSecret(string parms, string secret, string expected)
    {
        // parms + secret 拼接后做 MD5，输出小写十六进制
        Assert.Equal(expected, Login.GetSign(parms, secret));
    }

    [Fact]
    public void GetTimeStamp_SecondsVsMilliseconds( )
    {
        var secs = long.Parse(Login.GetTimeStamp(true));
        var ms = long.Parse(Login.GetTimeStamp(false));

        // 秒级：与当前时间差在 5 秒内
        Assert.InRange(secs, DateTimeOffset.Now.ToUnixTimeSeconds( ) - 5, DateTimeOffset.Now.ToUnixTimeSeconds( ) + 5);
        // 毫秒级：与当前时间差在 5 秒内
        Assert.InRange(ms, DateTimeOffset.Now.ToUnixTimeMilliseconds( ) - 5000, DateTimeOffset.Now.ToUnixTimeMilliseconds( ) + 5000);
        // 毫秒级至少是秒级的 1000 倍
        Assert.True(ms >= secs * 1000);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(20)]
    [InlineData(64)]
    public void GetRandomString_HasRequestedLengthAndAllowedCharset(int length)
    {
        // 去掉了易混字符 I/O/1/l/0，避免人工抄写 token 时出错
        const string Allowed = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz_0123456789";
        var s = Login.GetRandomString(length);
        Assert.Equal(length, s.Length);
        Assert.All(s, c => Assert.Contains(c, Allowed));
    }

    [Fact]
    public void ToQueryString_PreservesInsertionOrderAndJoins( )
    {
        // HttpUtility.ParseQueryString 不排序，原样按插入顺序拼回 key=value&...
        var nvc = new NameValueCollection { ["b"] = "2", ["a"] = "1" };
        Assert.Equal("b=2&a=1", Login.ToQueryString(nvc));
    }

    [Fact]
    public void ToQueryString_EmptyCollection_ReturnsEmpty( )
    {
        Assert.Equal("", Login.ToQueryString([]));
    }

    [Fact]
    public void ToDictionary_FromNameValueCollection( )
    {
        var nvc = new NameValueCollection { ["appkey"] = "4409e2ce8ffd12b8", ["ts"] = "123" };
        var dict = nvc.ToDictionary( );
        Assert.Equal(2, dict.Count);
        Assert.Equal("4409e2ce8ffd12b8", dict["appkey"]);
        Assert.Equal("123", dict["ts"]);
    }
}
