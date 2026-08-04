using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Util;

using static BBDown.Core.Logger;
using static BBDown.Core.Util.HTTPUtil;

namespace BBDown.Download;

internal static class DownloadUtil
{
    private const int BlockSize = 1024 * 1024;
    private const int ManifestSaveIntervalMs = 2000;
    // CMCC 的 CDN 节点不支持 Range/多线程分片，强行并发会整体失败，故对其强制单线程
    private const string CmccCdnMarker = "-cmcc-";

    public sealed class DownloadConfig
    {
        public bool UseAria2c { get; set; }
        public string Aria2cArgs { get; set; } = string.Empty;
        public bool NoForceHttp { get; set; }
        public bool SingleThread { get; set; }
        public DownloadTask? RelatedTask { get; set; }
        public string Cookie { get; set; } = string.Empty;
        // <=0 表示不限制（Parallel 默认取 ProcessorCount）
        public int MaxDegreeOfParallelism { get; set; }
        // 多线程分片大小（字节）
        public long ChunkSize { get; set; } = PartFile.DefaultChunkSize;
    }

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
            await BBDownAria2c.RunAsync(BBDownAria2c.aria2c, BBDownAria2c.BuildArgs(url, path, config.Aria2cArgs, config.Cookie), ct);
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
        if (manifest is { Done: true } && manifest.Fingerprint == fingerprint && IsCompleteOnDisk(path, manifest.TotalSize))
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
            if (IsCompleteOnDisk(path, totalSize))
            {
                LogDebug("文件已下载过，跳过下载");
                if (resumable)
                {
                    PartFile.Save(path, Completed(fingerprint, totalSize, ifRange));
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

        await RunRangesAsync(url, path, config, manifest, ranges, resumable, ct);
    }

    private static async Task RunRangesAsync(string url, string path, DownloadConfig config, PartManifest manifest,
                                             List<(long From, long To)> ranges, bool resumable, CancellationToken ct)
    {
        var partPath = PartFile.PartPath(path);
        var completed = manifest.Completed;
        var manifestLock = new Lock( );
        var lastSaveTick = 0L;

        using var progress = new ProgressBar(config.RelatedTask is { } relatedTask ? relatedTask.ApplySample : null);
        progress.Report(0);

        void Report( )
        {
            var downloaded = 0L;
            foreach (var c in completed)
            {
                downloaded += c;
            }

            progress.Report(manifest.TotalSize > 0 ? (double) downloaded / manifest.TotalSize : 0, downloaded);
        }

        void Persist(bool force)
        {
            lock (manifestLock)
            {
                var now = Environment.TickCount64;
                if (!force && now - lastSaveTick < ManifestSaveIntervalMs)
                {
                    return;
                }

                lastSaveTick = now;
                // completed 的每个下标只由一个分片线程写，这里读到的最坏情况是稍旧的值，
                // 代价只是崩溃后多下几个字节，不会错位
                PartFile.Save(path, manifest);
            }
        }

        using (var handle = File.OpenHandle(partPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite, FileOptions.Asynchronous))
        {
            if (manifest.TotalSize > 0)
            {
                RandomAccess.SetLength(handle, manifest.TotalSize);
            }

            Persist(force: true);

            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = config.MaxDegreeOfParallelism > 0 ? config.MaxDegreeOfParallelism : -1,
                CancellationToken = ct,
            };
            await Parallel.ForEachAsync(Enumerable.Range(0, ranges.Count), parallelOptions, async (index, token) =>
            {
                try
                {
                    await DownloadRangeAsync(index, url, handle, config.Cookie, manifest, ranges, completed, ranges.Count > 1, Report, token);
                }
                catch (NotSupportedException ex)
                {
                    throw new NotSupportedException("服务器可能并不支持多线程/Range 下载，请使用 --single-thread 关闭多线程", ex);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"分片 {index} 下载失败：{ex.Message}", ex);
                }
                finally
                {
                    Persist(force: false);
                }
            });
        }

        var partLength = new FileInfo(partPath).Length;
        // 旧实现在拿不到 Content-Length 时会产出一个根本不存在的文件却不报错，这里必须拦死
        if (manifest.TotalSize > 0 && partLength != manifest.TotalSize)
        {
            throw new IOException($"下载不完整：{partLength} / {manifest.TotalSize} bytes");
        }

