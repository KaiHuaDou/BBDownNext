using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using BBDown.Core;
using BBDown.Core.Download;
using BBDown.Serve.Tasks;

namespace BBDown.Tests;

/// <summary>
/// 任务域状态容器测试：服务端配置注入（work-dir / host）、任务创建与状态迁移。
/// </summary>
public class TaskStoreTests
{
    private static TaskStore NewStore(ServeConfig config)
    {
        return new TaskStore(config, new TaskQueue( ));
    }

    [Fact]
    public void ApplyServeWorkDir_FallsBackToServerConfig( )
    {
        // 缺陷回归：此前 SetUpServer 丢弃了 --work-dir，serve 任务始终落到进程当前目录。
        // 验证服务端配置的工作目录会被注入到每个任务（且请求体不含该字段，无法被客户端覆盖）。
        var tmp = Path.Combine(Path.GetTempPath( ), "bbdown-workdir-" + Guid.NewGuid( ).ToString("N"));
        var store = NewStore(new ServeConfig(WorkDir: tmp));

        var opts = store.ApplyServeWorkDir(new DownloadRequest { Url = "https://www.bilibili.com/video/BV1xx411c7XD" });

        Assert.Equal(tmp, opts.WorkDir);
    }

    [Fact]
    public void ApplyServeHost_FallsBackToServerConfig( )
    {
        // P0-1 回归：host 由 serve 启动参数决定，请求体不含该字段，无法被客户端覆盖。
        var store = NewStore(new ServeConfig(Host: "https://biliplus.example.com", EpHost: "https://biliplus.example.com", TvHost: "api.snm0516.aisee.tv"));

        var opts = store.ApplyServeHost(new DownloadRequest { Url = "https://www.bilibili.com/video/BV1xx411c7XD" });

        Assert.Equal("https://biliplus.example.com", opts.Host);
        Assert.Equal("https://biliplus.example.com", opts.EpHost);
        Assert.Equal("api.snm0516.aisee.tv", opts.TvHost);
    }

    [Fact]
    public void ApplyServeHost_EmptyFallsBackToDefault( )
    {
        // §2.5：serve 启动参数 host 为空时回落官方默认，避免空 host 抛出 UriFormatException
        var store = NewStore(new ServeConfig(Host: "", EpHost: null, TvHost: "  "));

        var opts = store.ApplyServeHost(new DownloadRequest { Url = "https://www.bilibili.com/video/BV1xx411c7XD" });

        Assert.Equal(BiliApi.MainHost, opts.Host);
        Assert.Equal(BiliApi.MainHost, opts.EpHost);
        Assert.Equal(BiliApi.TvHost, opts.TvHost);
    }

    [Fact]
    public void CreateTask_AlwaysQueued( )
    {
        // 受理即 Queued（202 语义），执行权由 TaskWorker 闸门授予后转 Running
        var store = NewStore(new ServeConfig( ));

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

        Assert.Null(store.GetContext(new ResourceId.Av(1)));
    }

    [Fact]
    public void ReleaseContext_NotRegistered_ReturnsNull( )
    {
        var store = NewStore(new ServeConfig( ));

        Assert.Null(store.ReleaseContext(new ResourceId.Av(1)));
    }
}
