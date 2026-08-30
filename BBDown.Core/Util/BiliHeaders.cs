using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Web;

namespace BBDown.Core.Util;

/// <summary>
/// 请求头构造与「这个地址值不值得托付 Cookie」的判定。纯静态，不持有任何客户端实例。
/// 类名不叫 HttpHeaders 是为了避开 System.Net.Http.Headers.HttpHeaders。
/// </summary>
public static partial class BiliHeaders
{
    private static readonly string[] platforms = ["Windows NT 10.0; Win64", "Macintosh; Intel Mac OS X 10_15", "X11; Linux x86_64"];

    private static string RandomVersion(int min, int max)
    {
        var version = Random.Shared.NextDouble( ) * (max - min) + min;
        return version.ToString("F3");
    }

    private static string GetRandomUserAgent( )
    {
        string[] browsers = [$"AppleWebKit/537.36 (KHTML, like Gecko) Chrome/{RandomVersion(80, 110)} Safari/537.36", $"Gecko/20100101 Firefox/{RandomVersion(80, 110)}"];
        return $"Mozilla/5.0 ({platforms[Random.Shared.Next(platforms.Length)]}) {browsers[Random.Shared.Next(browsers.Length)]}";
    }

    // 进程级默认 UA：无配置（登录探测、重定向跟随等）或配置未指定时使用
    public static string UserAgent { get; } = GetRandomUserAgent( );

    // 番剧播放页要带 CURRENT_FNVAL 才会吐出 dash 源。只认 /ep123 /ss123 这样完整的路径段，
    // 裸 Contains("/ep") 会把 /episodes、/ssl 之类一并命中
    internal static bool IsBangumiPlayPage(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
               && uri.Segments.Any(segment => BangumiSegmentRegex( ).IsMatch(segment.TrimEnd('/')));
    }

    [GeneratedRegex(@"^(ep|ss)\d+$")]
    private static partial Regex BangumiSegmentRegex( );

    // 凭据门：携带操作者 Cookie 的请求只允许发往 B 站官方域或用户显式配置的 host（--host / --ep-host / --tv-host）。
    // b23.tv 短链展开后的目标不可信，拦截可防用户可控 URL 把 Cookie 外发给第三方
    private static readonly HashSet<string> TrustedCookieHosts =
    [
        BiliApi.MainHost, BiliApi.PassportHost, BiliApi.TvHost, BiliApi.IntlAppHost,
        BiliApi.IntlWebHost, BiliApi.LiveApiHost,
        "www.bilibili.com", "space.bilibili.com", "bangumi.bilibili.com",
        "comment.bilibili.com", "live.bilibili.com",
        // passport.snm0516.aisee.tv 即 B 站 passport 下发的跨域种 cookie 节点（aisee.tv 为 B 站自有域名），整体放行
        "passport.snm0516.aisee.tv"
    ];

    internal static bool IsTrustedCookieHost(string host, AppConfig cfg)
    {
        // hdslb.com 是 B 站官方 CDN 域（字幕、封面等静态资源均下发此域），其子域全由 B 站掌控；
        // 字幕 URL 来自 B 站自有 API 响应（非用户可控），整体放行避免把 Cookie 错判为不可信主机
        if (host.Equals("hdslb.com", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".hdslb.com", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return TrustedCookieHosts.Contains(host) || host == cfg.Host || host == cfg.EpHost || host == cfg.TvHost;
    }

    // UA 请求级化：显式参数 > AppConfig.UserAgent > 进程级默认。CLI 的 --user-agent 由 WorkSetup.ResolveConfig
    // 落入 AppConfig，serve 契约不含该字段，故不会出现跨任务互相覆盖全局 UA 的踩踏
    internal static void ApplyStandardGetHeaders(HttpRequestMessage request, string url, AppConfig cfg, string? userAgent = null)
    {
        // 在附加任何头之前拒绝，避免把操作者 Cookie 发往不可信主机
        if (Uri.TryCreate(url, UriKind.Absolute, out var gateUri) && !IsTrustedCookieHost(gateUri.Host, cfg))
        {
            throw new InvalidOperationException($"拒绝向不可信主机发送携带 Cookie 的请求：{gateUri.Host}");
        }

        var effectiveUserAgent = userAgent ?? (string.IsNullOrEmpty(cfg.UserAgent) ? UserAgent : cfg.UserAgent);
        request.Headers.TryAddWithoutValidation("User-Agent", effectiveUserAgent);
        request.Headers.TryAddWithoutValidation("Accept-Encoding", "gzip, deflate");
        var cookie = cfg.Cookie;
        if (Buvid.Fragment.Length != 0)
        {
            cookie += ";" + Buvid.Fragment;
        }

        request.Headers.TryAddWithoutValidation("Cookie", IsBangumiPlayPage(url) ? $"{cookie};CURRENT_FNVAL={Config.FnvalPgc};" : cookie);

        var host = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : "";
        // passport 系接口（扫码登录 generate/poll 等）同样校验 Referer，浏览器从 www.bilibili.com 发起，
        // 不带 Referer 会被服务端在拿到 data.Url 之前就挡下，导致 Web 登录拿不到 SESSDATA
        if (host is BiliApi.MainHost or BiliApi.PassportHost or "www.bilibili.com")
        {
            request.Headers.TryAddWithoutValidation("Referer", BiliApi.Site + "/");
        }

        if (host == BiliApi.IntlAppHost)
        {
            request.Headers.TryAddWithoutValidation("sec-ch-ua", "\"Google Chrome\";v=\"131\", \"Chromium\";v=\"131\", \"Not_A Brand\";v=\"24\"");
        }

        request.Headers.CacheControl = CacheControlHeaderValue.Parse("no-cache");
        request.Headers.Connection.Clear( );
    }

    // 移动端下载地址带 Referer 会被拒；platform=android_tv_yst 也以 android 开头，一次判定即可
    public static bool IsAndroidPlatformUrl(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
               && HttpUtility.ParseQueryString(uri.Query)["platform"]?.StartsWith("android", StringComparison.Ordinal) == true;
    }

    public static void AddDownloadHeaders(HttpRequestMessage request, string url, string cookie)
    {
        if (!IsAndroidPlatformUrl(url))
        {
            request.Headers.TryAddWithoutValidation("Referer", BiliApi.Site);
        }

        request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0");
        request.Headers.TryAddWithoutValidation("Cookie", cookie);
    }

    /// <summary>
    /// 直播拉流头。部分 CDN 节点（如 cn-*-ct-* 系列）强制校验 Referer，缺失直接 403；
    /// 另一些节点则不校验，故不能靠「能拉通」推断可以省略。<see cref="AddDownloadHeaders"/>
    /// 带的是 www 站点的 Referer，对直播 CDN 不适用。
    /// </summary>
    public static void AddLiveStreamHeaders(HttpRequestMessage request, string cookie)
    {
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        request.Headers.TryAddWithoutValidation("Referer", BiliApi.LiveSite + "/");
        request.Headers.TryAddWithoutValidation("Origin", BiliApi.LiveSite);
        // StreamHttpClient 关闭了自动解压，若服务端仍按默认协商返回 gzip 就会写出无法播放的文件
        request.Headers.TryAddWithoutValidation("Accept-Encoding", "identity");
        if (!string.IsNullOrEmpty(cookie))
        {
            request.Headers.TryAddWithoutValidation("Cookie", cookie);
        }
    }
}
