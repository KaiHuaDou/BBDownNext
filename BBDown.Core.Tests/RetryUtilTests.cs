using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BBDown.Core.Tests;

public class RetryUtilTests
{
    // 首次即成功：只调用一次，不做任何退避等待
    [Fact]
    public async Task RetryAsync_FirstTrySucceeds_NoRetry( )
    {
        var calls = 0;
        var delays = new List<TimeSpan>( );
        var result = await RetryUtil.RetryAsync(
            ( ) => { calls++; return Task.FromResult(42); },
            maxRetry: 3, item: "test", CancellationToken.None,
            shouldRetry: _ => true,
            delay: (backoff, _) => { delays.Add(backoff); return Task.CompletedTask; });

        Assert.Equal(42, result);
        Assert.Equal(1, calls);
        Assert.Empty(delays);
    }

    // 前 N 次失败、第 N+1 次成功：共调用 N+1 次，退避次数与数值与 2^(attempt+1) 一致
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public async Task RetryAsync_RetriesThenSucceeds_HonorsBudgetAndBackoff(int maxRetry)
    {
        var calls = 0;
        var delays = new List<TimeSpan>( );
        var result = await RetryUtil.RetryAsync(
            ( ) =>
            {
                var n = calls++;
                return n < maxRetry
                    ? Task.FromException<int>(new InvalidOperationException($"fail {n}"))
                    : Task.FromResult(n);
            },
            maxRetry, item: "test", CancellationToken.None,
            shouldRetry: _ => true,
            delay: (backoff, _) => { delays.Add(backoff); return Task.CompletedTask; });

        Assert.Equal(maxRetry, result);
        Assert.Equal(maxRetry + 1, calls);
        Assert.Equal(maxRetry, delays.Count);
        for (var i = 0; i < maxRetry; i++)
        {
            Assert.Equal(TimeSpan.FromSeconds(1 << (i + 1)), delays[i]);
        }
    }

    // 不可重试的异常（充电试看）不消耗重试预算，立即抛出
    [Fact]
    public async Task RetryAsync_ChargedPreview_ThrowsImmediately( )
    {
        var calls = 0;
        var delays = new List<TimeSpan>( );
        await Assert.ThrowsAsync<ChargedPreviewException>(async ( ) =>
            await RetryUtil.RetryAsync(
                ( ) => { calls++; return Task.FromException<int>(new ChargedPreviewException("试看")); },
                maxRetry: 3, item: "test", CancellationToken.None,
                shouldRetry: ex => PageDownload.ShouldRetry(ex, CancellationToken.None),
                delay: (backoff, _) => { delays.Add(backoff); return Task.CompletedTask; }));

        Assert.Equal(1, calls);
        Assert.Empty(delays);
    }

    // 用户真正取消时不退避重试，立即抛出
    [Fact]
    public async Task RetryAsync_Cancellation_ThrowsImmediately( )
    {
        using var cts = new CancellationTokenSource( );
        cts.Cancel( );
        var calls = 0;
        var delays = new List<TimeSpan>( );
        await Assert.ThrowsAsync<OperationCanceledException>(async ( ) =>
            await RetryUtil.RetryAsync(
                ( ) => { calls++; return Task.FromException<int>(new OperationCanceledException( )); },
                maxRetry: 3, item: "test", cts.Token,
                shouldRetry: ex => PageDownload.ShouldRetry(ex, cts.Token),
                delay: (backoff, _) => { delays.Add(backoff); return Task.CompletedTask; }));

        Assert.Equal(1, calls);
        Assert.Empty(delays);
    }

    // 始终失败：调用 maxRetry+1 次后抛出末次（最后一次）异常
    [Fact]
    public async Task RetryAsync_AlwaysFails_ThrowsLastException( )
    {
        var calls = 0;
        var last = new InvalidOperationException("last");
        var delays = new List<TimeSpan>( );
        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(async ( ) =>
            await RetryUtil.RetryAsync(
                ( ) => { calls++; return Task.FromException<int>(last); },
                maxRetry: 3, item: "test", CancellationToken.None,
                shouldRetry: _ => true,
                delay: (backoff, _) => { delays.Add(backoff); return Task.CompletedTask; }));

        Assert.Equal(4, calls);
        Assert.Equal(3, delays.Count);
        Assert.Same(last, thrown);
    }

    // 退避上限 16 秒（与 LiveRecorder.Backoff 一致）：2/4/8 之后指数增长停止
    // 大重试预算下等待时间必须有界
    [Fact]
    public async Task RetryAsync_LargeRetryBudget_CapsBackoffAtSixteenSeconds( )
    {
        var calls = 0;
        var delays = new List<TimeSpan>( );
        await Assert.ThrowsAsync<InvalidOperationException>(async ( ) =>
            await RetryUtil.RetryAsync(
                ( ) => { calls++; return Task.FromException<int>(new InvalidOperationException("fail")); },
                maxRetry: 6, item: "test", CancellationToken.None,
                shouldRetry: _ => true,
                delay: (backoff, _) => { delays.Add(backoff); return Task.CompletedTask; }));

        Assert.Equal(7, calls);
        Assert.Equal(6, delays.Count);
        Assert.Equal(TimeSpan.FromSeconds(2), delays[0]);
        Assert.Equal(TimeSpan.FromSeconds(4), delays[1]);
        Assert.Equal(TimeSpan.FromSeconds(8), delays[2]);
        Assert.All(delays, d => Assert.True(d <= TimeSpan.FromSeconds(16), $"退避 {d} 超过 16 秒上限"));
        Assert.Equal(TimeSpan.FromSeconds(16), delays[3]);
        Assert.Equal(TimeSpan.FromSeconds(16), delays[4]);
        Assert.Equal(TimeSpan.FromSeconds(16), delays[5]);
    }
}
