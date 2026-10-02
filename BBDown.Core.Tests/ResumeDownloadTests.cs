using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core.Logging;
using BBDown.Core.Workflow;

namespace BBDown.Core.Tests;

// 本集合替换的是 DownloaderAdapter.HttpClientFactory，与 HttpStubCollectionDefinition
// 替换的 HTTPUtil.AppHttpClient 是不同静态，故两个集合可并行。
[CollectionDefinition("DownloadHttpStub")]
public sealed class DownloadHttpStubCollectionDefinition;

[Collection<DownloadHttpStubCollectionDefinition>]
public class ResumeDownloadTests
{
    private const string Etag = "W/\"orig-etag\"";

    // 独占语义：Range 切片 / 206 / Content-Range / ETag 全部耦合在下载器协议上，
    // 其它桩用不到，故不收进 Stubs
    private sealed class ServingHandler(int delayMs = 0) : HttpMessageHandler
    {
        private readonly Lock gate = new( );
        public byte[] Data { get; init; } = [];
        public byte[] FullBody { get; init; } = [];
        public bool ProbeHasContentLength { get; init; } = true;
        // 服务器忽略 Range（200 整段），逼出不支持 Range 的路径
        public bool RangeReturns206 { get; init; } = true;
        public HttpStatusCode? FailureStatus { get; init; }

        public List<(string? Range, string? UserAgent, string? Referer, string? Cookie)> Requests { get; } = [];

        private async Task<HttpResponseMessage> Full( )
        {
            await Delay( );
            var body = FullBody.Length == 0 ? Data : FullBody;
            HttpContent content = ProbeHasContentLength ? new ByteArrayContent(body) : new ChunkedContent(body);
            var resp = new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
            resp.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
            if (ProbeHasContentLength)
            {
                resp.Content.Headers.ContentLength = body.Length;
            }

            resp.Headers.TryAddWithoutValidation("ETag", Etag);
            return resp;
        }

        private Task Delay( )
        {
            return delayMs == 0 ? Task.CompletedTask : Task.Delay(delayMs);
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (gate)
            {
                Requests.Add((
                    request.Headers.Range?.ToString( ),
                    request.Headers.TryGetValues("User-Agent", out var ua) ? ua.FirstOrDefault( ) : null,
                    request.Headers.TryGetValues("Referer", out var referer) ? referer.FirstOrDefault( ) : null,
                    request.Headers.TryGetValues("Cookie", out var cookie) ? cookie.FirstOrDefault( ) : null));
            }

            if (FailureStatus is { } status)
            {
                await Delay( );
                return new HttpResponseMessage(status);
            }

            // 探测或服务器不支持 Range → 回 200 整段
            if (request.Headers.Range is null || request.Headers.Range.Ranges.Count == 0 || !RangeReturns206)
            {
                return await Full( );
            }

            var item = request.Headers.Range.Ranges.First( );
            var start = item.From!.Value;
            var end = item.To ?? (Data.Length - 1);
            await Delay( );
            var slice = Data[(int) start..((int) end + 1)];
            var resp = new HttpResponseMessage(HttpStatusCode.PartialContent)
            {
                Content = new ByteArrayContent(slice),
            };
            resp.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
            resp.Headers.TryAddWithoutValidation("ETag", Etag);
            resp.Content.Headers.ContentRange = new System.Net.Http.Headers.ContentRangeHeaderValue(start, end, Data.Length);
            return resp;
        }
    }

    // 独占语义：用信号在达到目标并发数时精确放行，确保重叠窗口确定存在，不依赖固定延时窗。
    // releaseAt=1：首请求立即放行（单连接场景）；releaseAt=2：需等到第二请求在飞才放行（多线程场景）
    private sealed class GatedServingHandler(byte[] data, int releaseAt = 2) : HttpMessageHandler
    {
        private int inFlight;
        private int peak;
        private bool released;
        private readonly Lock gate = new( );
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int PeakConcurrent => Volatile.Read(ref peak);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            int current;
            lock (gate)
            {
                inFlight++;
                current = inFlight;
                if (current > peak)
                {
                    peak = current;
                }

                if (current >= releaseAt && !released)
                {
                    released = true;
                    release.TrySetResult( );
                }
            }

            try
            {
                await release.Task;
                var range = request.Headers.Range?.Ranges.FirstOrDefault( );
                if (range is null || range.From is null)
                {
                    var full = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(data) };
                    full.Headers.TryAddWithoutValidation("ETag", Etag);
                    return full;
                }

