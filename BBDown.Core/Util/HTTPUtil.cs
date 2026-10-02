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
        // 关闭自动重定向：带凭据的请求由 HttpTransfer.SendTrustGatedAsync 手动逐跳跟随并逐跳过凭据门，
        // 自动跟随会把 Cookie 头原样带到重定向目标，使门禁只覆盖首跳
        AllowAutoRedirect = false,
        AutomaticDecompression = DecompressionMethods.All,
        ServerCertificateCustomValidationCallback = (_, _, _, sslPolicyErrors) => IsTlsAcceptable(sslPolicyErrors)
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
    /// <remarks>
    /// 唯一消费方 <see cref="Live.LiveSegmentWriter"/> 同时做网络与文件 IO，不在离线测试范围内，
    /// 故不可替换：留一个无人使用的替换口会让人误以为这里有覆盖。
    /// </remarks>
    public static readonly HttpClient StreamHttpClient = new(new HttpClientHandler
    {
        AllowAutoRedirect = true,
        AutomaticDecompression = DecompressionMethods.None,
        ServerCertificateCustomValidationCallback = (_, _, _, sslPolicyErrors) => IsTlsAcceptable(sslPolicyErrors)
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

    // 全部出站 client 共用的 TLS 宽松判定：默认仅接受无错误的证书；
    // BBDOWN_INSECURE_TLS=1 时放行自签 / 中间人（抓包调试用），单一判定避免多处各写一份后行为分叉
    internal static bool IsTlsAcceptable(System.Net.Security.SslPolicyErrors sslPolicyErrors)
    {
        return sslPolicyErrors == System.Net.Security.SslPolicyErrors.None
               || Environment.GetEnvironmentVariable("BBDOWN_INSECURE_TLS") == "1";
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
        LogDebug("Response: {0}", Redactor.Text(TruncateForLog(htmlCode)));
        return htmlCode;
    }

    // 超长响应（playurl / 弹幕等）只打头部：全量数据已有 debug_*.json 落盘兜底，日志刷整段只会淹没有用信息
    internal static string TruncateForLog(string text)
    {
        return text.Length <= 8192 ? text : $"{text[..8192]}…（已截断，共 {text.Length} 字符）";
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
    /// 接收其 <c>Set-Cookie</c> 并返回容器。这是 B 站下发登录 cookie 的正规通道——cookie 只能靠「导航到该 URL」
    /// 的响应获得。重定向由 <see cref="HttpTransfer"/> 逐跳手动跟随，各跳的 Set-Cookie 都进容器。
    /// </summary>
    public static async Task<CookieContainer> GetCookieJarAsync(string url, AppConfig cfg, CancellationToken ct = default)
    {
        var jar = new CookieContainer( );
        using var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.All,
            CookieContainer = jar,
            ServerCertificateCustomValidationCallback = (_, _, _, ssl) => IsTlsAcceptable(ssl)
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

    // 重定向地址探测：请求不带任何凭据，手动跟随（AppHttpClient 已关闭自动跟随），
    // 短链（b23.tv 等）目标不受信任主机列表限制——无凭据请求无门禁意义。
    // 用 GET + 响应头即返回而非 HEAD：个别短链目标对 HEAD 回 405，Location 探测会整体失败；
    // 读到 Location 即释放响应，正文不会下载
    public static async Task<string> GetWebLocationAsync(string url, CancellationToken ct = default)
    {
        for (var hop = 0; ; hop++)
        {
            if (hop > HttpTransfer.MaxRedirectHops)
            {
                throw new InvalidOperationException($"重定向次数超过上限（{HttpTransfer.MaxRedirectHops}）");
            }

            using var webRequest = new HttpRequestMessage(HttpMethod.Get, url);
            webRequest.Headers.TryAddWithoutValidation("User-Agent", BiliHeaders.UserAgent);
            webRequest.Headers.CacheControl = System.Net.Http.Headers.CacheControlHeaderValue.Parse("no-cache");
            webRequest.Headers.Connection.Clear( );

            LogDebug("获取网页重定向地址：Url: {0}, Headers: {1}", Redactor.Text(url), Redactor.Headers(webRequest.Headers));
            using var webResponse = await AppHttpClient.SendAsync(webRequest, HttpCompletionOption.ResponseHeadersRead, ct);
            if (HttpTransfer.IsRedirect(webResponse.StatusCode) && webResponse.Headers.Location is { } next)
            {
                url = next.IsAbsoluteUri ? next.AbsoluteUri : new Uri(new Uri(url), next.OriginalString).AbsoluteUri;
                continue;
            }

            webResponse.EnsureSuccessStatusCode( );
            LogDebug("Location: {0}", Redactor.Text(url));
            return url;
        }
    }

    // 返回裸 JsonDocument，调用方自己取字段并负责 Dispose
    public static async Task<JsonDocument> GetJsonAsync(string url, AppConfig cfg, CancellationToken ct = default)
    {
        return JsonDocument.Parse(await GetWebSourceAsync(url, cfg, null, ct));
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
