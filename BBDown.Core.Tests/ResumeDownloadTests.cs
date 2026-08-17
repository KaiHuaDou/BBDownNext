using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core.Download;

namespace BBDown.Core.Tests;

// 所有用例都替换静态 DownloaderAdapter.HttpClientFactory，必须串行，否则会互相踩踏同一个静态字段
[CollectionDefinition("DownloadHttpStub")]
public sealed class DownloadHttpStubCollectionDefinition;

[Collection<DownloadHttpStubCollectionDefinition>]
public class ResumeDownloadTests
{
    private const string Etag = "W/\"orig-etag\"";

    // 不暴露 Content-Length 的响应体，用于逼出「远端不给长度」的单块全量下载路径
    private sealed class NoLengthContent(byte[] data) : HttpContent
    {
        private readonly byte[] data = data;

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            return stream.WriteAsync(data, 0, data.Length);
        }
    }

    private sealed class ServingHandler : HttpMessageHandler
    {
        private readonly Lock gate = new( );
        private readonly int delayMs;
        public byte[] Data { get; init; } = [];
        public byte[] FullBody { get; init; } = [];
        public bool ProbeHasContentLength { get; init; } = true;
        // 服务器忽略 Range（200 整段），逼出不支持 Range 的路径
        public bool RangeReturns206 { get; init; } = true;
        public HttpStatusCode? FailureStatus { get; init; }

        public List<(string? Range, string? UserAgent, string? Referer, string? Cookie)> Requests { get; } = [];

        public ServingHandler(int delayMs = 0)
        {
            this.delayMs = delayMs;
        }

        private async Task<HttpResponseMessage> Full( )
        {
            await Delay( );
            var body = FullBody.Length == 0 ? Data : FullBody;
            HttpContent content = ProbeHasContentLength ? new ByteArrayContent(body) : new NoLengthContent(body);
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

    // 观测并发峰值：延迟响应制造重叠窗口，统计同时在飞的请求数
    private sealed class GatedServingHandler(byte[] data) : HttpMessageHandler
    {
        private int inFlight;
        private int peak;
        private readonly Lock gate = new( );

        public int PeakConcurrent => Volatile.Read(ref peak);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (gate)
            {
                inFlight++;
                if (inFlight > peak)
                {
                    peak = inFlight;
                }
            }

            try
            {
                await Task.Delay(20, cancellationToken);
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

    private static async Task WithStubClient(HttpMessageHandler handler, Func<Task> act, string cookie = "")
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

    private static string TempDir( )
    {
        var dir = Path.Combine(Path.GetTempPath( ), "bbdown_resume_" + Path.GetRandomFileName( ));
        Directory.CreateDirectory(dir);
        return dir;
    }

    // 目标文件已完整产出过：不发任何请求直接跳过
    [Fact]
    public async Task Download_ExistingFile_Skips( )
    {
        var dir = TempDir( );
        try
        {
            var dest = Path.Combine(dir, "video.mp4");
            File.WriteAllBytes(dest, [1, 2, 3]);

            using var handler = new ServingHandler { Data = [4, 5, 6, 7] };
            await WithStubClient(handler, ( ) => DownloadUtil.DownloadAsync(
                "https://upos-sz.bilivideo.com/x.m4s", dest, new DownloadConfig( ), ct: CancellationToken.None));

            Assert.Empty(handler.Requests);
            Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(dest));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    // 多线程分片下载：stub 按 Range 切片，最终文件内容完整
    [Fact]
    public async Task Download_MultiThread_ProducesCompleteFile( )
    {
        var dir = TempDir( );
        try
        {
            var data = Enumerable.Range(0, 1000).Select(i => (byte) (i % 251)).ToArray( );
            var dest = Path.Combine(dir, "video.mp4");

            using var handler = new ServingHandler { Data = data };
            await WithStubClient(handler, ( ) => DownloadUtil.DownloadAsync(
                "https://upos-sz.bilivideo.com/x.m4s", dest, new DownloadConfig( ), ct: CancellationToken.None));

            Assert.True(File.Exists(dest));
            Assert.True(File.ReadAllBytes(dest).SequenceEqual(data));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    // 服务器不给 Content-Length 也不支持 Range：退化为单块全量下载，仍产出完整文件
    [Fact]
    public async Task Download_NoContentLength_FallsBackToSingleChunkAndCompletes( )
    {
        var dir = TempDir( );
        try
        {
            var data = Enumerable.Range(0, 73).Select(i => (byte) (i % 251)).ToArray( );
            var dest = Path.Combine(dir, "video.mp4");

            using var handler = new ServingHandler
            {
                Data = data,
                FullBody = data,
                ProbeHasContentLength = false,
                RangeReturns206 = false,
            };
            await WithStubClient(handler, ( ) => DownloadUtil.DownloadAsync(
                "https://upos-sz.bilivideo.com/x.m4s", dest, new DownloadConfig( ), ct: CancellationToken.None));

            Assert.True(File.ReadAllBytes(dest).SequenceEqual(data));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    // 预置的 .download 残留与当前地址不符（无有效续传元数据）：downloader 校验失败删除重下，产出正确内容
    [Fact]
    public async Task Download_StaleDownloadFile_RedownloadsFresh( )
    {
        var dir = TempDir( );
        try
        {
            var data = Enumerable.Range(0, 120).Select(i => (byte) (i % 251)).ToArray( );
            var dest = Path.Combine(dir, "video.mp4");
            // 塞满错误字节的残留临时文件，无元数据 → 续传校验必失败
            File.WriteAllBytes(dest + ".download", new byte[120]);

            using var handler = new ServingHandler { Data = data };
            await WithStubClient(handler, ( ) => DownloadUtil.DownloadAsync(
                "https://upos-sz.bilivideo.com/x.m4s", dest, new DownloadConfig( ), ct: CancellationToken.None));

            Assert.True(File.ReadAllBytes(dest).SequenceEqual(data));
            Assert.False(File.Exists(dest + ".download"));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    // 服务器持续回 5xx：下载失败向上抛，不静默产出文件
    [Fact]
    public async Task Download_ServerError_Throws( )
    {
        var dir = TempDir( );
        try
        {
            var dest = Path.Combine(dir, "video.mp4");
            using var handler = new ServingHandler { FailureStatus = HttpStatusCode.InternalServerError };
            await Assert.ThrowsAsync<HttpRequestException>(( ) =>
                WithStubClient(handler, ( ) => DownloadUtil.DownloadAsync(
                    "https://upos-sz.bilivideo.com/x.m4s", dest, new DownloadConfig( ), ct: CancellationToken.None)));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    // 下载中途取消：抛 OperationCanceledException
    [Fact]
    public async Task Download_Cancelled_ThrowsOperationCanceled( )
    {
        var dir = TempDir( );
        try
        {
            var data = Enumerable.Range(0, 4096).Select(i => (byte) (i % 251)).ToArray( );
            var dest = Path.Combine(dir, "video.mp4");

            using var handler = new ServingHandler(50) { Data = data };
            using var cts = new CancellationTokenSource( );
            cts.CancelAfter(80);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(( ) =>
                WithStubClient(handler, ( ) => DownloadUtil.DownloadAsync(
                    "https://upos-sz.bilivideo.com/x.m4s", dest, new DownloadConfig( ), ct: cts.Token)));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    // CMCC 域名强制单线程：即使没开 SingleThread，并发峰值也不超过 1
    [Fact]
    public async Task CmccHost_ForcesSingleConnection( )
    {
        var dir = TempDir( );
        try
        {
            var data = Enumerable.Range(0, 500).Select(i => (byte) (i % 251)).ToArray( );
            var dest = Path.Combine(dir, "video.mp4");

            using var handler = new GatedServingHandler(data);
            await WithStubClient(handler, ( ) => DownloadUtil.DownloadAsync(
                "https://upos-sz-cmcc.bilivideo.com/x.m4s", dest, new DownloadConfig( ), ct: CancellationToken.None));

            Assert.True(handler.PeakConcurrent <= 1, $"并发峰值 {handler.PeakConcurrent} 超过 1");
            Assert.True(File.ReadAllBytes(dest).SequenceEqual(data));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    // 多线程模式确实并行：并发峰值超过 1（与单线程模式形成对照）。
    // 数据须超过 MinimumChunkSize(1MB) 且足够大才会切成多块
    [Fact]
    public async Task Download_MultiThread_ExceedsSingleConnection( )
    {
        var dir = TempDir( );
        try
        {
            var data = Enumerable.Range(0, 5_000_000).Select(i => (byte) (i % 251)).ToArray( );
            var dest = Path.Combine(dir, "video.mp4");

            using var handler = new GatedServingHandler(data);
            await WithStubClient(handler, ( ) => DownloadUtil.DownloadAsync(
                "https://upos-sz.bilivideo.com/x.m4s", dest, new DownloadConfig( ), ct: CancellationToken.None));

            Assert.True(handler.PeakConcurrent > 1, "多线程下载未产生并行连接");
            Assert.True(File.ReadAllBytes(dest).SequenceEqual(data));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    // 进度采样回调：下载超过采样周期（200ms）后 onSample 至少被调用一次，ratio 单调不减
    [Fact]
    public async Task Download_ReportsProgressToOnSample( )
    {
        var dir = TempDir( );
        try
        {
            var data = Enumerable.Range(0, 2048).Select(i => (byte) (i % 251)).ToArray( );
            var dest = Path.Combine(dir, "video.mp4");
            var samples = new List<double>( );
            var config = new DownloadConfig { OnSample = (ratio, _) => { lock (samples) { samples.Add(ratio); } } };

            // 每请求延迟 150ms（探测 + 分片并行各一次），总时长超过采样周期
            using var handler = new ServingHandler(150) { Data = data };
            await WithStubClient(handler, ( ) => DownloadUtil.DownloadAsync(
                "https://upos-sz.bilivideo.com/x.m4s", dest, config, ct: CancellationToken.None));

            Assert.NotEmpty(samples);
            Assert.True(samples[^1] >= samples[0]);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    // 下载请求头与旧 AddDownloadHeaders 一致：UA、非 android 平台 Referer、Cookie 都带上
    [Fact]
    public async Task Download_SendsBrowserLikeHeaders( )
    {
        var dir = TempDir( );
        try
        {
            var data = Enumerable.Range(0, 2048).Select(i => (byte) (i % 251)).ToArray( );
            var dest = Path.Combine(dir, "video.mp4");

            using var handler = new ServingHandler { Data = data };
            await WithStubClient(handler, ( ) => DownloadUtil.DownloadAsync(
                "https://upos-sz.bilivideo.com/x.m4s", dest, new DownloadConfig { Cookie = "SESSDATA=abc" }, ct: CancellationToken.None), cookie: "SESSDATA=abc");

            Assert.NotEmpty(handler.Requests);
            Assert.All(handler.Requests, r => Assert.Equal("Mozilla/5.0", r.UserAgent));
            // Referer 是受限头，.NET 会解析为 Uri 后发送规范形式（带尾斜杠），与旧 AddDownloadHeaders 一致
            Assert.All(handler.Requests, r => Assert.Equal("https://www.bilibili.com/", r.Referer));
            Assert.All(handler.Requests, r => Assert.Equal("SESSDATA=abc", r.Cookie));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    // android 平台地址带 Referer 会被 CDN 拒绝：验证该分支不带 Referer 但仍带 UA
    [Fact]
    public async Task Download_AndroidUrl_OmitsReferer( )
    {
        var dir = TempDir( );
        try
        {
            var data = Enumerable.Range(0, 2048).Select(i => (byte) (i % 251)).ToArray( );
            var dest = Path.Combine(dir, "video.mp4");

            using var handler = new ServingHandler { Data = data };
            await WithStubClient(handler, ( ) => DownloadUtil.DownloadAsync(
                "https://upos-sz.bilivideo.com/x.m4s?platform=android_tv_yst&deadline=1", dest, new DownloadConfig( ), ct: CancellationToken.None));

            Assert.NotEmpty(handler.Requests);
            Assert.All(handler.Requests, r => Assert.Equal("Mozilla/5.0", r.UserAgent));
            Assert.All(handler.Requests, r => Assert.Null(r.Referer));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
