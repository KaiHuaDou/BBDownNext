using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core.Util;

using static BBDown.Core.Logger;
using static BBDown.Core.Util.Utils;

namespace BBDown.Core.Download;

public static class DownloadUtil
{
    // CMCC 的 CDN 节点不支持 Range/多线程分片，强行并发会整体失败，故对其强制单线程
    private const string CmccCdnMarker = "-cmcc-";

    /// <summary>封面、弹幕这类一次性小文件：下完即走，不保留续传状态。</summary>
    public static Task DownloadFileAsync(string url, string path, DownloadConfig config, CancellationToken ct = default)
    {
        return DownloadAsync(url, path, config, resumable: false, ct: ct);
    }

    /// <summary>
    /// 唯一的下载入口。数据经 downloader 落到 &lt;path&gt;.download，完成后自动改名为正式文件
    /// 失败或取消时 .download 保留（内嵌续传元数据），下次重跑接着下
    /// <paramref name="fallbackUrl"/> 为 host 替换前的原始直链，替换后的地址 404 时改回该地址下载
    /// </summary>
    public static async Task DownloadAsync(string url, string path, DownloadConfig config, string? fallbackUrl = null, bool resumable = true, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(url))
        {
            return;
        }

        if (!config.NoForceHttp)
        {
            url = ReplaceUrl(url);
        }

        url = await ResolveReplacedUrlAsync(url, fallbackUrl, config, ct);

        LogDebug("Start downloading: {0}", url);

        var destDir = Path.GetDirectoryName(path)!;
        if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
        {
            Directory.CreateDirectory(destDir);
        }

        var singleThread = config.SingleThread;
        if (!singleThread && url.Contains(CmccCdnMarker))
        {
            LogWarn("检测到 CMCC 域名 CDN，已经禁用多线程。");
            singleThread = true;
        }

        if (config.UseAria2c)
        {
            // aria2c 中断后目标文件（预分配 / 半成品）与 .aria2 控制文件并存
            // 控制文件仍在时交给 aria2c --continue 恢复，不能把残缺文件当成品跳过
            if (File.Exists(path) && !File.Exists(path + ".aria2"))
            {
                LogDebug("文件已下载过，跳过下载");
                DeleteResumeArtifacts(path);
                return;
            }

            await BBDownAria2c.RunAsync(config.Aria2cPath ?? "aria2c", BBDownAria2c.BuildArgs(url, path, config.Aria2cArgs, config.Cookie, singleThread, config.ParallelCount), ct);
            // 退出码为 0 但产物不落地：按残留信号区分「未下完」与「未产出」，便于定位
            if (File.Exists(path + ".aria2"))
            {
                throw new InvalidOperationException("aria2c 退出码为 0 但控制文件仍在，下载未完成");
            }

            if (!File.Exists(path))
            {
                throw new InvalidOperationException("aria2c 退出码为 0 但目标文件未产出");
            }

            return;
        }

        // 目标文件已完整产出过：直接跳过（.download 只在中断时残留，此时最终文件不存在）
        // .aria2 控制文件仍在时成品可能是 aria2 的预分配半成品，不按成品跳过，清掉控制文件改走普通下载重新覆盖
        if (File.Exists(path) && !File.Exists(path + ".aria2"))
        {
            LogDebug("文件已下载过，跳过下载");
            DeleteResumeArtifacts(path);
            return;
        }

        if (File.Exists(path) && File.Exists(path + ".aria2"))
        {
            LogWarn("目标文件存在但 .aria2 控制文件仍在，可能为未完成的下载，重新下载覆盖");
            SafeDelete(path + ".aria2");
        }

        await DownloaderAdapter.RunAsync(url, path, config, singleThread, resumable, ct);
    }

    // 删除目标文件及其 downloader 临时文件（混流成功等场景的清理）
    internal static void Discard(string path)
    {
        SafeDelete(path);
        SafeDelete(path + ".download");
    }

    // 成品已完整时，其他下载模式在同目标名下遗留的续传文件不再有用，跳过路径顺带回收
    // 遗留的 .download 会让临时目录因非空永不回收；成品不完整时不得调用（续传文件仍有价值）
    internal static void DeleteResumeArtifacts(string path)
    {
        SafeDelete(path + ".download");
        SafeDelete(path + ".aria2");
    }

    // 替换后的直链可能不被备用 CDN 承载（实测大会员影片的 APP 接口流在备用 host 整片 404）
    // 且重试沿用同一地址、永不恢复：探测命中 404 且 host 确经替换时改回原始直链
    private static async Task<string> ResolveReplacedUrlAsync(string url, string? fallbackUrl, DownloadConfig config, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(fallbackUrl))
        {
            return url;
        }

        var original = config.NoForceHttp ? fallbackUrl : ReplaceUrl(fallbackUrl);
        if (HostOf(url) == HostOf(original) || !await IsNotFoundAsync(url, config.Cookie, ct))
        {
            return url;
        }

        LogWarn("替换后的下载地址返回 404，改用原始地址下载");
        return original;
    }

    private static string HostOf(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : "";
    }

    // 对直链发 Range 0-0 的轻量探测：CDN 对不存在的对象与 Range 无关地返回 404
    // 探测自身的网络异常不影响本次下载，按未命中处理；请求头与真实下载一致，避免被 CDN 区别对待
    private static async Task<bool> IsNotFoundAsync(string url, string cookie, CancellationToken ct)
    {
        try
        {
            using var socketHandler = new SocketsHttpHandler
            {
                SslOptions = { RemoteCertificateValidationCallback = (_, _, _, sslPolicyErrors) => HTTPUtil.IsTlsAcceptable(sslPolicyErrors) },
            };
            using var handler = new DownloadHeaderHandler(socketHandler, cookie);
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Range = new RangeHeaderValue(0, 0);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            return response.StatusCode == HttpStatusCode.NotFound;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogDebug("下载地址探测失败：{0}", ex.Message);
            return false;
        }
    }

    /// <summary>
    /// 将下载地址强制转换为 HTTP
    /// </summary>
    private static string ReplaceUrl(string url)
    {
        if (url.Contains(".mcdn.bilivideo.cn:"))
        {
            LogDebug("对 [*.mcdn.bilivideo.cn:xxx] 域名不做处理");
            return url;
        }

        // 只换 scheme：带签名参数的 CDN 地址其 query 内可能嵌套 https://，全文替换会破坏签名
        if (!url.StartsWith("https:", StringComparison.OrdinalIgnoreCase))
        {
            return url;
        }

        LogDebug("将 https 更改为 http");
        return $"http:{url["https:".Length..]}";
    }
}
