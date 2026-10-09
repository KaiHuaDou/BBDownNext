using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

using BBDown.Core;
using BBDown.Core.Download;
using BBDown.Core.Pipeline;
using BBDown.Core.Workflow;

namespace BBDown.Serve.Tasks;

/// <summary>
/// 任务受理与状态机：URL 与 ResourceId 两级去重、暂停表、执行队列投递、停止与启动
/// 两表维护与 serve 参数注入见 TaskStore.Table.cs
/// </summary>
internal sealed partial class TaskStore(ServeConfig config, ChannelWriter<TaskEnvelope> queueWriter)
{
    private const int MaxFinishedTasks = 200;
    private const int MaxEnqueued = 100;

    private readonly ConcurrentDictionary<ResourceId, DownloadTask> running = new( );
    private readonly ConcurrentDictionary<ResourceId, DownloadTask> finished = new( );
    // enqueue（不立即执行）任务的执行信封暂存：start 时取出写入执行队列，故暂停态任务不占执行队列
    private readonly ConcurrentDictionary<ResourceId, TaskEnvelope> pending = new( );
    // 暂停表的容量判定、增删、占位与入队都在这把锁内完成：
    // 先读 Count 再写是无原子性的，超限那一刻并发的受理会一并放行；
    // Start 的回填与 RemoveTask 竞争也会把已摘除的任务写回（取消源已释放，任务却复活）；
    // running 先占位、pending 后写入时，RemoveTask 会因暂停表里还没有条目而整体跳过，
    // 随后受理方把条目写进暂停表，得到一个 running 查不到、却能被 start 投入执行的孤儿任务
    private readonly Lock pendingGate = new( );
    // 事件上下文按 scope（ResourceId 规范串，见 DownloadTask.Scope）键存：规范串与 /get-tasks 返回的 id 相同
    // 经 ResourceId.TryParse 可往返，避免 record ToString 与规范串不对称导致 opus 等任务订阅/交互失效
    private readonly ConcurrentDictionary<string, ChannelWorkflowContext> contexts = new( );
    private readonly string? workDir = config.WorkDir;
    private readonly string? host = config.Host;
    private readonly string? epHost = config.EpHost;
    private readonly string? tvHost = config.TvHost;

    /// <summary>
    /// 任务结构变更通知通道：任何 running / finished / pending 的增删改都写入一个标记项
    /// 由 WebSocket Hub 后台读取并广播全量列表帧（taskList），前端经事件流感知任务列表、无需轮询
    /// 单消费者（Hub 单例）读取，writer 用 TryWrite 保证变更点不抛
    /// </summary>
    private readonly Channel<StoreChanged> changes = Channel.CreateUnbounded<StoreChanged>( );
    public ChannelReader<StoreChanged> Changes => changes.Reader;
    internal readonly record struct StoreChanged;
    internal void NotifyChanged( )
    {
        changes.Writer.TryWrite(default);
    }

    /// <summary>
    /// 受理任务：按原样 URL 去重 → 解析 → 占位并入队
    /// Execute 直接写执行队列（受理即跑）；Enqueue 仅入暂停表、不写执行队列，待 <see cref="Start"/> 才执行
    /// 命中已有任务返回 Duplicate（携带已有任务），执行队列写满返回 QueueFull，均由端点映射为对应状态码
    /// </summary>
    public async Task<EnqueueResult> EnqueueAsync(ServeRequestOptions req, SubmitMode mode, CancellationToken token)
    {
        var option = ApplyServeHost(ApplyServeWorkDir(req.ToDownloadRequest( )));
        var requestConfig = WorkSetup.ResolveConfig(option, option.Api);
        var requestedUrl = option.Url.Trim( );

        // 先按原样 URL 去重：解析可能展开 b23 短链、抓播放页、调 FixAvidAsync（数次外发 HTTPS），
        // 而重复提交最常见的形式正是原样重发同一地址，不该为它放大成数次对 B 站的请求
        if (FindByUrl(requestedUrl) is { } same)
        {
            // 拉起暂停态要动暂停表，与受理路径同处一把锁
            lock (pendingGate)
            {
                return Relaunch(same);
            }
        }

        // 解析阶段尚无任务级令牌（任务在解析成功后创建），用进程级令牌：服务器关停即可中断排队中的解析
        var id = await InputResolver.ResolveIdAsync(requestedUrl, requestConfig, token);
        var task = CreateTask(id, requestedUrl, mode == SubmitMode.Enqueue ? DownloadStatus.Pending : DownloadStatus.Queued);

        lock (pendingGate)
        {
            var claimed = running.GetOrAdd(id, task);
            if (!ReferenceEquals(claimed, task))
            {
                // 重复提交同资源：新建任务的白费掉，其 linked CTS 必须释放，否则重复请求会累积泄漏
                task.DisposeCts( );
                return Relaunch(claimed);
            }

            // 任务自受理起即持有事件上下文（事件流始终启用）。注册先于入队：任务被立即消费并收尾时
            // ReleaseContext 也能命中，避免上下文在收尾之后才写入造成僵尸条目
            contexts[task.Scope] = new ChannelWorkflowContext( );
            var envelope = new TaskEnvelope(task, option, req.CallBackWebHook);

            // Enqueue 模式：仅存入暂停表，不写执行队列（WebUI「加入队列不执行」）；start 时再取出投入
            // 暂停表上限镜像执行队列，防止任务无限挂起累积事件上下文与取消源；超限回滚受理并返回 429
            if (mode == SubmitMode.Enqueue)
            {
                if (pending.Count >= MaxEnqueued)
                {
                    RollbackLocked(id, task);
                    return new EnqueueResult(null, false, true);
                }

                pending[id] = envelope;
            }
            else if (!queueWriter.TryWrite(envelope))
            {
                // 入队失败回滚：任务尚未执行，从运行表与上下文表移除并释放取消源
                RollbackLocked(id, task);
                return new EnqueueResult(null, false, true);
            }
        }

        NotifyChanged( );
        return new EnqueueResult(task, false, false);
    }

