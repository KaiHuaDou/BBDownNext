using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core.Entity;
using static BBDown.Core.Entity.Entity;

using Xunit;

namespace BBDown.Tests;

public class PageOrchestrationTests
{
    private static Page MakePage(int index) => new Page
    {
        index = index,
        aid = "aid" + index,
        cid = "cid" + index,
        epid = "",
        title = "t" + index,
        dur = 1,
        res = "",
        pubTime = 0,
    };

    // 默认（不停止）：中间分P 失败，后续分P 仍继续跑，失败以列表形式返回（由调用方汇总成 AggregateException）
    [Fact]
    public async Task Default_ContinuesAfterFailureAndCollectsErrors()
    {
        var pages = Enumerable.Range(0, 3).Select(MakePage).ToList();
        var ran = new List<int>();
        const int failAt = 1;

        Func<Page, CancellationToken, Task> run = (p, _) =>
        {
            ran.Add(p.index);
            if (p.index == failAt) throw new InvalidOperationException("boom");
            return Task.CompletedTask;
        };

        var errors = await Program.RunPagesAsync(pages, stopOnError: false, run, CancellationToken.None);

        Assert.Equal([0, 1, 2], ran); // 失败页之后仍在跑
        Assert.Single(errors);
        Assert.Equal(1, errors[0].Page.index);
        Assert.Equal("boom", errors[0].Error.Message);
    }

    // --stop-on-error：第一个失败即停，后续分P 不再执行
    [Fact]
    public async Task StopOnError_AbortsAfterFirstFailure()
    {
        var pages = Enumerable.Range(0, 3).Select(MakePage).ToList();
        var ran = new List<int>();
        const int failAt = 1;

        Func<Page, CancellationToken, Task> run = (p, _) =>
        {
            ran.Add(p.index);
            if (p.index == failAt) throw new InvalidOperationException("boom");
            return Task.CompletedTask;
        };

        var errors = await Program.RunPagesAsync(pages, stopOnError: true, run, CancellationToken.None);

        Assert.Equal([0, 1], ran); // 第 2 个分P 没有跑
        Assert.Single(errors);
        Assert.Equal(1, errors[0].Page.index);
    }

    // Ctrl+C 的取消信号必须立刻上抛，不能被吞进 AggregateException
    [Fact]
    public async Task OperationCanceled_IsRethrownNotAggregated()
    {
        var pages = Enumerable.Range(0, 3).Select(MakePage).ToList();
        var ran = new List<int>();

        Func<Page, CancellationToken, Task> run = (p, _) =>
        {
            ran.Add(p.index);
            throw new OperationCanceledException( );
        };

        await Assert.ThrowsAsync<OperationCanceledException>( ( ) =>
            Program.RunPagesAsync(pages, stopOnError: false, run, CancellationToken.None));

        Assert.Equal([0], ran);
    }
}
