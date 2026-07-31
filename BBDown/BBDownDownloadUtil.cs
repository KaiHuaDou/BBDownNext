using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using static BBDown.Core.Entity.Entity;
using static BBDown.Core.Logger;
using static BBDown.Core.Util.HTTPUtil;

namespace BBDown;

internal static class BBDownDownloadUtil
{
    public sealed class DownloadConfig
    {
        public bool UseAria2c { get; set; }
        public string Aria2cArgs { get; set; } = string.Empty;
        public bool ForceHttp { get; set; }
        public bool MultiThread { get; set; }
        public DownloadTask? RelatedTask { get; set; }
        public string Cookie { get; set; } = string.Empty;
        // <=0 表示不限制（Parallel 默认取 ProcessorCount）
        public int MaxDegreeOfParallelism { get; set; }
        // 多线程分片大小（字节），默认 20MB
        public long ChunkSize { get; set; } = 20 * 1024 * 1024;
    }

    private static async Task RangeDownloadToTmpAsync(int id, string url, string tmpName, long fromPosition, long? toPosition, Action<int, long, long> onProgress, string cookie, bool failOnRangeNotSupported = false, CancellationToken ct = default)
    {
        DateTimeOffset? lastTime = File.Exists(tmpName) ? new FileInfo(tmpName).LastWriteTimeUtc : null;
        using var fileStream = new FileStream(tmpName, FileMode.OpenOrCreate);
        fileStream.Seek(0, SeekOrigin.End);
        if (toPosition > 0 && fileStream.Position == toPosition - fromPosition + 1)
        {
            // 已下载完成 直接汇报进度并跳过下载
            onProgress(id, fileStream.Position, fileStream.Position);
            return;
        }

        var downloadedBytes = fromPosition + fileStream.Position;

        using var response = await GetWithRangeAsync(url, downloadedBytes, toPosition, cookie, lastTime, ct);

        if (response.StatusCode == HttpStatusCode.OK) // server doesn't response a partial content
        {
            if (failOnRangeNotSupported && (downloadedBytes > 0 || toPosition != null)) throw new NotSupportedException("Range request is not supported.");
            downloadedBytes = 0;
            fileStream.Seek(0, SeekOrigin.Begin);
        }

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        var contentLength = response.Content.Headers.ContentLength;
        // 已知总长度时用它作为进度分母；未知时退化为 MaxValue（进度停在 0%, 但不再编造虚假百分比）
        var totalBytes = downloadedBytes + (contentLength ?? long.MaxValue - downloadedBytes);
        long received = 0;

        const int blockSize = 1024 * 1024;
        var buffer = new byte[blockSize];

        while (contentLength == null ? true : received < contentLength)
        {
            var read = await stream.ReadAsync(buffer, ct);
            if (read == 0) break;
            await fileStream.WriteAsync(buffer.AsMemory(0, read), ct);
            received += read;
            onProgress(id, downloadedBytes + received - fromPosition, totalBytes);
        }

        // 完整性：本次会话写入的字节数必须等于 Content-Length。
        // 206 Partial Content 时 Content-Length 是「本分片剩余字节」而非累计文件长度，
        // 之前用 FileInfo.Length（累计）比对会在单线程续传时误报 Retry...
        if (contentLength != null && received != contentLength)
        {
            throw new IOException("Retry...");
        }
    }

    public static async Task DownloadFileAsync(string url, string path, DownloadConfig config, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(url)) return;
        if (config.ForceHttp) url = ReplaceUrl(url);
        LogDebug("Start downloading: {0}", url);
        var desDir = Path.GetDirectoryName(path)!;
        if (!string.IsNullOrEmpty(desDir) && !Directory.Exists(desDir)) Directory.CreateDirectory(desDir);
        if (config.UseAria2c)
        {
            await BBDownAria2c.DownloadFileByAria2cAsync(url, path, config.Aria2cArgs, config.Cookie, ct);
            if (File.Exists(path + ".aria2") || !File.Exists(path))
            {
                throw new InvalidOperationException("aria2下载可能存在错误");
            }

            Console.WriteLine( );
            return;
        }