        if (partLength == 0)
        {
            throw new IOException("下载结果为空");
        }

        File.Move(partPath, path, true);
        if (!resumable)
        {
            PartFile.Discard(path);
            return;
        }

        manifest.TotalSize = partLength;
        manifest.Done = true;
        PartFile.Save(path, manifest);
    }

    private static async Task DownloadRangeAsync(int index, string url, Microsoft.Win32.SafeHandles.SafeFileHandle handle,
                                                 string cookie, PartManifest manifest, List<(long From, long To)> ranges,
                                                 long[] completed, bool requireRangeSupport, Action report, CancellationToken ct)
    {
        var (from, to) = ranges[index];
        var expected = to >= 0 ? to - from + 1 : -1;
        if (expected >= 0 && completed[index] >= expected)
        {
            report( );
            return;
        }

        HttpResponseMessage response;
        try
        {
            response = await GetWithRangeAsync(url, from + completed[index], to >= 0 ? to : null, cookie, manifest.IfRange, ct);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable && expected < 0 && completed[index] > 0)
        {
            // 开放区间且本地已有数据：起点越界多半是因为已经下完了。
            // 不轻信这个假设——确认磁盘上的 part 文件确实已包含 from+completed 这段字节，
            // 否则某个 CDN 把开放区间 Range 当越界处理就会让我们静默截断了文件。
            var onDisk = RandomAccess.GetLength(handle);
            if (onDisk < from + completed[index])
            {
                throw new IOException($"分片 {index} 收到 416 但磁盘数据不足（{onDisk} < {from + completed[index]}），放弃不安全的续传假定");
            }

            return;
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.OK)
            {
                // 服务器无视了 Range（或 If-Range 判定内容已变），只能从头来过
                if (requireRangeSupport)
                {
                    throw new NotSupportedException("Range request is not supported.");
                }

                completed[index] = 0;
            }
            else
            {
                CaptureValidators(response, manifest);
            }

            var contentLength = response.Content.Headers.ContentLength;
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            var buffer = new byte[BlockSize];
            var offset = from + completed[index];
            var received = 0L;
            do
            {
                var read = await stream.ReadAsync(buffer, ct);
                if (read == 0)
                {
                    break;
                }

                await RandomAccess.WriteAsync(handle, buffer.AsMemory(0, read), offset, ct);
                offset += read;
                received += read;
                completed[index] += read;
                report( );
            }
            while (expected < 0 || completed[index] < expected);

            if (expected >= 0 && completed[index] != expected)
            {
                throw new IOException($"分片 {index} 收到 {completed[index]} / {expected} bytes");
            }

            // 206 时 Content-Length 是本次响应的字节数，不是文件总长
            if (expected < 0 && contentLength is not null && received != contentLength)
            {
                throw new IOException($"响应被截断：{received} / {contentLength} bytes");
            }
        }
    }

    /// <summary>记下服务器给的校验器，续传时原样回传；顺带从 Content-Range 补全未知的总长度。</summary>
    private static void CaptureValidators(HttpResponseMessage response, PartManifest manifest)
    {
        manifest.IfRange ??= response.Headers.ETag?.ToString( )
                             ?? response.Content.Headers.LastModified?.ToString("R");
        if (manifest.TotalSize <= 0 && response.Content.Headers.ContentRange?.Length is { } length)
        {
            manifest.TotalSize = length;
        }
    }

    private static bool IsCompleteOnDisk(string path, long totalSize)
    {
        return totalSize > 0 && File.Exists(path) && new FileInfo(path).Length == totalSize;
    }

    private static PartManifest Completed(string fingerprint, long totalSize, string? ifRange)
    {
        return new PartManifest
        {
            Fingerprint = fingerprint,
            TotalSize = totalSize,
            ChunkSize = totalSize,
            IfRange = ifRange,
            Completed = [totalSize],
            Done = true,
        };
    }

    private static async Task<(long Size, string? IfRange)> ProbeAsync(string url, string cookie, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        AddDownloadHeaders(request, url, cookie);
        using var response = await SendRawAsync(request, ct);
        response.EnsureSuccessStatusCode( );
        var validator = response.Headers.ETag?.ToString( ) ?? response.Content.Headers.LastModified?.ToString("R");
        return (response.Content.Headers.ContentLength ?? 0, validator);
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
