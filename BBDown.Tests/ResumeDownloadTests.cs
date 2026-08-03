using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core.Util;

namespace BBDown.Tests;

// 所有用例都替换进程级 HTTPUtil.AppHttpClient，必须串行，否则会互相踩踏同一个静态客户端
[CollectionDefinition("DownloadHttpStub")]
public sealed class DownloadHttpStubCollectionDefinition;

[Collection("DownloadHttpStub")]
public class ResumeDownloadTests
{
    private const string Etag = "W/\"orig-etag\"";

    // 不暴露 Content-Length 的响应体，用于逼出「远端不给长度」的开放区间下载路径
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
        public byte[] Data { get; init; } = [];
        public byte[] FullBody { get; init; } = [];
        public string ETag { get; init; } = Etag;
        public bool ProbeHasContentLength { get; init; } = true;
        public bool RangeReturns206 { get; init; } = true;
        // 模拟把开放区间 Range 当越界处理的坏 CDN：Range 请求直接回 416
        public bool RangeReturns416 { get; init; }

        public List<(string? Range, string? IfRange)> Requests { get; } = [];

        private HttpResponseMessage Full( )
        {
            var body = FullBody.Length == 0 ? Data : FullBody;
            // ProbeHasContentLength=false 时用不暴露长度的 content，逼出「远端不给 Content-Length」的开放区间路径
            HttpContent content = ProbeHasContentLength ? new ByteArrayContent(body) : new NoLengthContent(body);
            var resp = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = content,
            };
            resp.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
            if (ProbeHasContentLength)
            {
                resp.Content.Headers.ContentLength = body.Length;
            }

            resp.Headers.TryAddWithoutValidation("ETag", ETag);
            return resp;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var range = request.Headers.Range?.ToString( );
            var ifRange = request.Headers.IfRange?.EntityTag?.ToString( );
            lock (gate)
            {
                Requests.Add((range, ifRange));
            }

            // 探测（无 Range）或服务器判定内容已变（If-Range 不符）→ 回 200 整段
            if (request.Headers.Range is null || request.Headers.Range.Ranges.Count == 0 || !RangeReturns206)
            {
                return Task.FromResult(Full( ));
            }

