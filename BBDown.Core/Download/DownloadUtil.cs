using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using static BBDown.Core.Logger;
using static BBDown.Core.Util.HTTPUtil;

namespace BBDown.Core.Download;

public static class DownloadUtil
{
    // CMCC 的 CDN 节点不支持 Range/多线程分片，强行并发会整体失败，故对其强制单线程
    private const string CmccCdnMarker = "-cmcc-";

    /// <summary>封面、弹幕这类一次性小文件：下完即走，不在用户目录里留续传状态。</summary>
    public static Task DownloadFileAsync(string url, string path, DownloadConfig config, CancellationToken ct = default)
    {
        return DownloadAsync(url, path, config, resumable: false, ct);
    }

    /// <summary>
    /// 唯一的下载入口。数据先落到 &lt;path&gt;.bbdown.part（各分片按 offset 并发写同一个文件），
    /// 进度记在 &lt;path&gt;.bbdown.json，全部校验通过后整体 move 成正式文件。
    /// 失败或取消时两者都保留，下次重跑接着下。
    /// </summary>
    public static async Task DownloadAsync(string url, string path, DownloadConfig config, bool resumable = true, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(url))
        {
            return;
        }

        if (!config.NoForceHttp)
        {
            url = ReplaceUrl(url);
        }

        LogDebug("Start downloading: {0}", url);

        var destDir = Path.GetDirectoryName(path)!;
        if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
        {
            Directory.CreateDirectory(destDir);
        }

        if (config.UseAria2c)
        {
            await BBDownAria2c.RunAsync(config.Aria2cPath ?? "aria2c", BBDownAria2c.BuildArgs(url, path, config.Aria2cArgs, config.Cookie), ct);
            if (File.Exists(path + ".aria2") || !File.Exists(path))
            {
                throw new InvalidOperationException("aria2 下载可能存在错误");
            }

            Console.WriteLine( );
            return;
        }

        var singleThread = !resumable || config.SingleThread;
        if (!singleThread && url.Contains(CmccCdnMarker))
        {
            LogWarn("检测到 CMCC 域名 CDN，已经禁用多线程。");
            singleThread = true;
        }

        var fingerprint = PartFile.Fingerprint(url);
        var manifest = PartFile.TryLoad(path);

        // 上一轮已经下完并 move 过：连大小探测都不用发
        if (manifest is { Done: true } && manifest.Fingerprint == fingerprint && PartDownloader.IsCompleteOnDisk(path, manifest.TotalSize))
        {
            LogDebug("文件已下载过，跳过下载");
            return;
        }

        if (manifest is not null && (manifest.Done || !PartFile.Matches(manifest, fingerprint, -1)))
        {
            // 指纹不符（多半是换了画质）或已失效，旧字节一律不能用
            LogDebug("已有的续传数据与本次下载地址不匹配，丢弃重下");
            PartFile.Discard(path);
            manifest = null;
        }

        long totalSize;
        string? ifRange;
        if (manifest?.TotalSize > 0)
        {
            totalSize = manifest.TotalSize;
            ifRange = manifest.IfRange;
            LogDebug("续传：已完成 {0} / {1} bytes", PartFile.DownloadedBytes(manifest), totalSize);
        }
        else
        {
            (totalSize, ifRange) = await ProbeAsync(url, config.Cookie, ct);
            LogDebug("文件大小：{0} bytes", totalSize);
            if (PartDownloader.IsCompleteOnDisk(path, totalSize))
            {
                LogDebug("文件已下载过，跳过下载");
                if (resumable)
                {
                    PartFile.Save(path, PartDownloader.Completed(fingerprint, totalSize, ifRange));
                }

                return;
            }
        }

        var chunkSize = !singleThread && config.ChunkSize > 0 ? config.ChunkSize : long.MaxValue;
        var ranges = PartFile.Ranges(totalSize, chunkSize);
        // 远端没给 Content-Length，退化为一条开放区间：仍可续传，只是进度没有分母
        if (ranges.Count == 0)
        {
            ranges.Add((0, -1));
        }

        manifest ??= new PartManifest
        {
            Fingerprint = fingerprint,
            TotalSize = totalSize,
            ChunkSize = chunkSize,
            IfRange = ifRange,
            Completed = new long[ranges.Count],
        };
        LogDebug("分段数量：{0}", ranges.Count);

        await PartDownloader.RunAsync(url, path, config, manifest, ranges, resumable, ct);
    }

    // 探测文件大小。先带 Range 0-0 请求：206 的 Content-Range 带总长，且读完 1 字节后连接回池，
    // 分片可复用同一连接（省掉每个文件一次 TCP+TLS 握手）。
    // 服务器忽略 Range（200）时直接取 Content-Length（不读 body，连接无法复用）；拒绝 Range（416 等）时退化为无 Range 探测。
    private static async Task<(long Size, string? IfRange)> ProbeAsync(string url, string cookie, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        AddDownloadHeaders(request, url, cookie);
        request.Headers.Range = new(0, 0);
        using var response = await SendRawAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.PartialContent)
        {
            await response.Content.ReadAsByteArrayAsync(ct);
            return (response.Content.Headers.ContentRange?.Length ?? 0, ReadValidator(response));
        }

        if (response.StatusCode == HttpStatusCode.OK)
        {
            return (response.Content.Headers.ContentLength ?? 0, ReadValidator(response));
        }

        return await ProbeFullAsync(url, cookie, ct);
    }

    private static async Task<(long Size, string? IfRange)> ProbeFullAsync(string url, string cookie, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        AddDownloadHeaders(request, url, cookie);
        using var response = await SendRawAsync(request, ct);
        response.EnsureSuccessStatusCode( );
        return (response.Content.Headers.ContentLength ?? 0, ReadValidator(response));
    }

    private static string? ReadValidator(HttpResponseMessage response)
    {
        return response.Headers.ETag?.ToString( ) ?? response.Content.Headers.LastModified?.ToString("R");
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

        LogDebug("将 https 更改为 http");
        return url.Replace("https:", "http:");
    }
}
