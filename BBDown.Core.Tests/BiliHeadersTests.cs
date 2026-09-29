using System;
using System.Linq;
using System.Net.Http;

namespace BBDown.Core.Tests;

/// <summary>
/// 请求头构造与 Cookie 可信主机判定的纯函数测试。
/// </summary>
public class BiliHeadersTests
{
    [Theory]
    [InlineData("https://www.bilibili.com/bangumi/play/ep123456", true)]
    [InlineData("https://www.bilibili.com/bangumi/play/ss12345", true)]
    [InlineData("https://www.bilibili.com/cheese/play/ep123456", true)]
    [InlineData("https://www.bilibili.com/bangumi/play/ep123456/", true)]
    public void IsBangumiPlayPage_MatchesEpAndSsSegments(string url, bool expected)
    {
        Assert.Equal(expected, BiliHeaders.IsBangumiPlayPage(url));
    }

    // 旧实现是裸 Contains("/ep") || Contains("/ss")，这些都会被误判
    [Theory]
    [InlineData("https://api.bilibili.com/x/player/pagelist?bvid=BV1x")]
    [InlineData("https://api.bilibili.com/pgc/view/web/season?ep_id=123456")]
    [InlineData("https://api.bilibili.com/x/web-interface/view/episodes")]
    [InlineData("https://cdn.example.com/ssl/video.m4s")]
    [InlineData("https://cdn.example.com/upgcxcode/ep/xx.m4s")]
    [InlineData("not a url")]
    public void IsBangumiPlayPage_IgnoresIncidentalMatches(string url)
    {
        Assert.False(BiliHeaders.IsBangumiPlayPage(url));
    }

    [Theory]
    [InlineData("https://cn-cdn.bilivideo.com/v.m4s?platform=android", true)]
    [InlineData("https://cn-cdn.bilivideo.com/v.m4s?platform=android_tv_yst", true)]
    [InlineData("https://cn-cdn.bilivideo.com/v.m4s?deadline=1&platform=android&os=upos", true)]
    [InlineData("https://cn-cdn.bilivideo.com/v.m4s?platform=pc", false)]
    [InlineData("https://cn-cdn.bilivideo.com/v.m4s", false)]
    // 参数值里出现 platform=android 字样不算，必须是 platform 参数本身
    [InlineData("https://cn-cdn.bilivideo.com/v.m4s?trace=platform%3Dandroid", false)]
    public void IsAndroidPlatformUrl_ReadsPlatformQueryParam(string url, bool expected)
    {
        Assert.Equal(expected, BiliHeaders.IsAndroidPlatformUrl(url));
    }

