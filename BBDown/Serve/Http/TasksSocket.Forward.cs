using System;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

using BBDown.Core;
using BBDown.Core.Workflow;

namespace BBDown.Serve.Http;

/// <summary>
/// TaskSocketHub 的转发与广播部分：按任务转发事件 / 快照帧、结构变更全局广播（taskList）、订阅清理。
/// 连接生命周期与帧收发见 TasksSocket.cs。
/// </summary>
internal sealed partial class TaskSocketHub
{
    private static readonly TimeSpan SnapshotInterval = TimeSpan.FromMilliseconds(200);

    private void StartForwarder(DownloadTask task, ChannelWorkflowContext ctx)
    {
        var tokenSource = new CancellationTokenSource( );
        if (!forwarders.TryAdd(task.Id, tokenSource))
        {
            tokenSource.Dispose( );
            return;
        }

        _ = ForwardAsync(task, ctx, tokenSource.Token);
    }

    // 转发循环：可靠事件即时推，进度按周期帧推（仅变化时）；无订阅者时结束并允许下次订阅重启
    private async Task ForwardAsync(DownloadTask task, ChannelWorkflowContext ctx, CancellationToken token)
    {
        try
        {
            var outgoing = Channel.CreateUnbounded<EventFrame>( );
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token);
            var sender = Task.Run(( ) => SendLoopAsync(task.Id, outgoing.Reader, linked.Token), linked.Token);
            var events = Task.Run(( ) => ForwardEventsAsync(task.Scope, ctx, outgoing.Writer, linked.Token), linked.Token);
            var snapshots = Task.Run(( ) => ForwardSnapshotsAsync(task.Scope, outgoing.Writer, linked.Token), linked.Token);
            await Task.WhenAll(events, snapshots);
            linked.Cancel( );
            try
            {
                await sender;
            }
            catch (OperationCanceledException)
            {
            }
        }
        catch (OperationCanceledException)
        {
            // 订阅清空或关服导致的取消，转发结束属正常路径
        }
        finally
        {
            forwarders.TryRemove(task.Id, out _);
        }
    }

    private static async Task ForwardEventsAsync(string scope, ChannelWorkflowContext ctx, ChannelWriter<EventFrame> writer, CancellationToken token)
    {
        await foreach (var evt in ctx.ReadAllAsync(token))
        {
            await writer.WriteAsync(new EventFrame("event", TaskId: scope, Event: evt), token);
        }
    }

    // 快照轮询：仅阶段内样本引用变化时推帧（ProgressBus 阶段内复用同一 ProgressState 实例，
    // 每次 Publish 生成新 ProgressSampleEvent；按样本引用比较才能捕捉变化，按 state 比较会漏推）
    private static async Task ForwardSnapshotsAsync(string scope, ChannelWriter<EventFrame> writer, CancellationToken token)
    {
        ProgressSampleEvent? last = null;
        while (!token.IsCancellationRequested)
        {
            await Task.Delay(SnapshotInterval, token);
            var state = ProgressBus.Latest(scope);
            if (state?.Sample is { } sample && !ReferenceEquals(sample, last))
            {
                last = sample;
                await writer.WriteAsync(new EventFrame("snapshot", TaskId: scope, Snapshot: sample), token);
            }
        }
    }

    private async Task SendLoopAsync(ResourceId id, ChannelReader<EventFrame> reader, CancellationToken token)
    {
        await foreach (var frame in reader.ReadAllAsync(token))
        {
            await BroadcastAsync(id, frame, token);
        }
    }

    // 广播一帧给任务订阅者；发送失败的连接视为断开，移出订阅
    private async Task BroadcastAsync(ResourceId id, EventFrame frame, CancellationToken token)
    {
        if (!subscriptions.TryGetValue(id, out var set))
        {
            return;
        }

        foreach (var (socket, _) in set)
        {
            try
            {
                await SendAsync(socket, frame, token);
            }
            catch (Exception)
            {
                RemoveSubscription(socket, id);
            }
        }
    }

    // 当前全量列表帧：running + finished，供连接建立时初始同步与每次结构变更广播
    private EventFrame TaskListFrame( )
    {
        return new EventFrame("taskList", Tasks: new DownloadTaskSnapshot(store.RunningSnapshot( ), store.FinishedSnapshot( )));
    }

    // 后台泵仅启动一次（单例生命周期内）：首次连接建立时触发，避免多连接重复开泵。
    // 连接建立前发生的结构变更已缓存在 store 的变更通道里，泵启动时一并重放，不丢变更。
    private void EnsurePump( )
    {
        if (Interlocked.Exchange(ref pumpStarted, 1) == 0)
        {
            _ = PumpStoreChangesAsync(AppEnv.CancellationToken);
        }
    }

    // 订阅 store 变更通道：每次结构变更（增删 / 状态切换 / 完成 / 清空）向所有连接广播最新列表。
    // 进度不在此列（由按任务的 snapshot 帧高频推送），故列表帧仅在结构变化时发送，开销极低。
    private async Task PumpStoreChangesAsync(CancellationToken token)
    {
        try
        {
            await foreach (var _ in store.Changes.ReadAllAsync(token))
            {
                await BroadcastGlobalAsync(TaskListFrame( ), token);
            }
        }
        catch (OperationCanceledException)
        {
            // serve 关停导致的取消，泵结束属正常路径
        }
    }

    // 向所有连接广播一帧；发送失败的连接视为断开，移出全局表
    private async Task BroadcastGlobalAsync(EventFrame frame, CancellationToken token)
    {
        foreach (var (socket, _) in allSockets)
        {
            try
            {
                await SendAsync(socket, frame, token);
            }
            catch (Exception)
            {
                allSockets.TryRemove(socket, out _);
            }
        }
    }

    private void RemoveSubscription(WebSocket socket, ResourceId id)
    {
        if (!subscriptions.TryGetValue(id, out var set))
        {
            return;
        }

        set.TryRemove(socket, out _);
        if (!set.IsEmpty)
        {
            return;
        }

        // 最后订阅者离开：停转发循环
        subscriptions.TryRemove(id, out _);
        if (forwarders.TryRemove(id, out var tokenSource))
        {
            tokenSource.Cancel( );
            tokenSource.Dispose( );
        }
    }

    private void RemoveAllSubscriptions(WebSocket socket)
    {
        foreach (var (id, set) in subscriptions)
        {
            if (set.ContainsKey(socket))
            {
                RemoveSubscription(socket, id);
            }
        }
    }
}
