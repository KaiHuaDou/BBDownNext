using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace BBDown.Core.Tests;

/// <summary>
/// 不声明 <c>Content-Length</c> 的分块响应体，逼被测实现走逐块累计而非声明长度
/// </summary>
/// <param name="data">单块内容；为 <see langword="null"/> 时反复发送同一块，用于验证总量上限。</param>
internal sealed class ChunkedContent(byte[]? data) : HttpContent
{
    // 无上限时上限 100 MB：即便被测实现漏判上限也不会无限循环
    private const int MaxChunks = 100;

    protected override bool TryComputeLength(out long length)
    {
        length = 0;
        return false;
    }

    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
    {
        var chunk = data ?? new byte[1024 * 1024];
        var rounds = data is null ? MaxChunks : 1;
        for (var i = 0; i < rounds; i++)
        {
            try
            {
                await stream.WriteAsync(chunk, cancellationToken);
            }
            catch (Exception)
            {
                // 读取端越过上限后会关闭管道，写入侧自然失败，与被测行为无关
                return;
            }
        }
    }

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
    {
        return SerializeToStreamAsync(stream, context, CancellationToken.None);
    }
}
