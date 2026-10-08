#pragma warning disable CA2000 // HttpClient 构造时接管 handler 链的释放（DelegatingHandler 所有权转移是静态分析误报）

using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core.Util;
using BBDown.Core.Workflow;

using Downloader;

namespace BBDown.Core.Download;

// downloader 库的适配层：把 DownloadService 的配置、进度事件与完成信号映射到 BBDown 的约定
// 断点续传走 downloader 的自动续传：元数据内嵌在 <path>.download 末尾，恢复时比对服务端大小
// 不一致（URL 指向的内容已变）自动删除重下
public static class DownloaderAdapter
{
    // 分片并发上限：B 站 CDN 按客户端限制连接数，连接过多反使每条连接被限速（上游 README 同样建议 8/4 量级）
    // FLV 多片段共享同一配额（见 FlvDownload），片段间与片段内合计不超过该值
    internal const int MaxRangeConcurrency = 16;

    // 分片数按连接数的倍数切：库按「完成一片才开下一片」调度，片远多于连接时快连接轮换多片
    // 慢连接的剩余份额被切成小块，静态大分片的长尾（快连接空闲干等慢连接爬完 128MB）随之消失
    internal const int ChunksPerConnection = 4;

    // 每次网络读取的块大小：库默认 1024（1KB），读取块过小使每秒数十万次分配与系统调用挤压网络吞吐
    private const int BufferBlockSizeBytes = 64 * 1024;

    // 单块读超时与失败重试：B 站 CDN 突发停顿常见，块级超时放宽到 30 秒避免误杀慢速连接
    // CDN 断流频繁，重试次数须多于默认，5 次耗尽即取消全部兄弟分片
    private const int BlockTimeoutMs = 30_000;
    private const int MaxTryAgainOnFailure = 8;
    // 下载数据先入内存缓冲再写盘，128MB 上限限制缓冲占用
    private const long MaxMemoryBufferBytes = 128 * 1024 * 1024;
    // 分片大小下限：文件过小时自动降分片数，避免单片只剩几 KB 徒增请求开销
    private const long MinimumChunkSize = 8 * 1024 * 1024;

    // 可替换：测试经 InternalsVisibleTo 注入带 stub handler 的实例
    internal static Func<string, HttpClient> HttpClientFactory { get; set; } = BuildHttpClient;

    internal static async Task RunAsync(string url, string path, DownloadConfig config, bool singleThread, bool resumable, CancellationToken ct)
    {
        // 采样回调统一走 ProgressBus：scope 由宿主任务作用域携带，阶段外（封面/弹幕等附属下载）静默忽略
        // 传字节增量即可，阶段内累计由总线按 scope 维护（同任务多轨并发不互相覆盖）
        using var progress = new ProgressSampler((ratio, delta) => ProgressBus.Publish(ratio, delta, delta / ProgressSampler.SampleInterval.TotalSeconds));
        var client = HttpClientFactory(config.Cookie);
        var options = new DownloadConfiguration
        {
            ChunkCount = singleThread ? 1 : config.ParallelCount * ChunksPerConnection,
            ParallelCount = singleThread ? 1 : config.ParallelCount,
            ParallelDownload = !singleThread,
            BufferBlockSize = BufferBlockSizeBytes,
            BlockTimeout = BlockTimeoutMs,
            MaxTryAgainOnFailure = MaxTryAgainOnFailure,
            MaximumMemoryBufferBytes = MaxMemoryBufferBytes,
            MinimumChunkSize = MinimumChunkSize,
            EnableAutoResumeDownload = resumable,
            // 目标文件已由调用方判定不存在，此处再判断：存在即跳过而非删除重下
            FileExistPolicy = FileExistPolicy.IgnoreDownload,
            // 自定义工厂下 downloader 不接管请求头与生命周期，客户端由本层管理
            CustomHttpClientFactory = ( ) => client,
        };

        await using var downloader = new DownloadService(options);
        var tcs = new TaskCompletionSource<(Exception? Error, DownloadPackage Package)>(TaskCreationOptions.RunContinuationsAsynchronously);
        downloader.DownloadProgressChanged += (_, e) =>
            progress.Report(e.TotalBytesToReceive > 0 ? (double) e.ReceivedBytesSize / e.TotalBytesToReceive : 0, e.ReceivedBytesSize);
        downloader.DownloadFileCompleted += (_, e) => tcs.TrySetResult((e.Error, (DownloadPackage) e.UserState!));

        try
        {
            // 失败/取消不抛异常，完成信号都走事件；事件先于该方法返回的 Task 完成，顺序等待即可
            await downloader.DownloadFileTaskAsync(url, path, ct);
            var (error, package) = await tcs.Task;

            // 成功判定见 IsDownloadSuccess：Completed 即成功；库把「目标已存在即跳过」以 Failed 送达
            // 但文件保留，须视为成功，否则重跑误报失败
            if (IsDownloadSuccess(package.Status, File.Exists(path)))
            {
                return;
            }

            if (package.Status == DownloadStatus.Failed)
            {
                throw error ?? new IOException($"下载失败：{path}");
            }

            ct.ThrowIfCancellationRequested( );
            throw new IOException("下载未完成");
        }
        finally
        {
            client.Dispose( );
        }
    }

    // 下载成功判定：Completed 即为成功；downloader 的 FileExistPolicy.IgnoreDownload 把「目标已存在即跳过」
    // 以 Failed 状态送达（库行为，实测验证）且保留原文件——该场景必须视为成功，否则重跑会误报失败
    // 文件不存在的 Failed 才是真实下载失败。此隐含约定抽成纯函数以便单测锁定，避免库升级后静默改判
    internal static bool IsDownloadSuccess(DownloadStatus status, bool fileExists)
    {
        return status == DownloadStatus.Completed
               || (status == DownloadStatus.Failed && fileExists);
    }

    // 下载客户端：请求头与 AddDownloadHeaders 一致（UA / Referer 按平台 / Cookie）
    // 关闭自动解压以免媒体流被当压缩内容处理；TLS 校验可由环境变量关闭
    private static HttpClient BuildHttpClient(string cookie)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.None,
            ConnectTimeout = TimeSpan.FromSeconds(30),
            SslOptions = { RemoteCertificateValidationCallback = (_, _, _, sslPolicyErrors) => HTTPUtil.IsTlsAcceptable(sslPolicyErrors) },
        };
        return new HttpClient(new DownloadHeaderHandler(handler, cookie)) { Timeout = Timeout.InfiniteTimeSpan };
    }
}

// 按请求 URL 平台分支加下载头（与 AddDownloadHeaders 一致）：android 平台地址带 Referer 会被拒
internal sealed class DownloadHeaderHandler(HttpMessageHandler inner, string cookie) : DelegatingHandler(inner)
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // 缺 UA 的请求会被 B 站 CDN 直接 403 拒绝
        request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0");
        if (!BiliHeaders.IsAndroidPlatformUrl(request.RequestUri!.AbsoluteUri))
        {
            request.Headers.TryAddWithoutValidation("Referer", BiliApi.Site);
        }

        if (cookie.Length != 0)
        {
            request.Headers.TryAddWithoutValidation("Cookie", cookie);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
