using System;
using System.Linq;

using BBDown.Core.Util;

namespace BBDown.Core.Tests;

public class SignUtilTests
{
    // P0-10: WBI 签名是 Web 端鉴权命门（MD5 + mixinKey），错一位全线 -403。
    // 下列向量取自 bilibili-API-collect/docs/misc/sign/wbi.md 的官方 Rust/Haskell 参考实现，
    // 用同一个 mixinKey 复算，确保本实现与服务端算法逐字节一致。
    private static readonly AppConfig WbiTestConfig =
        new("", "", BiliApi.MainHost, BiliApi.MainHost, BiliApi.TvHost, "", "ea1db124af3c7062474693fa704f4ff8");

    [Fact]
    public void WbiSign_MatchesOfficialAsciiVector( )
    {
        // 官方 Rust 用例：foo/bar/zab + wts=1702204169，mixinKey 如上
        const string Api = "foo=114&bar=514&zab=1919810&wts=1702204169";
        var signed = SignUtil.WbiSign(Api, WbiTestConfig);
        Assert.Equal("foo=114&bar=514&zab=1919810&wts=1702204169&w_rid=8f6f2b5b3d485fe1886cec6a0be8c5d4", signed);
    }

    [Fact]
    public void WbiSign_MatchesOfficialUnicodeVector( )
    {
        // 官方 Haskell 用例：值含中文与空格，验证编码大写十六进制 + 空格 -> %20 + 过滤 !'()*
        const string Api = "foo=114&bar=514&hello=世 界&wts=1744823207";
        var signed = SignUtil.WbiSign(Api, WbiTestConfig);
        Assert.Equal("foo=114&bar=514&hello=世 界&wts=1744823207&w_rid=93acf59d85f74453e40cea00056c3daf", signed);
    }

    [Fact]
    public void WbiSign_KeysSortedBeforeHashing( )
    {
        // 输出前缀保留原始顺序，但 w_rid 必须由排序后的 canonical 得出，故后缀一致
        var a = SignUtil.WbiSign("z=1&a=2&m=3&wts=1702204169", WbiTestConfig);
        var b = SignUtil.WbiSign("a=2&m=3&z=1&wts=1702204169", WbiTestConfig);
        Assert.EndsWith("&w_rid=08e72ac25c0e3d2c788f2230393e0668", a);
        Assert.EndsWith("&w_rid=08e72ac25c0e3d2c788f2230393e0668", b);
    }

    [Fact]
    public void WbiSign_EmptyWbi_ReturnsApiUnchanged( )
    {
        const string Api = "aid=1&cid=2&wts=1702204169";
        Assert.Same(Api, SignUtil.WbiSign(Api, AppConfig.Empty));
    }

    [Fact]
    public void WbiSign_StripsExistingWridFromInput( )
    {
        // 重新签名时不该把旧的 w_rid 也算进 canonical（否则校验失败）
        const string Api = "foo=114&bar=514&wts=1702204169&w_rid=deadbeef";
        var signed = SignUtil.WbiSign(Api, WbiTestConfig);
        Assert.Equal("foo=114&bar=514&wts=1702204169&w_rid=ed791ce4979dfe1e2aad3b03b73b13cc", signed);
    }

    [Fact]
    public void WbiSignNow_AppendsTimestampAndSignsWithIt( )
    {
        var signed = SignUtil.WbiSignNow("aid=1&cid=2", WbiTestConfig);

        var wts = signed.Split('&').Single(p => p.StartsWith("wts=", StringComparison.Ordinal))["wts=".Length..];
        Assert.InRange(long.Parse(wts), DateTimeOffset.Now.ToUnixTimeSeconds( ) - 60, DateTimeOffset.Now.ToUnixTimeSeconds( ));
        Assert.Equal(SignUtil.WbiSign($"aid=1&cid=2&wts={wts}", WbiTestConfig), signed);
    }

    [Fact]
    public void AppSign_HashesQueryConcatenatedWithSecret( )
    {
        Assert.Equal("8d9f51949e440aa629fd1a035708473a", SignUtil.AppSign("a=1&b=2", "secret"));
        Assert.Equal("ed04c91cf6f6ab5a01a31c0295c5da34", SignUtil.AppSign("a=1&b=2", ""));
    }

    [Fact]
    public void UnixTimestamp_SecondsVsMilliseconds( )
    {
        var seconds = long.Parse(SignUtil.UnixTimestamp( ));
        var milliseconds = long.Parse(SignUtil.UnixTimestamp(false));
        Assert.InRange(milliseconds / 1000, seconds, seconds + 1);
    }
}