    // 番剧播放页 Cookie 必须带 CURRENT_FNVAL=12240，网页源码兜底的 __playinfo__ 才吐出智能修复源
    [Fact]
    public void ApplyStandardGetHeaders_BangumiPlayPageCarriesFnvalPgc( )
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://www.bilibili.com/bangumi/play/ep249469");
        BiliHeaders.ApplyStandardGetHeaders(request, "https://www.bilibili.com/bangumi/play/ep249469", AppConfig.Empty);
        var cookie = request.Headers.GetValues("Cookie").Single( );
        Assert.Contains("CURRENT_FNVAL=12240", cookie);
    }

    [Fact]
    public void ApplyStandardGetHeaders_NonBangumiPageOmitsFnval( )
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://www.bilibili.com/video/BV1GJ411x7h7");
        BiliHeaders.ApplyStandardGetHeaders(request, "https://www.bilibili.com/video/BV1GJ411x7h7", AppConfig.Empty);
        var cookie = request.Headers.GetValues("Cookie").Single( );
        Assert.DoesNotContain("CURRENT_FNVAL", cookie);
    }

    [Theory]
    [InlineData("api.bilibili.com", true)]
    [InlineData("passport.bilibili.com", true)]
    [InlineData("api.snm0516.aisee.tv", true)]
    [InlineData("api.bilibili.tv", true)]
    [InlineData("api.biliintl.com", true)]
    [InlineData("api.live.bilibili.com", true)]
    [InlineData("www.bilibili.com", true)]
    [InlineData("space.bilibili.com", true)]
    [InlineData("bangumi.bilibili.com", true)]
    [InlineData("comment.bilibili.com", true)]
    [InlineData("live.bilibili.com", true)]
    [InlineData("passport.snm0516.aisee.tv", true)]
    // 字幕 CDN 域：AI 字幕走 aisubtitle.hdslb.com，常规字幕走 i0.hdslb.com，整体放行
    [InlineData("hdslb.com", true)]
    [InlineData("aisubtitle.hdslb.com", true)]
    [InlineData("i0.hdslb.com", true)]
    [InlineData("evil.example.com", false)]
    [InlineData("api.bilibili.com.evil.com", false)]
    // 必须以点分隔，evilhdslb.com 不是 B 站域名，不得放行
    [InlineData("evilhdslb.com", false)]
    public void IsTrustedCookieHost_AcceptsOfficialDomainsOnly(string host, bool expected)
    {
        Assert.Equal(expected, BiliHeaders.IsTrustedCookieHost(host, AppConfig.Empty));
    }

    // 用户自定义 host（--host / --ep-host / --tv-host 指向镜像站）属用户显式授权，必须放行
    [Fact]
    public void IsTrustedCookieHost_AcceptsConfiguredCustomHost( )
    {
        var cfg = AppConfig.Empty with { EpHost = "mirror.example.com" };
        Assert.True(BiliHeaders.IsTrustedCookieHost("mirror.example.com", cfg));
    }

    // 凭据门必须在附加任何头之前生效，拒绝携带操作者 Cookie 的请求发往不可信主机
    [Fact]
    public void ApplyStandardGetHeaders_RejectsUntrustedHost( )
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://evil.example.com/page");
        Assert.Throws<InvalidOperationException>(( ) => BiliHeaders.ApplyStandardGetHeaders(request, "https://evil.example.com/page", AppConfig.Empty));
    }

    // Accept-Encoding 不在头构造层出现：AppHttpClient 关闭了自动重定向但保留自动解压，
    // 协商头由 handler 按启用算法自动添加，此处手动指定会抑制 handler 并悄悄关闭 br 协商
    [Fact]
    public void ApplyStandardGetHeaders_DoesNotSetAcceptEncoding( )
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.bilibili.com/x/view");
        BiliHeaders.ApplyStandardGetHeaders(request, "https://api.bilibili.com/x/view", AppConfig.Empty);

        Assert.Empty(request.Headers.AcceptEncoding);
    }

    // 直播 CDN 部分节点无视协商强推 gzip，identity 显式拒绝压缩；
    // 压缩字节会过不了 LiveSegmentWriter 的 FLV 签名校验，录制直接失败
    [Fact]
    public void AddLiveStreamHeaders_ForcesIdentityEncoding( )
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://cn-hbxy-cmcc-01-16.bilivideo.com/live.flv");
        BiliHeaders.AddLiveStreamHeaders(request, "SESSDATA=abc");

        Assert.Equal(["identity"], request.Headers.AcceptEncoding.Select(v => v.Value));
        Assert.Equal(["SESSDATA=abc"], request.Headers.GetValues("Cookie"));
        Assert.Equal($"{BiliApi.LiveSite}/", request.Headers.GetValues("Referer").Single( ));
        Assert.Equal(BiliApi.LiveSite, request.Headers.GetValues("Origin").Single( ));
    }

    // 移动端下载地址带 Referer 会被 CDN 拒绝，平台判定必须与头行为联动
    [Theory]
    [InlineData("https://upos.example.com/v.m4s?platform=android", false)]
    [InlineData("https://upos.example.com/v.m4s?platform=android_tv_yst", false)]
    [InlineData("https://upos.example.com/v.m4s", true)]
    public void AddDownloadHeaders_OmitsRefererOnlyForAndroidPlatform(string url, bool expectReferer)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        BiliHeaders.AddDownloadHeaders(request, url, "SESSDATA=abc");

        if (expectReferer)
        {
            // Referer 是受限头，.NET 解析为 Uri 后再序列化，裸域名必然被规范化成带尾斜杠的形式
            Assert.Equal($"{BiliApi.Site}/", request.Headers.GetValues("Referer").Single( ));
        }
        else
        {
            Assert.False(request.Headers.Contains("Referer"));
        }

        Assert.Equal(["Mozilla/5.0"], request.Headers.GetValues("User-Agent"));
        Assert.Equal(["SESSDATA=abc"], request.Headers.GetValues("Cookie"));
    }
}
