using System;
using System.Buffers;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core.Util;

namespace BBDown.Live;

/// <summary>
/// 把一条直播 FLV 流写进一个分段文件，直到断流、下播或被取消。
/// </summary>
internal static class LiveSegmentWriter
{
    private const int BufferSize = 64 * 1024;
    // FLV 头三字节。CDN 出错时会回 HTML/JSON 而 HTTP 状态仍是 200，不校验就会写出一堆无法播放的垃圾段
    private static readonly byte[] FlvSignature = [0x46, 0x4C, 0x56];

    /// <summary>
    /// 服务端保活时可能长时间不推数据。超过该间隔没有任何字节即判定断流，交由调用方重连。
    /// </summary>
    internal static TimeSpan SilenceTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 返回本段写入的字节数。
    /// <paramref name="ct"/> 取消是**正常终止**而非错误——按下停录键时已写入的内容必须留下来参与混流，
    /// 所以这里不抛 <see cref="OperationCanceledException"/>，由调用方自行检查取消状态。
    /// </summary>
    public static async Task<long> WriteAsync(string url, string partPath, string cookie, Action<long>? onBytes, CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
        {
            return 0;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        HTTPUtil.AddLiveStreamHeaders(request, cookie);

        HttpResponseMessage response;
        try
        {
            response = await HTTPUtil.StreamHttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return 0;
        }

        using (response)
        {
            response.EnsureSuccessStatusCode( );

            await using var source = await response.Content.ReadAsStreamAsync(ct);
            // Append 而非 Create：重试同名分段时不该把已录内容抹掉
            await using var target = new FileStream(partPath, FileMode.Append, FileAccess.Write, FileShare.Read, BufferSize, useAsync: true);
            return await PumpAsync(source, target, onBytes, ct);
        }
    }

    private static async Task<long> PumpAsync(Stream source, Stream target, Action<long>? onBytes, CancellationToken ct)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        long written = 0;
        var verified = false;
        try
        {
            while (true)
            {
                int read;
                using var silence = CancellationTokenSource.CreateLinkedTokenSource(ct);
                silence.CancelAfter(SilenceTimeout);
                try
                {
                    read = await source.ReadAsync(buffer.AsMemory(0, BufferSize), silence.Token);
                }
                catch (OperationCanceledException)
                {
                    // 静默超时与用户停录都在这里落地，二者都属正常终止
                    break;
                }

                if (read == 0)
                {
                    break;
                }

                if (!verified)
                {
                    EnsureFlv(buffer, read);
                    verified = true;
                }

                // 刻意不传 ct：64 KiB 的落盘是有界操作，中途取消只会在分段末尾留下半个 FLV tag
                await target.WriteAsync(buffer.AsMemory(0, read), CancellationToken.None);
                written += read;
                onBytes?.Invoke(read);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            await target.FlushAsync(CancellationToken.None);
        }

        return written;
    }

    private static void EnsureFlv(byte[] buffer, int read)
    {
        if (read < FlvSignature.Length || !buffer.AsSpan(0, FlvSignature.Length).SequenceEqual(FlvSignature))
        {
            throw new InvalidDataException("拉流返回的不是 FLV 数据，可能是防盗链拦截或链接已失效");
        }
    }
}
