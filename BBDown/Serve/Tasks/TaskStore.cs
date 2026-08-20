using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core;
using BBDown.Core.Download;
using BBDown.Core.Pipeline;
using BBDown.Core.Workflow;

namespace BBDown.Serve.Tasks;

/// <summary>
/// 任务状态容器与受理入口：running / finished 两表、按 ResourceId 去重、完成后裁剪。
/// host 三兄弟与工作目录由服务端启动参数固定，经 ApplyServe* 注入每个任务（P0-1 / P0-2）。
/// </summary>
internal sealed class TaskStore(ServeConfig config, TaskQueue queue)
{
    private const int MaxFinishedTasks = 200;

    private readonly ConcurrentDictionary<ResourceId, DownloadTask> running = new( );
    private readonly ConcurrentDictionary<ResourceId, DownloadTask> finished = new( );
    private readonly ConcurrentDictionary<ResourceId, ChannelWorkflowContext> contexts = new( );
    private readonly string? workDir = config.WorkDir;
    private readonly string? host = config.Host;
    private readonly string? epHost = config.EpHost;
    private readonly string? tvHost = config.TvHost;
    private readonly TaskQueue queue = queue;
    private readonly bool interactive = config.Interactive;

    /// <summary>
    /// 受理任务：解析 URL → 去重 → 入队。命中已有任务返回 Duplicate（携带已有任务），
    /// 队列写满返回 QueueFull，均由端点映射为对应状态码。
    /// </summary>
    public async Task<EnqueueResult> EnqueueAsync(ServeRequestOptions req, CancellationToken token)
    {
        var option = ApplyServeHost(ApplyServeWorkDir(req.ToDownloadRequest( )));
        var config = WorkSetup.ResolveConfig(option, option.Api);
        // 解析阶段尚无任务级令牌（任务在解析成功后创建），用进程级令牌：服务器关停即可中断排队中的解析
        var id = await InputResolver.ResolveIdAsync(option.Url, config, token);
        var task = CreateTask(id, option.Url);
        var claimed = running.GetOrAdd(id, task);
        if (!ReferenceEquals(claimed, task))
        {
            // 重复提交同资源：新建任务的白费掉，其 linked CTS 必须释放，否则重复请求会累积泄漏
            task.Cts.Dispose( );
            return new EnqueueResult(claimed, true, false);
        }

        // 交互开启时任务自受理起持有事件上下文。注册先于入队：任务被立即消费并收尾时
        // ReleaseContext 也能命中，避免上下文在收尾之后才写入造成僵尸条目
        ChannelWorkflowContext? ctx = null;
        if (interactive)
        {
            ctx = new ChannelWorkflowContext( );
            contexts[id] = ctx;
        }

        if (!queue.Writer.TryWrite(new TaskEnvelope(task, option, req.CallBackWebHook)))
        {
            // 入队失败回滚：任务尚未执行，从运行表与上下文表移除并释放取消源
            running.TryRemove(id, out _);
            if (ctx is not null)
            {
                contexts.TryRemove(id, out _);
            }

            task.Cts.Dispose( );
            return new EnqueueResult(null, false, true);
        }

        return new EnqueueResult(task, false, false);
    }

    /// <summary>
    /// 取任务的事件上下文；交互未开启或任务已结束为 null。
    /// </summary>
    public ChannelWorkflowContext? GetContext(ResourceId id)
    {
        return contexts.GetValueOrDefault(id);
    }

    /// <summary>
    /// 任务结束收尾：移除事件上下文并取消该任务的挂起提问，返回被移除的上下文。
    /// </summary>
    public ChannelWorkflowContext? ReleaseContext(ResourceId id)
    {
        if (!contexts.TryRemove(id, out var ctx))
        {
            return null;
        }

        AskBus.CancelPending(id.ToString( ));
        return ctx;
    }

    /// <summary>
    /// 新建任务：受理即 Queued，TaskWorker 取得执行权后转 Running。
    /// </summary>
    public static DownloadTask CreateTask(ResourceId id, string url)
    {
        return new(id, url, DateTimeOffset.Now.ToUnixTimeMilliseconds( ))
        {
            Status = DownloadStatus.Queued,
        };
    }

    /// <summary>
    /// 按规范 id 查任务（运行中优先，其次已完成）。
    /// </summary>
    public DownloadTask? Get(ResourceId id)
    {
        return running.TryGetValue(id, out var task) || finished.TryGetValue(id, out task) ? task : null;
    }

    /// <summary>
    /// 取消运行中任务（经任务级 Cts，不影响其他任务），返回是否命中。
    /// </summary>
    public bool CancelRunning(ResourceId id)
    {
        if (!running.TryGetValue(id, out var task))
        {
            return false;
        }

        task.Cts.Cancel( );
        return true;
    }

    public List<DownloadTask> RunningSnapshot( )
    {
        return [.. running.Values];
    }

    public List<DownloadTask> FinishedSnapshot( )
    {
        return [.. finished.Values];
    }

    public void ClearFinished( )
    {
        finished.Clear( );
    }

    /// <summary>
    /// 仅清已失败（IsSuccessful == false）的已完成任务。
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
    }

    public void RemoveFinished(ResourceId id)
    {
        finished.TryRemove(id, out _);
    }

    /// <summary>
    /// 任务结束收尾：运行表移除、写入完成表并裁剪最旧条目。
    /// </summary>
    public void MoveToFinished(DownloadTask task)
    {
        running.TryRemove(task.Id, out _);
        finished[task.Id] = task;
        TrimFinishedTasks( );
    }

    // 已完成任务无上限增长会造成内存泄漏，超过阈值后按完成时间淘汰最旧的（P1-18）
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

    // serve 模式的工作目录由启动参数 --work-dir 决定，覆盖请求体（请求体根本不含该字段），
    // 这样客户端无法把落盘位置指向任意目录（P0-2 / P1-16）
    internal DownloadRequest ApplyServeWorkDir(DownloadRequest option)
    {
        if (!string.IsNullOrEmpty(workDir))
        {
            return option with { WorkDir = workDir };
        }

        return option;
    }

    // serve 模式的 API host 由启动参数（--host/--ep-host/--tv-host）决定，覆盖请求体（请求体已不含该字段），
    // 客户端无法把请求导向自己控制的服务器、从而窃走操作者的 SESSDATA（P0-1）。空值回落官方默认 host。
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
/// 受理结果：Duplicate 表示命中已有任务（携带已有任务），QueueFull 表示队列写满。
/// </summary>
internal sealed record EnqueueResult(DownloadTask? Task, bool Duplicate, bool QueueFull);
