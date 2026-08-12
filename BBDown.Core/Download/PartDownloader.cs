using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core.Util;

using Microsoft.Win32.SafeHandles;

using static BBDown.Core.Util.HTTPUtil;

namespace BBDown.Core.Download;

// 分片续传的执行层：并发写 part 文件、进度上报、清单持久化。编排决策在 DownloadUtil
public static class PartDownloader
{
    private const int BlockSize = 1024 * 1024;
    private const int ManifestSaveIntervalMs = 2000;

    internal static async Task RunAsync(string url, string path, DownloadConfig config, PartManifest manifest,
                                        List<(long From, long To)> ranges, bool resumable, CancellationToken ct)
    {
        var partPath = PartFile.PartPath(path);
        var completed = manifest.Completed;
        var manifestLock = new Lock( );
        var lastSaveTick = 0L;

        using var progress = new ProgressBar(config.OnSample, ct);
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
                // -1 即 Parallel 默认并发度（= Environment.ProcessorCount），分片并行度完全由下载器决定
                MaxDegreeOfParallelism = -1,
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

    private static async Task DownloadRangeAsync(int index, string url, SafeFileHandle handle,
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

    internal static bool IsCompleteOnDisk(string path, long totalSize)
    {
        return totalSize > 0 && File.Exists(path) && new FileInfo(path).Length == totalSize;
    }

    internal static PartManifest Completed(string fingerprint, long totalSize, string? ifRange)
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
}
