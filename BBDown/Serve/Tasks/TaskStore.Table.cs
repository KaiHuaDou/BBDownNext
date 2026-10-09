using System;
using System.Collections.Generic;
using System.Linq;

using BBDown.Core;
using BBDown.Core.Download;

namespace BBDown.Serve.Tasks;

/// <summary>
/// TaskStore 的两表维护部分：快照查询、已完成表裁剪、移除与收尾，以及 serve 启动参数对请求的覆盖注入
/// 受理与状态机在 TaskStore.cs
/// </summary>
internal sealed partial class TaskStore
{
    public List<DownloadTask> RunningSnapshot( )
    {
        return [.. running.Values];
    }

    public List<DownloadTask> FinishedSnapshot( )
    {
        return [.. finished.Values];
    }

    /// <summary>
    /// 实际在运行的任务：enqueue 暂停态（Pending 尚未进入执行队列）不计入
    /// /running 端点与 /healthz 的计数同用此判据，两处各写一遍会漂移
    /// </summary>
    public List<DownloadTask> RunningOnlySnapshot( )
    {
        return [.. running.Values.Where(t => t.Status != DownloadStatus.Pending)];
    }

    /// <summary>实际在运行的任务数，判据同 <see cref="RunningOnlySnapshot"/></summary>
    public int RunningCount( )
    {
        return running.Values.Count(t => t.Status != DownloadStatus.Pending);
    }

    public void ClearFinished( )
    {
        finished.Clear( );
        NotifyChanged( );
    }

    /// <summary>
    /// 仅清已失败（IsSuccessful == false）的已完成任务
    /// </summary>
    public void ClearFailedFinished( )
    {
        foreach (var (id, task) in finished)
        {
            if (!task.IsSuccessful)
            {
                finished.TryRemove(id, out _);
            }
        }

        NotifyChanged( );
    }

    /// <summary>
    /// 移除指定任务：已完成的直接清；enqueue 暂停态的从暂停表与运行表移除、释放取消源并清事件上下文
    /// 运行中的任务（已投入执行队列）不在此处理，须先用 stop 端点取消
    /// </summary>
    public void RemoveTask(ResourceId id)
    {
        finished.TryRemove(id, out _);
        lock (pendingGate)
        {
            if (pending.TryRemove(id, out var envelope))
            {
                running.TryRemove(id, out _);
                ReleaseContext(envelope.Task.Scope);
                envelope.Task.DisposeCts( );
            }
        }

        NotifyChanged( );
    }

    /// <summary>
    /// 任务结束收尾：运行表移除、写入完成表并裁剪最旧条目
    /// </summary>
    public void MoveToFinished(DownloadTask task)
    {
        running.TryRemove(task.Id, out _);
        finished[task.Id] = task;
        TrimFinishedTasks( );
        NotifyChanged( );
    }

    // 已完成任务无上限增长会造成内存泄漏，超过阈值后按完成时间淘汰最旧的
    private void TrimFinishedTasks( )
    {
        if (finished.Count <= MaxFinishedTasks)
        {
            return;
        }

        // 一次排序淘汰最旧的一批，避免循环内反复 OrderBy 造成 O(n²)
        foreach (var (id, oldest) in finished.OrderBy(kv => kv.Value.TaskFinishTime).Take(finished.Count - MaxFinishedTasks))
        {
            finished.TryRemove(id, out _);
        }
    }

    // serve 模式的工作目录由启动参数 --work-dir 决定，覆盖请求体（请求体根本不含该字段）
    // 客户端无法把写入位置指向任意目录
    internal DownloadRequest ApplyServeWorkDir(DownloadRequest option)
    {
        if (!string.IsNullOrEmpty(workDir))
        {
            return option with { WorkDir = workDir };
        }

        return option;
    }

    // serve 模式的 API host 由启动参数（--api-host/--api-ep-host/--api-tv-host）决定，覆盖请求体（请求体已不含该字段）
    // 客户端无法把请求导向自己控制的服务器、从而窃走操作者的 SESSDATA。空值回落官方默认 host
    internal DownloadRequest ApplyServeHost(DownloadRequest option)
    {
        return option with
        {
            Host = string.IsNullOrWhiteSpace(host) ? BiliApi.MainHost : host.Trim( ),
            EpHost = string.IsNullOrWhiteSpace(epHost) ? BiliApi.MainHost : epHost.Trim( ),
            TvHost = string.IsNullOrWhiteSpace(tvHost) ? BiliApi.TvHost : tvHost.Trim( ),
        };
    }
}

/// <summary>
/// 受理结果：Duplicate 表示命中已有任务（携带已有任务），QueueFull 表示队列写满
/// </summary>
internal sealed record EnqueueResult(DownloadTask? Task, bool Duplicate, bool QueueFull);

/// <summary>
/// 任务受理模式：Execute 受理即写执行队列（等同旧 POST 行为）；Enqueue 仅入暂停表，待 Start 才执行
/// </summary>
internal enum SubmitMode
{
    Execute,
    Enqueue,
}

/// <summary>
/// 排队中的任务执行单元：Request 供下载管线消费，CallBackWebHook 为任务完成回调地址
/// </summary>
internal sealed record TaskEnvelope(DownloadTask Task, DownloadRequest Request, string? CallBackWebHook);

/// <summary>Start 结果：Started 已投入执行队列；AlreadyStarted 已在运行或已结束；NotFound 任务不存在；QueueFull 执行队列写满</summary>
internal enum StartResult
{
    Started,
    AlreadyStarted,
    NotFound,
    QueueFull,
}

/// <summary>Stop 结果：Cancelled 已取消运行中任务；Removed 移除了 enqueue 暂停态任务；NotFound 任务不存在</summary>
internal enum StopResult
{
    Cancelled,
    Removed,
    NotFound,
}
