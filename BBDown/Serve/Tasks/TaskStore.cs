using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
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
    private const int MaxEnqueued = 100;

    private readonly ConcurrentDictionary<ResourceId, DownloadTask> running = new( );
    private readonly ConcurrentDictionary<ResourceId, DownloadTask> finished = new( );
    // enqueue（不立即执行）任务的执行信封暂存：start 时取出写入执行队列，故暂停态任务不占执行队列
    private readonly ConcurrentDictionary<ResourceId, TaskEnvelope> pending = new( );
    // 事件上下文按 scope（ResourceIdJsonConverter.Format 规范串）键存：规范串与 /get-tasks 返回的 id 形态一致，
    // 经 ResourceId.TryParse 可往返，避免 record ToString 与规范串不对称导致 opus 等任务订阅/交互失效
    private readonly ConcurrentDictionary<string, ChannelWorkflowContext> contexts = new( );
    private readonly string? workDir = config.WorkDir;
    private readonly string? host = config.Host;
    private readonly string? epHost = config.EpHost;
    private readonly string? tvHost = config.TvHost;
    private readonly TaskQueue queue = queue;

    /// <summary>
    /// 任务结构变更通知通道：任何 running / finished / pending 的增删改都写入一个标记项，
    /// 由 WebSocket Hub 后台读取并广播全量列表帧（taskList）。这样前端可放弃轮询、改为事件流推送。
    /// 单消费者（Hub 单例）读取，writer 用 TryWrite 保证变更点不抛。
    /// </summary>
    private readonly Channel<StoreChanged> changes = Channel.CreateUnbounded<StoreChanged>();
    public ChannelReader<StoreChanged> Changes => changes.Reader;
    internal readonly record struct StoreChanged;
    internal void NotifyChanged() => changes.Writer.TryWrite(default);

    /// <summary>
    /// 受理任务：解析 URL → 去重 → 按模式处理。
    /// Execute 直接写执行队列（受理即跑）；Enqueue 仅入暂停表、不写执行队列，待 <see cref="Start"/> 才执行。
    /// 命中已有任务返回 Duplicate（携带已有任务），执行队列写满返回 QueueFull，均由端点映射为对应状态码。
    /// </summary>
    public async Task<EnqueueResult> EnqueueAsync(ServeRequestOptions req, SubmitMode mode, CancellationToken token)
    {
        var option = ApplyServeHost(ApplyServeWorkDir(req.ToDownloadRequest( )));
        var config = WorkSetup.ResolveConfig(option, option.Api);
        // 解析阶段尚无任务级令牌（任务在解析成功后创建），用进程级令牌：服务器关停即可中断排队中的解析
        var id = await InputResolver.ResolveIdAsync(option.Url, config, token);
        var task = CreateTask(id, option.Url, mode == SubmitMode.Enqueue ? DownloadStatus.Pending : DownloadStatus.Queued);
        var claimed = running.GetOrAdd(id, task);
        if (!ReferenceEquals(claimed, task))
        {
            // 重复提交同资源：新建任务的白费掉，其 linked CTS 必须释放，否则重复请求会累积泄漏
            task.Cts.Dispose( );
            // enqueue 暂停态任务遇到执行模式提交：直接触发启动，避免被判 Duplicate 后永不执行
            if (claimed.Status == DownloadStatus.Pending)
            {
                return Start(id) switch
                {
                    StartResult.Started => new EnqueueResult(claimed, false, false),
                    StartResult.QueueFull => new EnqueueResult(null, false, true),
                    _ => new EnqueueResult(claimed, true, false)
                };
            }

            return new EnqueueResult(claimed, true, false);
        }

        // 任务自受理起即持有事件上下文（事件流始终启用）。注册先于入队：任务被立即消费并收尾时
        // ReleaseContext 也能命中，避免上下文在收尾之后才写入造成僵尸条目
        var ctx = new ChannelWorkflowContext( );
        contexts[ResourceIdJsonConverter.Format(task.Id)] = ctx;

        var envelope = new TaskEnvelope(task, option, req.CallBackWebHook);
        // Enqueue 模式：仅存入暂停表，不写执行队列（WebUI「加入队列不执行」）；start 时再取出投入。
        // 暂停表上限镜像执行队列，防止任务无限挂起累积事件上下文与取消源；超限回滚受理并返回 429
        if (mode == SubmitMode.Enqueue)
        {
            if (pending.Count >= MaxEnqueued)
            {
                running.TryRemove(id, out _);
                contexts.TryRemove(ResourceIdJsonConverter.Format(task.Id), out _);
                task.Cts.Dispose( );
                return new EnqueueResult(null, false, true);
            }

            pending[id] = envelope;
            NotifyChanged();
            return new EnqueueResult(task, false, false);
        }

        if (!queue.Writer.TryWrite(envelope))
        {
            // 入队失败回滚：任务尚未执行，从运行表、暂停表与上下文表移除并释放取消源
            pending.TryRemove(id, out _);
            running.TryRemove(id, out _);
            contexts.TryRemove(ResourceIdJsonConverter.Format(task.Id), out _);
            task.Cts.Dispose( );
            return new EnqueueResult(null, false, true);
        }

        NotifyChanged();
        return new EnqueueResult(task, false, false);
    }

    /// <summary>
    /// 启动一个 enqueue 暂停的任务：取出其执行信封写入执行队列。
    /// 不在暂停表（已运行 / 未知 / 已结束）返回 NotFound；执行队列写满返回 QueueFull（任务保留暂停态可重试）。
    /// </summary>
    public StartResult Start(ResourceId id)
    {
        if (!pending.TryRemove(id, out var envelope))
        {
            return StartResult.NotFound;
        }

        // 先置等待态再入队：channel 写建立先后序，worker 取到的必然是 Queued 之后的状态；
        // TryWrite 失败回退 Pending，任务保留暂停态可再次 start
        envelope.Task.Status = DownloadStatus.Queued;
        if (!queue.Writer.TryWrite(envelope))
        {
            pending[id] = envelope;
            envelope.Task.Status = DownloadStatus.Pending;
            return StartResult.QueueFull;
        }

        NotifyChanged();
        return StartResult.Started;
    }

    /// <summary>
    /// 取任务的事件上下文；交互未开启或任务已结束为 null。scope 为总线消息携带的任务标识
    /// （ResourceIdJsonConverter.Format 规范串）。
    /// </summary>
    public ChannelWorkflowContext? GetContext(string scope)
    {
        return contexts.GetValueOrDefault(scope);
    }

    /// <summary>
    /// 任务结束收尾：移除事件上下文并取消该任务的挂起提问，返回被移除的上下文。
    /// </summary>
    public ChannelWorkflowContext? ReleaseContext(string scope)
    {
        if (!contexts.TryRemove(scope, out var ctx))
        {
            return null;
        }

        AskBus.CancelPending(scope);
        return ctx;
    }

    /// <summary>
    /// 按作用域字符串（ResourceIdJsonConverter.Format 规范串）查任务，运行中优先；事件流帧 TaskId 回发订阅时命中。
    /// </summary>
    public DownloadTask? GetByScope(string scope)
    {
        foreach (var task in running.Values)
        {
            if (ResourceIdJsonConverter.Format(task.Id) == scope)
            {
                return task;
            }
        }

        foreach (var task in finished.Values)
        {
            if (ResourceIdJsonConverter.Format(task.Id) == scope)
            {
                return task;
            }
        }

        return null;
    }

    /// <summary>
    /// 新建任务：默认 Queued（受理即进入执行队列），Enqueue 模式传 Pending 表示暂停待启动。
    /// TaskWorker 取得执行权后转 Running。
    /// </summary>
    public static DownloadTask CreateTask(ResourceId id, string url, DownloadStatus initialStatus = DownloadStatus.Queued)
    {
        return new(id, url, DateTimeOffset.Now.ToUnixTimeMilliseconds( ))
        {
            Status = initialStatus,
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
        NotifyChanged();
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

        NotifyChanged();
    }

    /// <summary>
    /// 移除指定任务：已完成的直接清；enqueue 暂停态的从暂停表与运行表移除、释放取消源并清事件上下文。
    /// 运行中的任务（已投入执行队列）不在此处理，须先用 stop 端点取消。
    /// </summary>
    public void RemoveTask(ResourceId id)
    {
        finished.TryRemove(id, out _);
        if (pending.TryRemove(id, out var envelope))
        {
            running.TryRemove(id, out _);
            ReleaseContext(ResourceIdJsonConverter.Format(envelope.Task.Id));
            envelope.Task.Cts.Dispose( );
        }

        NotifyChanged();
    }

    /// <summary>
    /// 任务结束收尾：运行表移除、写入完成表并裁剪最旧条目。
    /// </summary>
    public void MoveToFinished(DownloadTask task)
    {
        running.TryRemove(task.Id, out _);
        finished[task.Id] = task;
        TrimFinishedTasks( );
        NotifyChanged();
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

/// <summary>
/// 任务受理模式：Execute 受理即写执行队列（等同旧 POST 行为）；Enqueue 仅入暂停表，待 Start 才执行。
/// </summary>
internal enum SubmitMode
{
    Execute,
    Enqueue,
}

/// <summary>
/// Start 结果：Started 已投入执行队列；NotFound 表示不在暂停表（已运行 / 未知 / 已结束）；QueueFull 表示执行队列写满。
/// </summary>
internal enum StartResult
{
    Started,
    NotFound,
    QueueFull,
}
