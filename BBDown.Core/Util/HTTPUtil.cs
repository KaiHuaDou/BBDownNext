using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using static BBDown.Core.Logger;

namespace BBDown.Core.Util;

// CA1054/CA2234: 本类中以 string 接收 url 的公开方法（GetWebSourceAsync / GetWebLocationAsync /
// GetJsonAsync / AddDownloadHeaders / GetWithRangeAsync / GetPostResponseAsync）均被 BBDown 主项目
// 直接调用，改为 System.Uri 会造成跨项目破坏性变更（本次改动范围仅限 BBDown.Core），故保留 string。
public static partial class HTTPUtil
{

    public static readonly HttpClient AppHttpClient = new(new HttpClientHandler
    {
        AllowAutoRedirect = true,
        AutomaticDecompression = DecompressionMethods.All,
        ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) =>
            sslPolicyErrors == System.Net.Security.SslPolicyErrors.None ||
            Environment.GetEnvironmentVariable("BBDOWN_INSECURE_TLS") == "1"
    })
    {
        Timeout = TimeSpan.FromMinutes(2)
    };

    private static readonly Random random = new( );
    private static readonly string[] platforms = ["Windows NT 10.0; Win64", "Macintosh; Intel Mac OS X 10_15", "X11; Linux x86_64"];

    private static string RandomVersion(int min, int max)
    {
        var version = random.NextDouble( ) * (max - min) + min;
        return version.ToString("F3");
    }

    private static string GetRandomUserAgent( )
    {
        string[] browsers = [$"AppleWebKit/537.36 (KHTML, like Gecko) Chrome/{RandomVersion(80, 110)} Safari/537.36", $"Gecko/20100101 Firefox/{RandomVersion(80, 110)}"];
        return $"Mozilla/5.0 ({platforms[random.Next(platforms.Length)]}) {browsers[random.Next(browsers.Length)]}";
    }

    public static string UserAgent { get; set; } = GetRandomUserAgent( );

    // 番剧播放页要带 CURRENT_FNVAL 才会吐出 dash 源。只认 /ep123 /ss123 这样完整的路径段，
    // 裸 Contains("/ep") 会把 /episodes、/ssl 之类一并命中
    internal static bool IsBangumiPlayPage(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
               && uri.Segments.Any(segment => BangumiSegmentRegex( ).IsMatch(segment.TrimEnd('/')));
    }

    [GeneratedRegex(@"^(ep|ss)\d+$")]
    private static partial Regex BangumiSegmentRegex( );

    public static async Task<string> GetWebSourceAsync(string url, AppConfig cfg, string? userAgent = null)
    {
        using var webRequest = new HttpRequestMessage(HttpMethod.Get, url);
        webRequest.Headers.TryAddWithoutValidation("User-Agent", userAgent ?? UserAgent);
        webRequest.Headers.TryAddWithoutValidation("Accept-Encoding", "gzip, deflate");
        webRequest.Headers.TryAddWithoutValidation("Cookie", IsBangumiPlayPage(url) ? cfg.Cookie + ";CURRENT_FNVAL=4048;" : cfg.Cookie);
        if (url.Contains("api.bilibili.com"))
        {
            webRequest.Headers.TryAddWithoutValidation("Referer", "https://www.bilibili.com/");
        }

        if (url.Contains("api.bilibili.tv"))
        {
            webRequest.Headers.TryAddWithoutValidation("sec-ch-ua", "\"Google Chrome\";v=\"131\", \"Chromium\";v=\"131\", \"Not_A Brand\";v=\"24\"");
        }

        webRequest.Headers.CacheControl = CacheControlHeaderValue.Parse("no-cache");
        webRequest.Headers.Connection.Clear( );

        LogDebug("获取网页内容: Url: {0}, Headers: {1}", url, webRequest.Headers);
        var webResponse = (await AppHttpClient.SendAsync(webRequest, HttpCompletionOption.ResponseHeadersRead)).EnsureSuccessStatusCode( );

        var htmlCode = await webResponse.Content.ReadAsStringAsync( );
        LogDebug("Response: {0}", htmlCode);
        return htmlCode;
    }

    // 重写重定向处理, 自动跟随多次重定向
    public static async Task<string> GetWebLocationAsync(string url)
    {
        using var webRequest = new HttpRequestMessage(HttpMethod.Head, url);
        webRequest.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        webRequest.Headers.TryAddWithoutValidation("Accept-Encoding", "gzip, deflate");
        webRequest.Headers.CacheControl = CacheControlHeaderValue.Parse("no-cache");
        webRequest.Headers.Connection.Clear( );

        LogDebug("获取网页重定向地址: Url: {0}, Headers: {1}", url, webRequest.Headers);
        var webResponse = (await AppHttpClient.SendAsync(webRequest, HttpCompletionOption.ResponseHeadersRead)).EnsureSuccessStatusCode( );
        var location = webResponse.RequestMessage!.RequestUri!.AbsoluteUri;
        LogDebug("Location: {0}", location);
        return location;
    }

    // 逃生舱：需要自行控制 Header/Range/平台分支时直接构造 HttpRequestMessage 走这里
    public static Task<HttpResponseMessage> SendRawAsync(HttpRequestMessage request)
    {
        LogDebug("发送请求: {0} {1}, Headers: {2}", request.Method, request.RequestUri?.AbsoluteUri ?? "", request.Headers);
        return AppHttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
    }

    // 返回裸 JsonDocument，调用方自己取字段并负责 Dispose
    public static async Task<JsonDocument> GetJsonAsync(string url, AppConfig cfg)
    {
        return JsonDocument.Parse(await GetWebSourceAsync(url, cfg));
    }

    public static void AddDownloadHeaders(HttpRequestMessage request, string url, string cookie)
    {
        if (!url.Contains("platform=android_tv_yst") && !url.Contains("platform=android"))
        {
            request.Headers.TryAddWithoutValidation("Referer", "https://www.bilibili.com");
        }

        request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0");
        request.Headers.TryAddWithoutValidation("Cookie", cookie);
    }

    public static async Task<HttpResponseMessage> GetWithRangeAsync(string url, long from, long? to, string cookie, DateTimeOffset? ifRange = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        AddDownloadHeaders(request, url, cookie);
        request.Headers.Range = new(from, to);
        request.Headers.IfRange = ifRange != null ? new(ifRange.Value) : null;
        return (await SendRawAsync(request)).EnsureSuccessStatusCode( );
    }

    public static async Task<byte[]> GetPostResponseAsync(string Url, byte[] postData, Dictionary<string, string>? headers = null)
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

        var response = await AppHttpClient.SendAsync(request);
        var bytes = await response.Content.ReadAsByteArrayAsync( );

        return bytes;
    }
}