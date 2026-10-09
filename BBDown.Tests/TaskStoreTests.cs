using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Channels;

using BBDown.Serve.Tasks;

namespace BBDown.Tests;

/// <summary>
/// 任务域状态容器测试：服务端配置注入（work-dir / host）、任务创建与状态迁移、暂停表的启停
/// </summary>
public class TaskStoreTests
{
    private static TaskStore NewStore(ServeConfig config)
    {
        return new TaskStore(config, Channel.CreateUnbounded<TaskEnvelope>( ).Writer);
    }

    [Fact]
    public void ApplyServeWorkDir_FallsBackToServerConfig( )
    {
        // 验证服务端配置的工作目录会被注入到每个任务（且请求体不含该字段，无法被客户端覆盖）
        var tmp = Path.Combine(Path.GetTempPath( ), "bbdown-workdir-" + Guid.NewGuid( ).ToString("N"));
        var store = NewStore(new ServeConfig(WorkDir: tmp));

        var opts = store.ApplyServeWorkDir(new DownloadRequest { Url = "https://www.bilibili.com/video/BV1xx411c7XD" });

        Assert.Equal(tmp, opts.WorkDir);
    }

    [Fact]
    public void ApplyServeHost_FallsBackToServerConfig( )
    {
        // host 由 serve 启动参数决定，请求体不含该字段，无法被客户端覆盖
        var store = NewStore(new ServeConfig(Host: "https://biliplus.example.com", EpHost: "https://biliplus.example.com", TvHost: "api.snm0516.aisee.tv"));

        var opts = store.ApplyServeHost(new DownloadRequest { Url = "https://www.bilibili.com/video/BV1xx411c7XD" });

        Assert.Equal("https://biliplus.example.com", opts.Host);
        Assert.Equal("https://biliplus.example.com", opts.EpHost);
        Assert.Equal("api.snm0516.aisee.tv", opts.TvHost);
    }

    [Fact]
    public void ApplyServeHost_EmptyFallsBackToDefault( )
    {
        // serve 启动参数 host 为空时回落官方默认，避免空 host 抛出 UriFormatException
        var store = NewStore(new ServeConfig(Host: "", EpHost: null, TvHost: "  "));

        var opts = store.ApplyServeHost(new DownloadRequest { Url = "https://www.bilibili.com/video/BV1xx411c7XD" });

        Assert.Equal(BiliApi.MainHost, opts.Host);
        Assert.Equal(BiliApi.MainHost, opts.EpHost);
        Assert.Equal(BiliApi.TvHost, opts.TvHost);
    }

    [Fact]
    public void CreateTask_AlwaysQueued( )
    {
        // 受理即 Queued（202 状态），执行权由 TaskWorker 闸门授予后转 Running
        _ = NewStore(new ServeConfig( ));

        var task = TaskStore.CreateTask(new ResourceId.Av(114514), "BV1xx411c7XD");

        Assert.Equal(DownloadStatus.Queued, task.Status);
    }

    [Fact]
    public void MoveToFinished_ExposesViaGet( )
    {
        var store = NewStore(new ServeConfig( ));
        var task = TaskStore.CreateTask(new ResourceId.Av(1), "u");

        store.MoveToFinished(task);

        Assert.Empty(store.RunningSnapshot( ));
        Assert.Equal(task, store.Get(new ResourceId.Av(1)));
    }

    [Fact]
    public void Get_UnknownId_ReturnsNull( )
    {
        var store = NewStore(new ServeConfig( ));

        Assert.Null(store.Get(new ResourceId.Av(999)));
    }

    [Fact]
    public void ClearFinished_RemovesAllFinished( )
    {
        var store = NewStore(new ServeConfig( ));
        store.MoveToFinished(TaskStore.CreateTask(new ResourceId.Av(1), "u"));

        store.ClearFinished( );

        Assert.Empty(store.FinishedSnapshot( ));
    }

    [Fact]
    public void ClearFailedFinished_KeepsSuccessful( )
    {
        var store = NewStore(new ServeConfig( ));
        var ok = TaskStore.CreateTask(new ResourceId.Av(1), "u");
        ok.IsSuccessful = true;
        store.MoveToFinished(ok);
        var bad = TaskStore.CreateTask(new ResourceId.Av(2), "u");
        bad.IsSuccessful = false;
        store.MoveToFinished(bad);

        store.ClearFailedFinished( );

        Assert.Equal([ok], store.FinishedSnapshot( ));
    }

