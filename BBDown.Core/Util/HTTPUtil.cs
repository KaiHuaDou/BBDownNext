using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

using static BBDown.Core.Logger;

namespace BBDown.Core.Util;

// CA1054/CA2234: 本类中以 string 接收 url 的公开方法（GetWebSourceAsync / GetWebLocationAsync /
// GetJsonAsync / AddDownloadHeaders / GetWithRangeAsync / GetPostResponseAsync）均被 BBDown 主项目
// 直接调用，改为 System.Uri 会造成跨项目破坏性变更（本次改动范围仅限 BBDown.Core），故保留 string。
public static partial class HTTPUtil
{

    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(2);

    // 可替换：测试经 InternalsVisibleTo 注入带 stub handler 的实例，解锁 8 个 Fetcher 的离线单测
    public static HttpClient AppHttpClient { get; internal set; } = new(new HttpClientHandler
    {
        AllowAutoRedirect = true,
        AutomaticDecompression = DecompressionMethods.All,
        ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) =>
            sslPolicyErrors == System.Net.Security.SslPolicyErrors.None ||
            Environment.GetEnvironmentVariable("BBDOWN_INSECURE_TLS") == "1"
    })
    {
        Timeout = DefaultTimeout
    };

    // 大文件下载需要更长的超时；进程级配置，运行时经此属性调整（不在热路径上改动 HttpClient 本身）
    internal static TimeSpan RequestTimeout
    {
        get => AppHttpClient.Timeout;
        set => AppHttpClient.Timeout = value;
    }

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

    public static string UserAgent { get; private set; } = GetRandomUserAgent( );

    // 仅允许在启动装配阶段设置 UA（构造期一次性设定），避免任意代码点改动全局状态
    public static void SetUserAgent(string ua) => UserAgent = ua;

    // 番剧播放页要带 CURRENT_FNVAL 才会吐出 dash 源。只认 /ep123 /ss123 这样完整的路径段，
    // 裸 Contains("/ep") 会把 /episodes、/ssl 之类一并命中
    internal static bool IsBangumiPlayPage(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
               && uri.Segments.Any(segment => BangumiSegmentRegex( ).IsMatch(segment.TrimEnd('/')));
    }

    [GeneratedRegex(@"^(ep|ss)\d+$")]
    private static partial Regex BangumiSegmentRegex( );

    public static async Task<string> GetWebSourceAsync(string url, AppConfig cfg, string? userAgent = null, CancellationToken ct = default)
    {
        using var webRequest = new HttpRequestMessage(HttpMethod.Get, url);
        webRequest.Headers.TryAddWithoutValidation("User-Agent", userAgent ?? UserAgent);
        webRequest.Headers.TryAddWithoutValidation("Accept-Encoding", "gzip, deflate");
        var cookie = cfg.Cookie;
        if (Buvid.Fragment.Length != 0) cookie += ";" + Buvid.Fragment;
        webRequest.Headers.TryAddWithoutValidation("Cookie", IsBangumiPlayPage(url) ? cookie + ";CURRENT_FNVAL=4048;" : cookie);

        var host = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : "";
        if (host == BiliApi.MainHost)
        {
            webRequest.Headers.TryAddWithoutValidation("Referer", BiliApi.Site + "/");
        }

        if (host == BiliApi.IntlAppHost)
        {
            webRequest.Headers.TryAddWithoutValidation("sec-ch-ua", "\"Google Chrome\";v=\"131\", \"Chromium\";v=\"131\", \"Not_A Brand\";v=\"24\"");
        }

        webRequest.Headers.CacheControl = CacheControlHeaderValue.Parse("no-cache");
        webRequest.Headers.Connection.Clear( );

        LogDebug("获取网页内容: Url: {0}, Headers: {1}", url, webRequest.Headers);
        using var webResponse = await AppHttpClient.SendAsync(webRequest, HttpCompletionOption.ResponseHeadersRead, ct);
        webResponse.EnsureSuccessStatusCode( );

        var htmlCode = await webResponse.Content.ReadAsStringAsync(ct);
        LogDebug("Response: {0}", htmlCode);
        return htmlCode;
    }

    // 重写重定向处理, 自动跟随多次重定向
    public static async Task<string> GetWebLocationAsync(string url, CancellationToken ct = default)
    {
        using var webRequest = new HttpRequestMessage(HttpMethod.Head, url);
        webRequest.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        webRequest.Headers.TryAddWithoutValidation("Accept-Encoding", "gzip, deflate");
        webRequest.Headers.CacheControl = CacheControlHeaderValue.Parse("no-cache");
        webRequest.Headers.Connection.Clear( );

        LogDebug("获取网页重定向地址: Url: {0}, Headers: {1}", url, webRequest.Headers);
        using var webResponse = await AppHttpClient.SendAsync(webRequest, HttpCompletionOption.ResponseHeadersRead, ct);
        webResponse.EnsureSuccessStatusCode( );
        var location = webResponse.RequestMessage!.RequestUri!.AbsoluteUri;
        LogDebug("Location: {0}", location);
        return location;
    }

    // 逃生舱：需要自行控制 Header/Range/平台分支时直接构造 HttpRequestMessage 走这里
    public static Task<HttpResponseMessage> SendRawAsync(HttpRequestMessage request, CancellationToken ct = default)
    {
        LogDebug("发送请求: {0} {1}, Headers: {2}", request.Method, request.RequestUri?.AbsoluteUri ?? "", request.Headers);
        return AppHttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
    }

    // 返回裸 JsonDocument，调用方自己取字段并负责 Dispose
    public static async Task<JsonDocument> GetJsonAsync(string url, AppConfig cfg, CancellationToken ct = default)
    {
        return JsonDocument.Parse(await GetWebSourceAsync(url, cfg, null, ct));
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

    public static async Task<HttpResponseMessage> GetWithRangeAsync(string url, long from, long? to, string cookie, DateTimeOffset? ifRange = null, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        AddDownloadHeaders(request, url, cookie);
        request.Headers.Range = new(from, to);
        request.Headers.IfRange = ifRange != null ? new(ifRange.Value) : null;

        // 失败响应握着连接不放会拖垮重试, 这里先释放再抛
        var response = await SendRawAsync(request, ct);
        if (response.IsSuccessStatusCode) return response;

        var status = response.StatusCode;
        response.Dispose( );
        throw new HttpRequestException($"下载请求失败: HTTP {(int) status} {status}", null, status);
    }

    public static async Task<byte[]> GetPostResponseAsync(string Url, byte[] postData, Dictionary<string, string>? headers = null, CancellationToken ct = default)
    {
        LogDebug("Post to: {0}, data: {1}", Url, Convert.ToBase64String(postData));

        ByteArrayContent content = new(postData);
        content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/grpc");

        using HttpRequestMessage request = new( )
        {
            RequestUri = new Uri(Url),
            Method = HttpMethod.Post,
            Content = content,
        };

        if (headers != null)
        {
            foreach (var header in headers)
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }
        else
        {
            request.Headers.TryAddWithoutValidation("User-Agent", "Dalvik/2.1.0 (Linux; U; Android 6.0.1; oneplus a5010 Build/V417IR) 6.10.0 os/android model/oneplus a5010 mobi_app/android build/6100500 channel/bili innerVer/6100500 osVer/6.0.1 network/2");
            request.Headers.TryAddWithoutValidation("grpc-encoding", "gzip");
        }

        using var response = await AppHttpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"gRPC 请求失败: HTTP {(int) response.StatusCode} {response.ReasonPhrase}", null, response.StatusCode);
        }

        var bytes = await response.Content.ReadAsByteArrayAsync(ct);

        // grpc-status 可能出现在响应头, 也可能出现在读完 body 后的 trailer 中
        var status = ReadGrpcMeta(response, "grpc-status");
        if (status is not (null or "0"))
        {
            throw new HttpRequestException($"gRPC 返回错误 status={status}: {ReadGrpcMeta(response, "grpc-message") ?? "无错误描述"}");
        }

        return bytes;
    }

    private static string? ReadGrpcMeta(HttpResponseMessage response, string name)
    {
        if (response.Headers.TryGetValues(name, out var values)
            || response.TrailingHeaders.TryGetValues(name, out values))
        {
            return values.FirstOrDefault( );
        }

        return null;
    }
}