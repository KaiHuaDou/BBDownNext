using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;

namespace BBDown.Serve;

// SSRF 防护：仅允许公网 http/https 出向回调，并在建立 TCP 连接前二次校验私网（§2.3 / P1-14）
internal static class SsrfGuard
{
    // 回调专用 client（§2.3）：禁止自动重定向，杜绝 302 跳进内网/云元数据面；
    // 并在真正建立 TCP 连接前对最终端点 IP 做二次校验，消除 DNS 重绑定窗口（TOCTOU-free）。
    internal static readonly HttpClient WebHookClient = new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        ConnectCallback = async (context, token) =>
        {
            var endpoint = context.DnsEndPoint;
            IPAddress ip;
            if (IPAddress.TryParse(endpoint.Host, out var literal))
            {
                ip = literal;
            }
            else
            {
                var addresses = await Dns.GetHostAddressesAsync(endpoint.Host, token);
                if (addresses.Length == 0)
                {
                    throw new HttpRequestException($"CallBackWebHook 无法解析 {endpoint.Host}");
                }

                ip = addresses[0];
            }

            // 连接前最终判定：私网/回环/链路本地/未指定地址一律拒绝
            if (IsPrivateAddress(ip))
            {
                throw new HttpRequestException($"CallBackWebHook 拒绝内网/回环地址 {ip}");
            }

            var socket = new Socket(ip.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            await socket.ConnectAsync(new IPEndPoint(ip, endpoint.Port), token);
            return new NetworkStream(socket, ownsSocket: true);
        }
    })
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    /// <summary>
    /// CallBackWebHook 仅允许公网 http/https，拒绝回环与内网地址，避免 SSRF 探活 169.254.169.254 等元数据服务（P1-14）
    /// </summary>
    internal static bool IsSafeWebHook(Uri uri)
    {
        if (uri.Scheme is not ("http" or "https"))
        {
            return false;
        }

        if (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !IPAddress.TryParse(uri.Host, out var ip) || !IsPrivateAddress(ip);
    }

    // 内部可见：供单测覆盖新增的私网段（§2.4）
    internal static bool IsPrivateAddress(IPAddress ip)
    {
        // IPv4-mapped IPv6（::ffff:a.b.c.d）须按其 IPv4 等价地址判定，
        // 否则 ::ffff:169.254.169.254 这类云元数据地址会绕过私网过滤（§2.4）
        if (ip.IsIPv4MappedToIPv6)
        {
            ip = ip.MapToIPv4( );
        }

        if (IPAddress.IsLoopback(ip))
        {
            return true;
        }

        // 未指定地址：IPv6 :: 作为出向目标等同本机，应拒绝（原实现漏网，§2.4）
        if (IPAddress.IPv6Any.Equals(ip))
        {
            return true;
        }

        var bytes = ip.GetAddressBytes( );
        return ip.AddressFamily switch
        {
            AddressFamily.InterNetwork =>
                bytes[0] == 10 ||
                (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) ||
                (bytes[0] == 192 && bytes[1] == 168) ||
                // 链路本地，含 169.254.169.254 云元数据地址
                (bytes[0] == 169 && bytes[1] == 254) ||
                bytes[0] == 127 ||
                // 0.0.0.0/8 为保留/未指定地址，作为出向 webhook 目标等同本机（P1-14）
                bytes[0] == 0 ||
                // CGNAT 共享地址（运营商级 NAT，原实现漏网，§2.4）
                (bytes[0] == 100 && bytes[1] is >= 64 and <= 127) ||
                // 192.0.0.0/24（原实现漏网，§2.4）
                (bytes[0] == 192 && bytes[1] == 0 && bytes[2] == 0) ||
                // 198.18.0.0/15 基准网络（benchmark，原实现漏网，§2.4）
                (bytes[0] == 198 && bytes[1] is >= 18 and <= 19) ||
                // 多播 224.0.0.0/4（原实现漏网，§2.4）
                (bytes[0] is >= 224 and <= 239),
            AddressFamily.InterNetworkV6 =>
                ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal ||
                // 用内建判定替代脆弱的字符串前缀比较（原实现对 fc/fd 做 StartsWith，§2.4）
                ip.IsIPv6UniqueLocal || ip.IsIPv6Multicast,
            _ => true
        };
    }

    internal static bool IsLoopbackUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return IPAddress.TryParse(uri.Host, out var ip) && IPAddress.IsLoopback(ip);
    }
}
