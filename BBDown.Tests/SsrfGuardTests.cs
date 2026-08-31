using System.Net;

namespace BBDown.Tests;

/// <summary>
/// Host 白名单与私网段判定的纯函数测试。真实 rebinding 场景依赖 DNS 与浏览器，起服务器也属耗时操作，一律不测。
/// </summary>
public class SsrfGuardTests
{
    [Theory]
    [InlineData("localhost")]
    [InlineData("LOCALHOST")]
    [InlineData("127.0.0.1")]
    [InlineData("127.1.2.3")]
    [InlineData("::1")]
    public void IsLoopbackHost_AcceptsLiteralLoopback(string host)
    {
        Assert.True(SsrfGuard.IsLoopbackHost(host));
    }

    [Theory]
    // rebinding 后攻击者域名解析结果就是回环地址，但 Host 头仍是攻击者域名
    [InlineData("evil.example.com")]
    [InlineData("127.0.0.1.evil.example.com")]
    [InlineData("192.168.1.1")]
    [InlineData("")]
    public void IsLoopbackHost_RejectsAnythingElse(string host)
    {
        Assert.False(SsrfGuard.IsLoopbackHost(host));
    }

    // 不做 DNS 解析：解析结果正是攻击者能操纵的东西
    [Fact]
    public void IsLoopbackHost_DoesNotResolveNames( )
    {
        Assert.False(SsrfGuard.IsLoopbackHost("localhost.evil.example.com"));
    }

    [Theory]
    [InlineData("10.0.0.1")]
    [InlineData("10.255.255.255")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.1.1")]
    [InlineData("169.254.169.254")]
    [InlineData("127.0.0.1")]
    [InlineData("0.0.0.0")]
    [InlineData("100.64.0.1")]
    [InlineData("192.0.0.8")]
    [InlineData("198.18.0.1")]
    [InlineData("198.19.255.255")]
    [InlineData("224.0.0.1")]
    [InlineData("239.255.255.255")]
    [InlineData("240.0.0.1")]
    [InlineData("255.255.255.255")]
    [InlineData("::")]
    [InlineData("::1")]
    [InlineData("fe80::1")]
    [InlineData("fc00::1")]
    [InlineData("fd12::1")]
    [InlineData("ff02::1")]
    // IPv4-mapped IPv6 须按其 IPv4 等价地址判定（云元数据 ::ffff:169.254.169.254）
    [InlineData("::ffff:169.254.169.254")]
    [InlineData("::ffff:10.0.0.1")]
    public void IsPrivateAddress_RejectsPrivateAndSpecialRanges(string address)
    {
        Assert.True(SsrfGuard.IsPrivateAddress(IPAddress.Parse(address)));
    }

    [Theory]
    [InlineData("1.1.1.1")]
    [InlineData("8.8.8.8")]
    [InlineData("172.32.0.1")]
    [InlineData("100.0.0.1")]
    [InlineData("198.20.0.1")]
    [InlineData("223.255.255.255")]
    [InlineData("2001:db8::1")]
    [InlineData("2606:4700::1111")]
    public void IsPrivateAddress_AllowsPublicAddresses(string address)
    {
        Assert.False(SsrfGuard.IsPrivateAddress(IPAddress.Parse(address)));
    }
}
