namespace BBDown.Tests;

/// <summary>
/// Host 白名单的纯函数测试。真实 rebinding 场景依赖 DNS 与浏览器，起服务器也属耗时操作，一律不测。
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
}
