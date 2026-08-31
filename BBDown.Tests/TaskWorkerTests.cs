using System;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

using BBDown.Core;
using BBDown.Core.Download;
using BBDown.Serve.Tasks;

namespace BBDown.Tests;

/// <summary>
/// 任务执行器测试：并发闸门语义（限流排队 / 不限流全并发 / 排队中取消）。
/// </summary>
public class TaskWorkerTests
{
    private static TaskWorker NewWorker(int maxConcurrent)
    {
        var channel = Channel.CreateUnbounded<TaskEnvelope>( );
        return new TaskWorker(channel.Reader, new TaskStore(new ServeConfig( ), channel.Writer), maxConcurrent);
    }

    [Fact]
    public async Task RunGatedAsync_NeverExceedsMaxConcurrent( )
    {
        const int cap = 2;
        const int total = 5;
        var store = new TaskStore(new ServeConfig( ), Channel.CreateUnbounded<TaskEnvelope>( ).Writer);
        using var worker = NewWorker(cap);

        var running = 0;
        var peak = 0;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = Enumerable.Range(0, total).Select(i => TaskStore.CreateTask(new ResourceId.Av(i), "u")).ToList( );
        var runs = tasks.Select(t => worker.RunGatedAsync(t, async ( ) =>
        {
            var now = Interlocked.Increment(ref running);
            int old;
            while ((old = Volatile.Read(ref peak)) < now && Interlocked.CompareExchange(ref peak, now, old) != old) { }

            // 第 cap 个任务进入并发即精确放行，无需自旋轮询等待
            if (now == cap) ready.TrySetResult( );
            await release.Task;
            Interlocked.Decrement(ref running);
        }, TestContext.Current.CancellationToken)).ToList( );

        await ready.Task;
        await Task.Delay(200, TestContext.Current.CancellationToken);
        Assert.Equal(cap, Volatile.Read(ref running));
        Assert.Equal(cap, Volatile.Read(ref peak));
        Assert.Equal(total - cap, tasks.Count(t => t.Status == DownloadStatus.Queued));
        Assert.Equal(cap, tasks.Count(t => t.Status == DownloadStatus.Running));

        release.SetResult( );
        await Task.WhenAll(runs);
        Assert.Equal(cap, Volatile.Read(ref peak));
        Assert.All(tasks, t => Assert.Equal(DownloadStatus.Running, t.Status));
    }

    [Fact]
    public async Task RunGatedAsync_Unlimited_RunsAllConcurrently( )
    {
        var store = new TaskStore(new ServeConfig( ), Channel.CreateUnbounded<TaskEnvelope>( ).Writer);
        using var worker = NewWorker(0);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var running = 0;
        var runs = Enumerable.Range(0, 4).Select(i => worker.RunGatedAsync(TaskStore.CreateTask(new ResourceId.Av(i), "u"),
            async ( ) =>
            {
                var now = Interlocked.Increment(ref running);
                if (now == 4) ready.TrySetResult( );
                await release.Task;
            },
            TestContext.Current.CancellationToken)).ToList( );

        await ready.Task;
        Assert.Equal(4, Volatile.Read(ref running));
        release.SetResult( );
        await Task.WhenAll(runs);
    }

    [Fact]
    public async Task RunGatedAsync_CancelledWhileQueued_DoesNotRunDownload( )
    {
        var store = new TaskStore(new ServeConfig( ), Channel.CreateUnbounded<TaskEnvelope>( ).Writer);
        using var worker = NewWorker(1);
        using var cts = new CancellationTokenSource( );
        var block = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var holder = worker.RunGatedAsync(TaskStore.CreateTask(new ResourceId.Av(1), "u"), ( ) => block.Task, CancellationToken.None);

        var queued = TaskStore.CreateTask(new ResourceId.Av(2), "u");
        var second = worker.RunGatedAsync(queued, ( ) => Task.FromException(new InvalidOperationException("不应执行")), cts.Token);
        await cts.CancelAsync( );

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async ( ) => await second);
        Assert.Equal(DownloadStatus.Queued, queued.Status);
        block.SetResult( );
        await holder;
    }
}
