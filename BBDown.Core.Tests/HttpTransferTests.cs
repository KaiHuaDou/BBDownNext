using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BBDown.Core.Tests;

/// <summary>
/// 响应体读取上限的测试。真实网络请求一律不测（见 AGENTS.md「测试范围约定」）。
/// </summary>
public class HttpTransferTests
{
    [Fact]
    public async Task ReadBodyAsync_DecodesUtf8AndStripsBom( )
    {
        using var content = new ByteArrayContent([.. Encoding.UTF8.Preamble, .. Encoding.UTF8.GetBytes("中文")]);
        Assert.Equal("中文", await HttpTransfer.ReadBodyAsync(content, TestContext.Current.CancellationToken));
    }

    // 声明长度不可信（自动解压后常被移除），但明显超限时可先拒，省去读取
    [Fact]
    public async Task ReadBodyBytesAsync_DeclaredLengthOverLimit_Throws( )
    {
        using var content = new ByteArrayContent([]);
        content.Headers.ContentLength = 128L * 1024 * 1024;

        await Assert.ThrowsAsync<InvalidDataException>(( ) => HttpTransfer.ReadBodyBytesAsync(content, TestContext.Current.CancellationToken));
    }

    // 分块慢发不声明长度，只能靠逐块累计兜住
    [Fact]
    public async Task ReadBodyBytesAsync_EndlessStream_ThrowsOnceLimitExceeded( )
    {
        using var content = new EndlessContent( );

        await Assert.ThrowsAsync<InvalidDataException>(( ) => HttpTransfer.ReadBodyBytesAsync(content, TestContext.Current.CancellationToken));
    }
}

/// <summary>
/// 无上限吐数据的响应体：用于验证逐块读取的累计上限确实生效。
/// 未覆写 TryComputeLength，模拟不声明 Content-Length 的分块响应。
/// </summary>
internal sealed class EndlessContent : HttpContent
{
    protected override bool TryComputeLength(out long length)
    {
        length = 0;
        return false;
    }

    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
    {
        var chunk = new byte[1024 * 1024];
        // 总量 100 MB，足以越过上限；即便被测实现漏判也不会无限循环
        for (var i = 0; i < 100; i++)
        {
            try
            {
                await stream.WriteAsync(chunk, cancellationToken);
            }
            catch (Exception)
            {
                // 读取端超限后关闭管道，写入侧自然失败，与被测行为无关
                return;
            }
        }
    }

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
    {
        return SerializeToStreamAsync(stream, context, CancellationToken.None);
    }
}