            // 坏 CDN：把开放区间 Range 当越界处理
            if (RangeReturns416)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.RequestedRangeNotSatisfiable));
            }

            var item = request.Headers.Range.Ranges.First( );
            var start = item.From!.Value;
            var end = item.To ?? (Data.Length - 1);
            if (ifRange is not null && ifRange != ETag)
            {
                return Task.FromResult(Full( ));
            }

            var slice = Data[(int) start..((int) end + 1)];
            var resp = new HttpResponseMessage(HttpStatusCode.PartialContent)
            {
                Content = new ByteArrayContent(slice),
            };
            resp.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
            resp.Headers.TryAddWithoutValidation("ETag", ETag);
            resp.Content.Headers.ContentRange = new System.Net.Http.Headers.ContentRangeHeaderValue(start, end, Data.Length);
            return Task.FromResult(resp);
        }
    }

    private static async Task WithStubClient(ServingHandler handler, Func<Task> act)
    {
        var original = HTTPUtil.AppHttpClient;
        using var client = new HttpClient(handler, disposeHandler: false);
        HTTPUtil.AppHttpClient = client;
        try
        {
            await act( );
        }
        finally
        {
            HTTPUtil.AppHttpClient = original;
        }
    }

    private static string TempDir( )
    {
        var dir = Path.Combine(Path.GetTempPath( ), "bbdown_resume_" + Path.GetRandomFileName( ));
        Directory.CreateDirectory(dir);
        return dir;
    }

    // 旧实现末片 to=-1，已完成的末片永远判不出完成，每次续传都白发一个必然 416 的请求。
    // 这里构造「首片/末片已完成、中间片部分完成」的清单，验证只有中间片会真正发请求。
    [Fact]
    public async Task Resume_SkipsAlreadyCompletedChunksIncludingLast( )
    {
        var dir = TempDir( );
        try
        {
            var data = Enumerable.Range(0, 120).Select(i => (byte) (i % 251)).ToArray( );
            const string url = "https://upos-sz.bilivideo.com/upgcxcode/10/20/2001/2001-1-30280.m4s?e=1";
            var dest = Path.Combine(dir, "video.mp4");
            const int chunkSize = 50; // 120 字节 → (0,49),(50,99),(100,119) 三片

            // 预置磁盘状态：第 0 片整片、第 2 片(末片)整片、第 1 片只写了前 10 字节
            var part = new byte[120];
            data.AsSpan(0, 50).CopyTo(part.AsSpan(0));
            data.AsSpan(50, 10).CopyTo(part.AsSpan(50));
            data.AsSpan(100, 20).CopyTo(part.AsSpan(100));
            File.WriteAllBytes(PartFile.PartPath(dest), part);

            PartFile.Save(dest, new PartManifest
            {
                Fingerprint = PartFile.Fingerprint(url),
                TotalSize = 120,
                ChunkSize = chunkSize,
                IfRange = Etag,
                Completed = [50, 10, 20],
            });

            using var handler = new ServingHandler { Data = data, FullBody = data, ETag = Etag };
            await WithStubClient(handler, ( ) => DownloadUtil.DownloadAsync(
                url, dest, new DownloadUtil.DownloadConfig { SingleThread = false, ChunkSize = chunkSize }, ct: CancellationToken.None));

            // 只有第 1 片（缺 60..99）发了请求，首片与末片都被跳过
            Assert.Single(handler.Requests);
            Assert.Equal("bytes=60-99", handler.Requests[0].Range);
            Assert.True(File.ReadAllBytes(dest).SequenceEqual(data));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    // 缺陷 #5：清单不含内容指纹，换画质后同长度数据被误判「已完成」→ 合并出损坏文件。
    // 这里预置一个指纹不符的清单 + 错误字节，验证旧 part 被丢弃、产出内容正确。
    [Fact]
    public async Task FingerprintMismatch_DiscardsStalePartAndRedownloads( )
    {
        var dir = TempDir( );
        try
        {
            var data = Enumerable.Range(0, 120).Select(i => (byte) (i % 251)).ToArray( );
            const string url = "https://upos-sz.bilivideo.com/upgcxcode/10/20/2001/2001-1-30280.m4s?e=1";
            var dest = Path.Combine(dir, "video.mp4");

            // 故意用别的 url 指纹，且 part 里塞满错误字节
            File.WriteAllBytes(PartFile.PartPath(dest), new byte[120]);
            PartFile.Save(dest, new PartManifest
            {
                Fingerprint = PartFile.Fingerprint("https://other.host/x/y/999-1-80.m4s?e=9"),
                TotalSize = 120,
                ChunkSize = 50,
                IfRange = Etag,
                Completed = [50, 50, 20],
            });

            using var handler = new ServingHandler { Data = data, FullBody = data, ETag = Etag };
            await WithStubClient(handler, ( ) => DownloadUtil.DownloadAsync(
                url, dest, new DownloadUtil.DownloadConfig { SingleThread = false, ChunkSize = 50 }, ct: CancellationToken.None));

            Assert.True(File.ReadAllBytes(dest).SequenceEqual(data));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    // 缺陷 #6：无 Content-Length 时旧实现产出不存在的文件还不报错。这里走单片开放区间，
    // 验证最终文件长度正确、内容完整。
    [Fact]
    public async Task NoContentLength_FallsBackToSingleOpenRangeAndCompletes( )
    {
        var dir = TempDir( );
        try
        {
            var data = Enumerable.Range(0, 73).Select(i => (byte) (i % 251)).ToArray( );
            const string url = "https://upos-sz.bilivideo.com/upgcxcode/10/20/2001/2001-1-30280.m4s?e=1";
            var dest = Path.Combine(dir, "video.mp4");

            using var handler = new ServingHandler
            {
                Data = data,
                FullBody = data,
                ETag = Etag,
                ProbeHasContentLength = false, // 探测不给长度
                RangeReturns206 = false,      // Range 被忽略，回 200 整段
            };
            await WithStubClient(handler, ( ) => DownloadUtil.DownloadAsync(
                url, dest, new DownloadUtil.DownloadConfig { SingleThread = true }, ct: CancellationToken.None));

            var got = File.ReadAllBytes(dest);
            Assert.True(got.SequenceEqual(data));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    // 缺陷 #6 负向：远端给了空响应，绝不能静默产出一个空文件。
    [Fact]
    public async Task NoContentLength_EmptyResponse_ThrowsInsteadOfSilentSuccess( )
    {
        var dir = TempDir( );
        try
        {
            const string url = "https://upos-sz.bilivideo.com/upgcxcode/10/20/2001/2001-1-30280.m4s?e=1";
            var dest = Path.Combine(dir, "video.mp4");

            using var handler = new ServingHandler
            {
                Data = [],
                FullBody = [],
                ETag = Etag,
                ProbeHasContentLength = false,
                RangeReturns206 = false,
            };
            await Assert.ThrowsAsync<IOException>(( ) =>
                WithStubClient(handler, ( ) => DownloadUtil.DownloadAsync(
                    url, dest, new DownloadUtil.DownloadConfig { SingleThread = true }, ct: CancellationToken.None)));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    // 缺陷 #7：旧实现把本地 .tmp 的 mtime 当 If-Range，恒晚于远端 → 校验形同虚设。
    // 这里预置清单 IfRange 为服务器给的 ETag 原文，验证续传请求确实回传了这个 ETag。
    [Fact]
    public async Task Resume_SendsServerEtagAsIfRangeNotLocalTimestamp( )
    {
        var dir = TempDir( );
        try
        {
            var data = Enumerable.Range(0, 120).Select(i => (byte) (i % 251)).ToArray( );
            const string url = "https://upos-sz.bilivideo.com/upgcxcode/10/20/2001/2001-1-30280.m4s?e=1";
            var dest = Path.Combine(dir, "video.mp4");
            const int chunkSize = 50;

            var part = new byte[120];
            data.AsSpan(0, 50).CopyTo(part.AsSpan(0));
            data.AsSpan(50, 10).CopyTo(part.AsSpan(50));
            data.AsSpan(100, 20).CopyTo(part.AsSpan(100));
            File.WriteAllBytes(PartFile.PartPath(dest), part);

            PartFile.Save(dest, new PartManifest
            {
                Fingerprint = PartFile.Fingerprint(url),
                TotalSize = 120,
                ChunkSize = chunkSize,
                IfRange = Etag,
                Completed = [50, 10, 20],
            });

            using var handler = new ServingHandler { Data = data, FullBody = data, ETag = Etag };
            await WithStubClient(handler, ( ) => DownloadUtil.DownloadAsync(
                url, dest, new DownloadUtil.DownloadConfig { SingleThread = false, ChunkSize = chunkSize }, ct: CancellationToken.None));

            // 末片/首片跳过，只发了第 1 片的请求，且带上的 If-Range 就是服务器给的 ETag 原文
            Assert.Single(handler.Requests);
            Assert.Equal(Etag, handler.Requests[0].IfRange);
            Assert.True(File.ReadAllBytes(dest).SequenceEqual(data));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    // 防御性校验：开放区间且本地已有数据，旧实现会轻信「起点越界=已经下完」直接跳过。
    // 这里让坏 CDN 对开放区间 Range 回 416，但磁盘 part 已经包含了 from+completed 这段字节，
    // 验证不再发请求、直接当已完成收尾（不抛异常）。
    [Fact]
    public async Task OpenRange_416WithEnoughDiskBytes_TreatedAsComplete( )
    {
        var dir = TempDir( );
        try
        {
            var data = Enumerable.Range(0, 100).Select(i => (byte) (i % 251)).ToArray( );
            const string url = "https://upos-sz.bilivideo.com/upgcxcode/10/20/2001/2001-1-30280.m4s?e=1";
            var dest = Path.Combine(dir, "video.mp4");
            const int have = 50; // 本地已下 50 字节，续传请求将从 50 起

            File.WriteAllBytes(PartFile.PartPath(dest), data.AsSpan(0, have).ToArray( ));
            PartFile.Save(dest, new PartManifest
            {
                Fingerprint = PartFile.Fingerprint(url),
                TotalSize = 0, // 未知总长 → 退化为开放区间
                ChunkSize = long.MaxValue,
                IfRange = Etag,
                Completed = [have],
            });

            using var handler = new ServingHandler
            {
                Data = data,
                FullBody = data,
                ETag = Etag,
                ProbeHasContentLength = false, // 探测不给长度 → 走开放区间
                RangeReturns416 = true,
            };
            // 不抛异常：磁盘已有 50 字节 >= from+completed，信任本地、跳过
            await WithStubClient(handler, ( ) => DownloadUtil.DownloadAsync(
                url, dest, new DownloadUtil.DownloadConfig { SingleThread = true }, ct: CancellationToken.None));

            var got = File.ReadAllBytes(dest);
            Assert.Equal(have, got.Length);
            Assert.True(got.AsSpan(0, have).SequenceEqual(data.AsSpan(0, have)));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    // 防御性校验（负向）：同样坏 CDN 回 416，但磁盘 part 长度不足（< from+completed）。
    // 验证不再轻信「已经下完」的假设，而是抛错上抛，避免静默产出截断文件。
    [Fact]
    public async Task OpenRange_416WithShortDiskBytes_ThrowsInsteadOfSilentTruncate( )
    {
        var dir = TempDir( );
        try
        {
            var data = Enumerable.Range(0, 100).Select(i => (byte) (i % 251)).ToArray( );
            const string Url = "https://upos-sz.bilivideo.com/upgcxcode/10/20/2001/2001-1-30280.m4s?e=1";
            var dest = Path.Combine(dir, "video.mp4");
            const int Have = 50; // 清单声称已下 50 字节
            const int OnDisk = 30; // 但磁盘实际只有 30 字节

            File.WriteAllBytes(PartFile.PartPath(dest), data.AsSpan(0, OnDisk).ToArray( ));
            PartFile.Save(dest, new PartManifest
            {
                Fingerprint = PartFile.Fingerprint(Url),
                TotalSize = 0,
                ChunkSize = long.MaxValue,
                IfRange = Etag,
                Completed = [Have],
            });

            using var handler = new ServingHandler
            {
                Data = data,
                FullBody = data,
                ETag = Etag,
                ProbeHasContentLength = false,
                RangeReturns416 = true,
            };
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(( ) =>
                WithStubClient(handler, ( ) => DownloadUtil.DownloadAsync(
                    Url, dest, new DownloadUtil.DownloadConfig { SingleThread = true }, ct: CancellationToken.None)));
            Assert.IsType<IOException>(ex.InnerException);
            Assert.Contains("416", ex.InnerException!.Message);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