    /// <summary>
    /// 命中已有任务时的处理：暂停态直接拉起（否则被判 Duplicate 后永不执行），
    /// 其余情况报 Duplicate
    /// </summary>
    private EnqueueResult Relaunch(DownloadTask claimed)
    {
        if (claimed.Status != DownloadStatus.Pending)
        {
            return new EnqueueResult(claimed, true, false);
        }

        return StartLocked(claimed.Id) switch
        {
            StartResult.Started => new EnqueueResult(claimed, false, false),
            StartResult.QueueFull => new EnqueueResult(null, false, true),
            _ => new EnqueueResult(claimed, true, false)
        };
    }

    // 回滚一次刚占位却未成功入队的任务。调用方必须持有 pendingGate
    private void RollbackLocked(ResourceId id, DownloadTask task)
    {
        running.TryRemove(id, out _);
        contexts.TryRemove(task.Scope, out _);
        task.DisposeCts( );
    }

    /// <summary>
    /// 按原始 URL 找任务（运行中优先，其次已完成）。URL 未规范化，只能挡住原样重发，
    /// 同一资源的不同写法（BV 号 / 短链 / 带 query 的地址）仍走解析后的 id 比对
    /// </summary>
    private DownloadTask? FindByUrl(string url)
    {
        foreach (var task in running.Values)
        {
            if (task.Url == url)
            {
                return task;
            }
        }

        foreach (var task in finished.Values)
        {
            if (task.Url == url)
            {
                return task;
            }
        }

        return null;
    }

    /// <summary>
    /// 启动一个 enqueue 暂停的任务：取出其执行信封写入执行队列
    /// 不在暂停表时按「已运行 / 已结束」与「不存在」分别返回 AlreadyStarted 与 NotFound；
    /// 执行队列写满返回 QueueFull（任务保留暂停态可重试）
    /// </summary>
    public StartResult Start(ResourceId id)
    {
        lock (pendingGate)
        {
            var result = StartLocked(id);
            if (result == StartResult.Started)
            {
                NotifyChanged( );
            }

            return result;
        }
    }

    // 取出、置态、入队与回填同处 pendingGate：与 RemoveTask 竞争时不会复活已移除的任务。调用方必须持有 pendingGate
    private StartResult StartLocked(ResourceId id)
    {
        if (!pending.TryRemove(id, out var envelope))
        {
            return Get(id) is null ? StartResult.NotFound : StartResult.AlreadyStarted;
        }

        // 先置等待态再入队：channel 写建立先后序，worker 取到的必然是 Queued 之后的状态
        // TryWrite 失败回退 Pending，任务保留暂停态可再次 start
        envelope.Task.Status = DownloadStatus.Queued;
        if (!queueWriter.TryWrite(envelope))
        {
            pending[id] = envelope;
            envelope.Task.Status = DownloadStatus.Pending;
            return StartResult.QueueFull;
        }

        return StartResult.Started;
    }

    /// <summary>
    /// 取任务的事件上下文；交互未开启或任务已结束为 null。scope 为总线消息携带的任务标识
    /// （ResourceId 规范串，见 DownloadTask.Scope）
    /// </summary>
    public ChannelWorkflowContext? GetContext(string scope)
    {
        return contexts.GetValueOrDefault(scope);
    }

    /// <summary>
    /// 任务结束收尾：移除事件上下文并取消该任务的挂起提问，返回被移除的上下文
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
    /// 按作用域字符串（ResourceId 规范串）查任务，运行中优先；事件流帧 TaskId 回发订阅时命中
    /// </summary>
    public DownloadTask? GetByScope(string scope)
    {
        foreach (var task in running.Values)
        {
            if (task.Scope == scope)
            {
                return task;
            }
        }

        foreach (var task in finished.Values)
        {
            if (task.Scope == scope)
            {
                return task;
            }
        }

        return null;
    }

    /// <summary>
    /// 新建任务：默认 Queued（受理即进入执行队列），Enqueue 模式传 Pending 表示暂停待启动
    /// TaskWorker 取得执行权后转 Running
    /// </summary>
    public static DownloadTask CreateTask(ResourceId id, string url, DownloadStatus initialStatus = DownloadStatus.Queued)
    {
        return new(id, url, DateTimeOffset.Now.ToUnixTimeMilliseconds( ))
        {
            Status = initialStatus,
        };
    }

    /// <summary>
    /// 按规范 id 查任务（运行中优先，其次已完成）
    /// </summary>
    public DownloadTask? Get(ResourceId id)
    {
        return running.TryGetValue(id, out var task) || finished.TryGetValue(id, out task) ? task : null;
    }

    /// <summary>
    /// 停止任务（经任务级取消源，不影响其他任务），返回是否命中
    /// enqueue 暂停态的任务尚未投入执行，取消它没有意义（worker 拿到已取消的令牌会立刻退出，
    /// 用户看到的是「已启动后秒取消」），故改为从暂停表与运行表移除，返回 Removed 表示已移除
    /// </summary>
    public StopResult Stop(ResourceId id)
    {
        lock (pendingGate)
        {
            if (pending.TryRemove(id, out var envelope))
            {
                running.TryRemove(id, out _);
                ReleaseContext(envelope.Task.Scope);
                envelope.Task.DisposeCts( );
                NotifyChanged( );
                return StopResult.Removed;
            }
        }

        if (!running.TryGetValue(id, out var task))
        {
            return StopResult.NotFound;
        }

        task.Cancel( );
        return StopResult.Cancelled;
    }
}
