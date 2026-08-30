using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BBDown.Core.Util;

/// <summary>
/// 传输层：响应体读取与重定向跟随。两者都是「对端可操纵输入」的边界，故统一收口在此。
/// </summary>
public static class HttpTransfer
{
    // 响应体大小上限：被攻破的端点或 --insecure 下的中间人可用 gzip 炸弹 / 分块慢发打满进程内存
    private const int MaxResponseBytes = 64 * 1024 * 1024;

    private const int MaxRedirectHops = 10;

    private static InvalidDataException ResponseTooLarge( )
    {
        return new InvalidDataException($"响应体超过 {MaxResponseBytes / 1024 / 1024} MB 上限，已中止读取");
    }

    /// <summary>
    /// 响应体读取的唯一入口。开启自动解压后 Content-Length 会被移除，声明长度不可信，
    /// 只能逐块读取并累计设总量上限。全库不应再直接调用 HttpContent 的 ReadAs*Async。
    /// </summary>
    internal static async Task<byte[]> ReadBodyBytesAsync(HttpContent content, CancellationToken ct = default)
    {
        var declared = content.Headers.ContentLength;
        if (declared > MaxResponseBytes)
        {
            throw ResponseTooLarge( );
        }

        await using var stream = await content.ReadAsStreamAsync(ct);
        await using var buffer = new MemoryStream(declared is > 0 and <= MaxResponseBytes ? (int) declared : 0);
        var chunk = new byte[8192];
        long total = 0;
        int read;
        while ((read = await stream.ReadAsync(chunk, ct)) > 0)
        {
            total += read;
            if (total > MaxResponseBytes)
            {
                throw ResponseTooLarge( );
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray( );
    }

    internal static async Task<string> ReadBodyAsync(HttpContent content, CancellationToken ct = default)
    {
        var bytes = await ReadBodyBytesAsync(content, ct);
        // B 站接口响应全为 UTF-8；AOT 下不注册额外编码提供程序，故固定按 UTF-8 解码
        var span = bytes.AsSpan( );
        if (span.StartsWith(Encoding.UTF8.Preamble))
        {
            span = span[Encoding.UTF8.Preamble.Length..];
        }

        return Encoding.UTF8.GetString(span);
    }

    // 只跟随「服务器要求继续」的状态码；300 多选与 304 未修改不构成重定向
    private static bool IsRedirect(HttpStatusCode status)
    {
        return status is HttpStatusCode.Moved or HttpStatusCode.Found or HttpStatusCode.SeeOther
            or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;
    }

    /// <summary>
    /// 携带凭据的请求的唯一发送入口：手动逐跳跟随重定向，每跳都过 <see cref="BiliHeaders.IsTrustedCookieHost"/>。
    /// HttpClient 的自动重定向在库内部完成，凭据门只覆盖首跳，重定向目标会绕过它。
    /// </summary>
    /// <remarks>
    /// 取消令牌由本方法持有并逐跳传给 <paramref name="sendAsync"/>，委托不得自行捕获外部的令牌：
    /// 否则令牌同时存在于委托闭包与形参两条通道，形参改不动闭包里的那一份，形同虚设。
    /// </remarks>
    internal static async Task<HttpResponseMessage> SendTrustGatedAsync(
        string url,
        HttpMethod method,
        Func<string, HttpMethod, HttpRequestMessage> createRequest,
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync,
        AppConfig cfg,
        CancellationToken ct)
    {
        for (var hop = 0; ; hop++)
        {
            if (hop > MaxRedirectHops)
            {
                throw new InvalidOperationException($"重定向次数超过上限（{MaxRedirectHops}）");
            }

            var host = new Uri(url).Host;
            if (!BiliHeaders.IsTrustedCookieHost(host, cfg))
            {
                throw new InvalidOperationException($"拒绝向不可信主机发送携带 Cookie 的请求：{host}");
            }

            using var request = createRequest(url, method);
            var response = await sendAsync(request, ct);
            if (!IsRedirect(response.StatusCode) || response.Headers.Location is not { } location)
            {
                return response;
            }

            var next = location.IsAbsoluteUri ? location.AbsoluteUri : new Uri(new Uri(url), location.OriginalString).AbsoluteUri;
            response.Dispose( );
            // 303 必须转为 GET（与浏览器一致）；307 / 308 保持原方法与请求体
            method = response.StatusCode == HttpStatusCode.SeeOther ? HttpMethod.Get : method;
            url = next;
        }
    }
}
