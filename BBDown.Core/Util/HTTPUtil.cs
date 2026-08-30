using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using static BBDown.Core.Logger;

namespace BBDown.Core.Util;

/// <summary>
/// 客户端实例与请求入口。头构造在 <see cref="BiliHeaders"/>，响应体读取与重定向跟随着
/// <see cref="HttpTransfer"/>，本类只负责把三者接起来。
/// </summary>
public static class HTTPUtil
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(2);

    // 可替换：测试经 InternalsVisibleTo 注入带 stub handler 的实例，解锁 8 个 Fetcher 的离线单测
    public static HttpClient AppHttpClient { get; internal set; } = new(new HttpClientHandler
    {
        AllowAutoRedirect = true,
        AutomaticDecompression = DecompressionMethods.All,
        ServerCertificateCustomValidationCallback = (_, __, ___, sslPolicyErrors) =>
            sslPolicyErrors == System.Net.Security.SslPolicyErrors.None ||
            Environment.GetEnvironmentVariable("BBDOWN_INSECURE_TLS") == "1"
    })
    {
        Timeout = DefaultTimeout,
        // 优先协商 HTTP/2，服务端不支持时自动降级到 HTTP/1.1；减少握手与队头阻塞
        DefaultRequestVersion = HttpVersion.Version20,
        DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower
    };

    /// <summary>
    /// 长连接专用客户端。<see cref="HttpClient.Timeout"/> 覆盖「响应体读取全程」而不只是首字节，
    /// 用 <see cref="AppHttpClient"/>（2 分钟）拉直播流会每 2 分钟被硬掐一次，故必须无限超时；
    /// 断流靠调用方的静默检测判定，不靠超时。关掉自动解压避免把视频流当压缩内容处理。
    /// </summary>
    public static HttpClient StreamHttpClient { get; internal set; } = new(new HttpClientHandler
    {
        AllowAutoRedirect = true,
        AutomaticDecompression = DecompressionMethods.None,
        ServerCertificateCustomValidationCallback = (_, _, _, sslPolicyErrors) =>
            sslPolicyErrors == System.Net.Security.SslPolicyErrors.None ||
            Environment.GetEnvironmentVariable("BBDOWN_INSECURE_TLS") == "1"
    })
    {
        Timeout = Timeout.InfiniteTimeSpan,
        // 直播流是单条超长响应，HTTP/2 的流控窗口在这种场景下只会添乱
        DefaultRequestVersion = HttpVersion.Version11,
        DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower
    };

    static HTTPUtil( )
    {
        if (Environment.GetEnvironmentVariable("BBDOWN_INSECURE_TLS") == "1")
        {
            Logger.LogWarn("已关闭 TLS 证书校验");
        }
    }

    public static async Task<string> GetWebSourceAsync(string url, AppConfig cfg, string? userAgent = null, CancellationToken ct = default)
    {
        using var webResponse = await HttpTransfer.SendTrustGatedAsync(url, HttpMethod.Get,
            (target, method) =>
            {
                var request = new HttpRequestMessage(method, target);
                BiliHeaders.ApplyStandardGetHeaders(request, target, cfg, userAgent);
                LogDebug("获取网页内容：Url: {0}, Headers: {1}", Redactor.Text(target), Redactor.Headers(request.Headers));
                return request;
            },
            (request, token) => AppHttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token),
            cfg, ct);
        webResponse.EnsureSuccessStatusCode( );

        var htmlCode = await HttpTransfer.ReadBodyAsync(webResponse.Content, ct);
        LogDebug("Response: {0}", Redactor.Text(htmlCode));
        return htmlCode;
    }

    /// <summary>
    /// 登录专用：发 GET 并返回未释放的响应，便于调用方读取 <c>Set-Cookie</c> 响应头。调用方负责 Dispose。
    /// </summary>
    public static async Task<HttpResponseMessage> GetRawResponseAsync(string url, AppConfig cfg, CancellationToken ct = default)
    {
        var resp = await HttpTransfer.SendTrustGatedAsync(url, HttpMethod.Get,
            (target, method) =>
            {
                var request = new HttpRequestMessage(method, target);
                BiliHeaders.ApplyStandardGetHeaders(request, target, cfg);
                LogDebug("登录请求：{0}", Redactor.Text(target));
                return request;
            },
            (request, token) => AppHttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token),
            cfg, ct);
        resp.EnsureSuccessStatusCode( );
        return resp;
    }

    /// <summary>
    /// 登录专用：发 POST 表单并返回未释放的响应，便于读取 <c>Set-Cookie</c> 与响应体（cookie 主动续期用）。调用方负责 Dispose。
    /// </summary>
    public static async Task<HttpResponseMessage> PostFormRawAsync(string url, Dictionary<string, string> form, AppConfig cfg, CancellationToken ct = default)
    {
        var resp = await HttpTransfer.SendTrustGatedAsync(url, HttpMethod.Post,
            (target, method) =>
            {
                var request = new HttpRequestMessage(method, target) { Content = new FormUrlEncodedContent(form) };
                BiliHeaders.ApplyStandardGetHeaders(request, target, cfg);
                LogDebug("登录请求 (POST): {0}", Redactor.Text(target));
                return request;
            },
            (request, token) => AppHttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token),
            cfg, ct);
        resp.EnsureSuccessStatusCode( );
        return resp;
    }

    /// <summary>
    /// 登录专用：GET 指定地址（通常是 poll 成功返回的 crossDomain 端点），通过独立 <see cref="CookieContainer"/>
    /// 接收其 <c>Set-Cookie</c> 并返回容器。这是 B 站下发登录 cookie 的正规通道——浏览器正是靠「导航到该 URL」
    /// 拿到 cookie，而 BBDown 之前从未执行这步，只从 <c>data.Url</c> 的 query 解析（该通道已被移除），
    /// 因此拿不到 cookie。重定向由 <see cref="HttpTransfer"/> 逐跳手动跟随，各跳的 Set-Cookie 都进容器。
    /// </summary>
    public static async Task<CookieContainer> GetCookieJarAsync(string url, AppConfig cfg, CancellationToken ct = default)
    {
        var jar = new CookieContainer( );
        using var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.All,
            CookieContainer = jar,
            ServerCertificateCustomValidationCallback = (_, _, _, ssl) =>
                ssl == System.Net.Security.SslPolicyErrors.None ||
                Environment.GetEnvironmentVariable("BBDOWN_INSECURE_TLS") == "1"
        };
        using var client = new HttpClient(handler) { Timeout = DefaultTimeout };
        using var resp = await HttpTransfer.SendTrustGatedAsync(url, HttpMethod.Get,
            (target, method) =>
            {
                var request = new HttpRequestMessage(method, target);
                BiliHeaders.ApplyStandardGetHeaders(request, target, cfg);
                LogDebug("crossDomain GET: {0}", Redactor.Text(target));
                return request;
            },
            client.SendAsync,
            cfg, ct);
        resp.EnsureSuccessStatusCode( );
        return jar;
    }

    // 重写重定向处理，自动跟随多次重定向
    public static async Task<string> GetWebLocationAsync(string url, CancellationToken ct = default)
    {
        using var webRequest = new HttpRequestMessage(HttpMethod.Head, url);
        webRequest.Headers.TryAddWithoutValidation("User-Agent", BiliHeaders.UserAgent);
        webRequest.Headers.TryAddWithoutValidation("Accept-Encoding", "gzip, deflate");
        webRequest.Headers.CacheControl = System.Net.Http.Headers.CacheControlHeaderValue.Parse("no-cache");
        webRequest.Headers.Connection.Clear( );

        LogDebug("获取网页重定向地址：Url: {0}, Headers: {1}", Redactor.Text(url), Redactor.Headers(webRequest.Headers));
        using var webResponse = await AppHttpClient.SendAsync(webRequest, HttpCompletionOption.ResponseHeadersRead, ct);
        webResponse.EnsureSuccessStatusCode( );
        var location = webResponse.RequestMessage!.RequestUri!.AbsoluteUri;
        LogDebug("Location: {0}", Redactor.Text(location));
        return location;
    }

    // 逃生舱：需要自行控制 Header/Range/平台分支时直接构造 HttpRequestMessage 走这里。
    // 不走凭据门也不做逐跳校验：调用方（下载分片）自行负责目标地址的可信性
    public static Task<HttpResponseMessage> SendRawAsync(HttpRequestMessage request, CancellationToken ct = default)
    {
        LogDebug("发送请求：{0} {1}, Headers: {2}", request.Method, Redactor.Text(request.RequestUri?.AbsoluteUri ?? ""), Redactor.Headers(request.Headers));
        return AppHttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
    }

    // 返回裸 JsonDocument，调用方自己取字段并负责 Dispose
    public static async Task<JsonDocument> GetJsonAsync(string url, AppConfig cfg, CancellationToken ct = default)
    {
        return JsonDocument.Parse(await GetWebSourceAsync(url, cfg, null, ct));
    }

    /// <summary>
    /// GetWithRangeAsync
    /// </summary>
    /// <param name="ifRange">
    /// 服务器上次给的 ETag 或 Last-Modified 原文。必须原样回传：自己拿本地文件时间戳去造
    /// If-Range 只会让校验恒通过，等于没做校验。为空则不带该头。
    /// </param>
    public static async Task<HttpResponseMessage> GetWithRangeAsync(string url, long from, long? to, string cookie, string? ifRange = null, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        BiliHeaders.AddDownloadHeaders(request, url, cookie);
        request.Headers.Range = new(from, to);
        if (!string.IsNullOrEmpty(ifRange))
        {
            request.Headers.TryAddWithoutValidation("If-Range", ifRange);
        }

        // 失败响应握着连接不放会拖垮重试，这里先释放再抛
        var response = await SendRawAsync(request, ct);
        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        var status = response.StatusCode;
        response.Dispose( );
        throw new HttpRequestException($"下载请求失败：HTTP {(int) status} {status}", null, status);
    }

    public static async Task<byte[]> GetPostResponseAsync(string Url, byte[] postData, Dictionary<string, string>? headers = null, CancellationToken ct = default)
    {
        LogDebug("Post to: {0}, data: {1}", Redactor.Text(Url), Convert.ToBase64String(postData));
        // 仅对已知幂等的 gRPC 只读查询做有界重试：PlayView / 弹幕视图均不修改服务端状态；
        // Widevine 走独立 client 不经此方法。非幂等写操作切勿复用此方法
        const int maxAttempts = 3;
        var delay = TimeSpan.FromMilliseconds(500);
        for (var attempt = 1; ; attempt++)
        {
            ByteArrayContent content = new(postData);
            content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse("application/grpc");
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

            HttpResponseMessage? response;
            try
            {
                response = await AppHttpClient.SendAsync(request, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw; // 真取消
            }
            catch (OperationCanceledException)
            {
                // 仅 HttpClient.Timeout 触发、非用户取消
                if (attempt >= maxAttempts)
                {
                    throw new TimeoutException($"POST 超时：{Url}");
                }

                await Task.Delay(delay, ct); delay *= 2; continue;
            }
            catch (HttpRequestException ex) when (ex.InnerException is TimeoutException or TaskCanceledException)
            {
                if (attempt >= maxAttempts)
                {
                    throw;
                }

                await Task.Delay(delay, ct); delay *= 2; continue;
            }

            using (response)
            {
                if ((int) response.StatusCode >= 500 && attempt < maxAttempts)
                {
                    await Task.Delay(delay, ct); delay *= 2; continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    throw new HttpRequestException($"gRPC 请求失败：HTTP {(int) response.StatusCode} {response.ReasonPhrase}", null, response.StatusCode);
                }

                var bytes = await HttpTransfer.ReadBodyBytesAsync(response.Content, ct);

                // grpc-status 可能出现在响应头，也可能出现在读完 body 后的 trailer 中
                var status = ReadGrpcMeta(response, "grpc-status");
                if (status is not (null or "0"))
                {
                    throw new HttpRequestException($"gRPC 返回错误 status={status}: {ReadGrpcMeta(response, "grpc-message") ?? "无错误描述"}");
                }

                return bytes;
            }
        }
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