        // 重试与退避统一由调用方（DownloadPageAsync）负责，.tmp 续传保证重试不会丢已下载的字节
        var tmpName = Path.Combine(desDir, Path.GetFileName(path) + ".tmp");
        using var progress = new ProgressBar(config.RelatedTask);
        await RangeDownloadToTmpAsync(0, url, tmpName, 0, null, (_, downloaded, total) => progress.Report((double) downloaded / total, downloaded), config.Cookie, false, ct);
        File.Move(tmpName, path, true);
    }

    public static async Task MultiThreadDownloadFileAsync(string url, string path, DownloadConfig config, CancellationToken ct = default)
    {
        if (config.ForceHttp) url = ReplaceUrl(url);
        LogDebug("Start downloading: {0}", url);
        if (config.UseAria2c)
        {
            await BBDownAria2c.DownloadFileByAria2cAsync(url, path, config.Aria2cArgs, config.Cookie, ct);
            if (File.Exists(path + ".aria2") || !File.Exists(path))
            {
                throw new InvalidOperationException("aria2下载可能存在错误");
            }

            Console.WriteLine( );
            return;
        }

        var fileSize = await GetFileSizeAsync(url, config.Cookie, ct);
        LogDebug("文件大小：{0} bytes", fileSize);
        //已下载过, 跳过下载
        if (File.Exists(path) && new FileInfo(path).Length == fileSize)
        {
            LogDebug("文件已下载过, 跳过下载");
            return;
        }

        var allClips = GetAllClips(url, fileSize, config.ChunkSize);
        var total = allClips.Count;
        LogDebug("分段数量：{0}", total);
        ConcurrentDictionary<int, long> clipProgress = new( );
        foreach (var i in allClips) clipProgress[i.index] = 0;

        using var progress = new ProgressBar(config.RelatedTask);
        progress.Report(0);
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = config.MaxDegreeOfParallelism > 0 ? config.MaxDegreeOfParallelism : -1,
            CancellationToken = ct,
        };
        await Parallel.ForEachAsync(allClips, parallelOptions, async (clip, token) =>
        {
            var tmp = Path.Combine(Path.GetDirectoryName(path)!, clip.index.ToString("00000") + "_" + Path.GetFileNameWithoutExtension(path) + (Path.GetExtension(path).EndsWith(".mp4") ? ".vclip" : ".aclip"));
            try
            {
                await RangeDownloadToTmpAsync(clip.index, url, tmp, clip.from, clip.to == -1 ? null : clip.to, (index, downloaded, _) =>
                {
                    clipProgress[index] = downloaded;
                    progress.Report((double) clipProgress.Values.Sum( ) / fileSize, clipProgress.Values.Sum( ));
                }, config.Cookie, true, token);
            }
            catch (NotSupportedException ex)
            {
                throw new NotSupportedException("服务器可能并不支持多线程/Range 下载, 请使用 --single-thread 关闭多线程", ex);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"分片 {clip.index} 下载失败: {ex.Message}", ex);
            }
        });
    }

    //此函数主要是切片下载逻辑
    internal static List<Clip> GetAllClips(string url, long fileSize, long chunkSize = 20 * 1024 * 1024)
    {
        List<Clip> clips = [];
        var index = 0;
        long counter = 0;
        while (fileSize > 0)
        {
            var size = Math.Min(chunkSize, fileSize);
            Clip c = new( )
            {
                index = index,
                from = counter,
                // 闭区间 [from, to]，非末片各占 chunkSize 字节；末片 to=-1 表示下到结尾
                to = fileSize > chunkSize ? counter + size - 1 : -1
            };
            clips.Add(c);
            fileSize -= size;
            counter += size;
            index++;
        }

        return clips;
    }

    private static async Task<long> GetFileSizeAsync(string url, string cookie, CancellationToken ct = default)
    {
        using var httpRequestMessage = new HttpRequestMessage(HttpMethod.Get, url);
        AddDownloadHeaders(httpRequestMessage, url, cookie);
        using var response = await SendRawAsync(httpRequestMessage, ct);
        response.EnsureSuccessStatusCode( );
        return response.Content.Headers.ContentLength ?? 0;
    }

    /// <summary>
    /// 将下载地址强制转换为HTTP
    /// </summary>
    /// <param name="url"></param>
    /// <returns></returns>
    private static string ReplaceUrl(string url)
    {
        if (url.Contains(".mcdn.bilivideo.cn:"))
        {
            LogDebug("对[*.mcdn.bilivideo.cn:xxx]域名不做处理");
            return url;
        }

        LogDebug("将https更改为http");
        return url.Replace("https:", "http:");
    }
}