    [Fact]
    public void GetContext_NotRegistered_ReturnsNull( )
    {
        // 交互关闭（默认）或任务未受理时无事件上下文
        var store = NewStore(new ServeConfig( ));

        Assert.Null(store.GetContext(new ResourceId.Av(1).ToString( )));
    }

    [Fact]
    public void ReleaseContext_NotRegistered_ReturnsNull( )
    {
        var store = NewStore(new ServeConfig( ));

        Assert.Null(store.ReleaseContext(new ResourceId.Av(1).ToString( )));
    }

    // live 形式不经 InputResolver 的触网分支（TryDispatch 直接打标，FixAvidAsync 对 LiveRoom 直接返回），
    // 因此 EnqueueAsync 在本用例里是纯内存操作
    private static async Task<(TaskStore Store, Channel<TaskEnvelope> Queue, ResourceId Id)> EnqueuedAsync(ServeConfig config, SubmitMode mode)
    {
        var queue = Channel.CreateUnbounded<TaskEnvelope>( );
        var store = new TaskStore(config, queue.Writer);

        var result = await store.EnqueueAsync(new ServeRequestOptions { Url = "live" + NextLiveRoom( ) }, mode, CancellationToken.None);

        return (store, queue, result.Task!.Id);
    }

    private static int nextLiveRoom = 900000;

    private static string NextLiveRoom( )
    {
        return Interlocked.Increment(ref nextLiveRoom).ToString(CultureInfo.InvariantCulture);
    }

    [Fact]
    public async Task Start_PendingTask_WritesToQueue( )
    {
        var (store, queue, id) = await EnqueuedAsync(new ServeConfig( ), SubmitMode.Enqueue);

        Assert.Equal(StartResult.Started, store.Start(id));
        Assert.True(queue.Reader.TryRead(out var envelope));
        Assert.Equal(id, envelope.Task.Id);
        Assert.Equal(DownloadStatus.Queued, envelope.Task.Status);
    }

    [Fact]
    public async Task Start_AlreadyStarted_ReportsConflictInsteadOfNotFound( )
    {
        // 已在运行与「不存在」含义不同：前者是重复的启动请求，端点据此返回 409 而非 404
        var (store, _, id) = await EnqueuedAsync(new ServeConfig( ), SubmitMode.Enqueue);
        store.Start(id);

        Assert.Equal(StartResult.AlreadyStarted, store.Start(id));
    }

    [Fact]
    public void Start_UnknownId_ReturnsNotFound( )
    {
        var store = NewStore(new ServeConfig( ));

        Assert.Equal(StartResult.NotFound, store.Start(new ResourceId.Av(123456)));
    }

    // 暂停态任务尚未投入执行，取消它只会表现为「已启动后秒取消」，故从暂停表与运行表整体移除
    [Fact]
    public async Task Stop_PendingTask_RemovesItAndCannotStartLater( )
    {
        var (store, queue, id) = await EnqueuedAsync(new ServeConfig( ), SubmitMode.Enqueue);

        Assert.Equal(StopResult.Removed, store.Stop(id));
        Assert.Null(store.Get(id));
        Assert.False(queue.Reader.TryRead(out _));
        Assert.Equal(StartResult.NotFound, store.Start(id));
    }

    [Fact]
    public async Task Stop_RunningTask_CancelsWithoutRemoving( )
    {
        var (store, _, id) = await EnqueuedAsync(new ServeConfig( ), SubmitMode.Execute);

        Assert.Equal(StopResult.Cancelled, store.Stop(id));
        Assert.NotNull(store.Get(id));
    }

    [Fact]
    public void Stop_UnknownId_ReturnsNotFound( )
    {
        var store = NewStore(new ServeConfig( ));

        Assert.Equal(StopResult.NotFound, store.Stop(new ResourceId.Av(654321)));
    }

    // 原样重发同一地址不该为它放大成数次对 B 站的请求：受理应直接命中已有任务
    [Fact]
    public async Task EnqueueAsync_SameUrlTwice_ReportsDuplicate( )
    {
        var queue = Channel.CreateUnbounded<TaskEnvelope>( );
        var store = new TaskStore(new ServeConfig( ), queue.Writer);
        var req = new ServeRequestOptions { Url = "live" + NextLiveRoom( ) };

        var first = await store.EnqueueAsync(req, SubmitMode.Execute, CancellationToken.None);
        var second = await store.EnqueueAsync(req, SubmitMode.Execute, CancellationToken.None);

        Assert.False(first.Duplicate);
        Assert.True(second.Duplicate);
        Assert.Equal(first.Task!.Id, second.Task!.Id);
        Assert.Single(store.RunningSnapshot( ));
    }
}