                var from = range.From.Value;
                var to = range.To ?? data.Length - 1;
                var resp = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(data[(int) from..((int) to + 1)]) };
                resp.Headers.TryAddWithoutValidation("ETag", Etag);
                resp.Content.Headers.ContentRange = new System.Net.Http.Headers.ContentRangeHeaderValue(from, to, data.Length);
                return resp;
            }
            finally
            {
                lock (gate)
                {
                    inFlight--;
                }
            }
        }
    }

    private static async Task WithDownloadStub(HttpMessageHandler handler, Func<Task> act, string cookie = "")
    {
        var original = DownloaderAdapter.HttpClientFactory;
        // 走真实请求头层（DownloadHeaderHandler）+ stub 网络层，验证的才是产品链路
        DownloaderAdapter.HttpClientFactory = _ => new HttpClient(new DownloadHeaderHandler(handler, cookie), disposeHandler: false);
        try
        {
            await act( );
        }
        finally
        {
            DownloaderAdapter.HttpClientFactory = original;
        }
    }

    // 目标文件已完整产出过：不发任何请求直接跳过
    [Fact]
    public async Task Download_ExistingFile_Skips( )
    {
        using var dir = new TempDir( );
        {
            var dest = Path.Combine(dir.FullPath, "video.mp4");
            File.WriteAllBytes(dest, [1, 2, 3]);

            using var handler = new ServingHandler { Data = [4, 5, 6, 7] };
            await WithDownloadStub(handler, ( ) => DownloadUtil.DownloadAsync(
                "https://upos-sz.bilivideo.com/x.m4s", dest, new DownloadConfig( ), ct: CancellationToken.None));

            Assert.Empty(handler.Requests);
            Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(dest));
        }
    }

    // 多线程分片下载：stub 按 Range 切片，最终文件内容完整
    [Fact]
    public async Task Download_MultiThread_ProducesCompleteFile( )
    {
        using var dir = new TempDir( );
        {
            var data = Enumerable.Range(0, 1000).Select(i => (byte) (i % 251)).ToArray( );
            var dest = Path.Combine(dir.FullPath, "video.mp4");

            using var handler = new ServingHandler { Data = data };
            await WithDownloadStub(handler, ( ) => DownloadUtil.DownloadAsync(
                "https://upos-sz.bilivideo.com/x.m4s", dest, new DownloadConfig( ), ct: CancellationToken.None));

            Assert.True(File.Exists(dest));
            Assert.True(File.ReadAllBytes(dest).SequenceEqual(data));
        }
    }

    // 服务器不给 Content-Length 也不支持 Range：退化为单块全量下载，仍产出完整文件
    [Fact]
    public async Task Download_NoContentLength_FallsBackToSingleChunkAndCompletes( )
    {
        using var dir = new TempDir( );
        {
            var data = Enumerable.Range(0, 73).Select(i => (byte) (i % 251)).ToArray( );
            var dest = Path.Combine(dir.FullPath, "video.mp4");

            using var handler = new ServingHandler
            {
                Data = data,
                FullBody = data,
                ProbeHasContentLength = false,
                RangeReturns206 = false,
            };
            await WithDownloadStub(handler, ( ) => DownloadUtil.DownloadAsync(
                "https://upos-sz.bilivideo.com/x.m4s", dest, new DownloadConfig( ), ct: CancellationToken.None));

            Assert.True(File.ReadAllBytes(dest).SequenceEqual(data));
        }
    }

    // 预置的 .download 残留与当前地址不符（无有效续传元数据）：downloader 校验失败删除重下，产出正确内容
    [Fact]
    public async Task Download_StaleDownloadFile_RedownloadsFresh( )
    {
        using var dir = new TempDir( );
        {
            var data = Enumerable.Range(0, 120).Select(i => (byte) (i % 251)).ToArray( );
            var dest = Path.Combine(dir.FullPath, "video.mp4");
            // 塞满错误字节的残留临时文件，无元数据 → 续传校验必失败
            File.WriteAllBytes(dest + ".download", new byte[120]);

            using var handler = new ServingHandler { Data = data };
            await WithDownloadStub(handler, ( ) => DownloadUtil.DownloadAsync(
                "https://upos-sz.bilivideo.com/x.m4s", dest, new DownloadConfig( ), ct: CancellationToken.None));

            Assert.True(File.ReadAllBytes(dest).SequenceEqual(data));
            Assert.False(File.Exists(dest + ".download"));
        }
    }

    // 服务器持续回 5xx：下载失败向上抛，不静默产出文件
    [Fact]
    public async Task Download_ServerError_Throws( )
    {
        using var dir = new TempDir( );
        {
            var dest = Path.Combine(dir.FullPath, "video.mp4");
            using var handler = new ServingHandler { FailureStatus = HttpStatusCode.InternalServerError };
            await Assert.ThrowsAsync<HttpRequestException>(( ) =>
                WithDownloadStub(handler, ( ) => DownloadUtil.DownloadAsync(
                    "https://upos-sz.bilivideo.com/x.m4s", dest, new DownloadConfig( ), ct: CancellationToken.None)));
        }
    }

    // 下载中途取消：抛 OperationCanceledException
    [Fact]
    public async Task Download_Cancelled_ThrowsOperationCanceled( )
    {
        using var dir = new TempDir( );
        {
            var data = Enumerable.Range(0, 4096).Select(i => (byte) (i % 251)).ToArray( );
            var dest = Path.Combine(dir.FullPath, "video.mp4");

            using var handler = new ServingHandler(50) { Data = data };
            using var cts = new CancellationTokenSource( );
            cts.CancelAfter(80);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(( ) =>
                WithDownloadStub(handler, ( ) => DownloadUtil.DownloadAsync(
                    "https://upos-sz.bilivideo.com/x.m4s", dest, new DownloadConfig( ), ct: cts.Token)));
        }
    }

    // CMCC 域名强制单线程：即使没开 SingleThread，并发峰值也不超过 1
    [Fact]
    public async Task CmccHost_ForcesSingleConnection( )
    {
        using var dir = new TempDir( );
        {
            var data = Enumerable.Range(0, 500).Select(i => (byte) (i % 251)).ToArray( );
            var dest = Path.Combine(dir.FullPath, "video.mp4");

            using var handler = new GatedServingHandler(data, releaseAt: 1);
            await WithDownloadStub(handler, ( ) => DownloadUtil.DownloadAsync(
                "https://upos-sz-cmcc.bilivideo.com/x.m4s", dest, new DownloadConfig( ), ct: CancellationToken.None));

            Assert.True(handler.PeakConcurrent <= 1, $"并发峰值 {handler.PeakConcurrent} 超过 1");
            Assert.True(File.ReadAllBytes(dest).SequenceEqual(data));
        }
    }

    // 进度采样回调：下载超过采样周期（200ms）后 ProgressBus 至少收到一次样本，ratio 单调不减
    [Fact]
    public async Task Download_ReportsProgressToProgressBus( )
    {
        using var dir = new TempDir( );
        {
            var data = Enumerable.Range(0, 2048).Select(i => (byte) (i % 251)).ToArray( );
            var dest = Path.Combine(dir.FullPath, "video.mp4");
            var samples = new List<double>( );
            void onProgress(WorkflowEvent evt)
            {
                // 总线广播给所有订阅者，并行测试的样本也会到达，只认本任务作用域的样本
                if (evt is ProgressSampleEvent { Scope: "test-download" } sample)
                {
                    lock (samples)
                    {
                        samples.Add(sample.Ratio);
                    }
                }
            }

            ProgressBus.Subscribe(onProgress);
            try
            {
                // 每请求延迟 150ms（探测 + 分片并行各一次），总时长超过采样周期；阶段内样本才被采集
                using var handler = new ServingHandler(150) { Data = data };
                using (MessageBus.BeginScope("test-download"))
                {
                    using (ProgressBus.BeginStage("下载"))
                    {
                        await WithDownloadStub(handler, ( ) => DownloadUtil.DownloadAsync(
                            "https://upos-sz.bilivideo.com/x.m4s", dest, new DownloadConfig( ), ct: CancellationToken.None));
                    }
                }

                Assert.NotEmpty(samples);
                Assert.True(samples[^1] >= samples[0]);
            }
            finally
            {
                ProgressBus.Unsubscribe(onProgress);
            }
        }
    }

    // 下载请求头须带全：UA、非 android 平台 Referer、Cookie
    [Fact]
    public async Task Download_SendsBrowserLikeHeaders( )
    {
        using var dir = new TempDir( );
        {
            var data = Enumerable.Range(0, 2048).Select(i => (byte) (i % 251)).ToArray( );
            var dest = Path.Combine(dir.FullPath, "video.mp4");

            using var handler = new ServingHandler { Data = data };
            await WithDownloadStub(handler, ( ) => DownloadUtil.DownloadAsync(
                "https://upos-sz.bilivideo.com/x.m4s", dest, new DownloadConfig { Cookie = "SESSDATA=abc" }, ct: CancellationToken.None), cookie: "SESSDATA=abc");

            Assert.NotEmpty(handler.Requests);
            Assert.All(handler.Requests, r => Assert.Equal("Mozilla/5.0", r.UserAgent));
            // Referer 是受限头，.NET 会解析为 Uri 后发送规范形式（带尾斜杠），与旧 AddDownloadHeaders 一致
            Assert.All(handler.Requests, r => Assert.Equal("https://www.bilibili.com/", r.Referer));
            Assert.All(handler.Requests, r => Assert.Equal("SESSDATA=abc", r.Cookie));
        }
    }

    // android 平台地址带 Referer 会被 CDN 拒绝：验证该分支不带 Referer 但仍带 UA
    [Fact]
    public async Task Download_AndroidUrl_OmitsReferer( )
    {
        using var dir = new TempDir( );
        {
            var data = Enumerable.Range(0, 2048).Select(i => (byte) (i % 251)).ToArray( );
            var dest = Path.Combine(dir.FullPath, "video.mp4");

            using var handler = new ServingHandler { Data = data };
            await WithDownloadStub(handler, ( ) => DownloadUtil.DownloadAsync(
                "https://upos-sz.bilivideo.com/x.m4s?platform=android_tv_yst&deadline=1", dest, new DownloadConfig( ), ct: CancellationToken.None));

            Assert.NotEmpty(handler.Requests);
            Assert.All(handler.Requests, r => Assert.Equal("Mozilla/5.0", r.UserAgent));
            Assert.All(handler.Requests, r => Assert.Null(r.Referer));
        }
    }
}
