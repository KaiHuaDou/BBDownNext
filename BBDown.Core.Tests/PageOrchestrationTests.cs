using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core.Entity;

namespace BBDown.Core.Tests;

public class PageOrchestrationTests
{
    private static Page MakePage(int index)
    {
        return new Page
        {
            Index = index,
            Aid = "aid" + index,
            Cid = "cid" + index,
            EpId = "",
            Title = "t" + index,
            Dur = 1,
            Res = "",
            PubTime = 0,
        };
    }

    // 默认（不停止）：中间分P 失败，后续分P 仍继续跑，失败以列表形式返回（由调用方汇总成 AggregateException）
    [Fact]
    public async Task Default_ContinuesAfterFailureAndCollectsErrors( )
    {
        var pages = Enumerable.Range(0, 3).Select(MakePage).ToList( );
        var ran = new List<int>( );
        const int failAt = 1;

        Task run(Page p, CancellationToken _)
        {
            ran.Add(p.Index);
            if (p.Index == failAt)
            {
                throw new InvalidOperationException("boom");
            }

            return Task.CompletedTask;
        }

        var errors = await PageQueue.RunPagesAsync(pages, stopOnError: false, run, CancellationToken.None);

        Assert.Equal([0, 1, 2], ran); // 失败页之后仍在跑
        var error = Assert.Single(errors);
        Assert.Equal(1, error.Page.Index);
        Assert.Equal("boom", error.Error.Message);
    }

    // --stop-on-error：第一个失败即停，后续分 P 不执行
    [Fact]
    public async Task StopOnError_AbortsAfterFirstFailure( )
    {
        var pages = Enumerable.Range(0, 3).Select(MakePage).ToList( );
        var ran = new List<int>( );
        const int failAt = 1;

        Task run(Page p, CancellationToken _)
        {
            ran.Add(p.Index);
            if (p.Index == failAt)
            {
                throw new InvalidOperationException("boom");
            }

            return Task.CompletedTask;
        }

        var errors = await PageQueue.RunPagesAsync(pages, stopOnError: true, run, CancellationToken.None);

        Assert.Equal([0, 1], ran); // 第 2 个分P 没有跑
        var error = Assert.Single(errors);
        Assert.Equal(1, error.Page.Index);
    }

    // Ctrl+C 的取消信号必须立刻上抛，不能被吞进 AggregateException
    [Fact]
    public async Task OperationCanceled_TokenCanceled_IsRethrownNotAggregated( )
    {
        var pages = Enumerable.Range(0, 3).Select(MakePage).ToList( );
        var ran = new List<int>( );
        using var cts = new CancellationTokenSource( );
        cts.Cancel( );

        Task run(Page p, CancellationToken _)
        {
            ran.Add(p.Index);
            throw new OperationCanceledException( );
        }

        await Assert.ThrowsAsync<OperationCanceledException>(( ) =>
            PageQueue.RunPagesAsync(pages, stopOnError: false, run, cts.Token));

        Assert.Equal([0], ran);
    }

    // HttpClient 超时同样抛 OperationCanceledException，但此时 ct 并未取消，
    // 应按普通失败收集并继续跑其余分 P，而不是误判成用户中止整个任务
    [Fact]
    public async Task OperationCanceled_TokenNotCanceled_IsCollectedAsFailure( )
    {
        var pages = Enumerable.Range(0, 3).Select(MakePage).ToList( );
        var ran = new List<int>( );

        Task run(Page p, CancellationToken _)
        {
            ran.Add(p.Index);
            throw new OperationCanceledException( );
        }

        var errors = await PageQueue.RunPagesAsync(pages, stopOnError: false, run, CancellationToken.None);

        Assert.Equal([0, 1, 2], ran);
        Assert.Equal(3, errors.Count);
    }

    // 收藏夹全失败时失败列表可达数万项，汇总行只列前 20 项与总数
    [Fact]
    public void FormatPages_ManyItems_TruncatesWithCount( )
    {
        var items = Enumerable.Range(0, 25).Select(i => (MakePage(i), new InvalidOperationException("boom") as Exception)).ToList( );

        var message = PageQueue.FormatPages(items);

        Assert.Contains("P19（aid19）", message);
        Assert.DoesNotContain("P20（aid20）", message);
        Assert.Contains("共 25 条", message);
        Assert.True(message.Length < 512);
    }

    [Fact]
    public void FormatPages_WithinLimit_ShowsAllWithoutCount( )
    {
        var items = Enumerable.Range(0, 3).Select(i => (MakePage(i), new InvalidOperationException("boom") as Exception)).ToList( );

        var message = PageQueue.FormatPages(items);

        Assert.Equal("P0（aid0）, P1（aid1）, P2（aid2）", message);
    }
}