using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

using BBDown.Core;
using BBDown.Core.Workflow;

namespace BBDown.Serve.Http;

/// <summary>
/// TaskSocketHub 的转发与广播部分：按任务转发事件 / 快照帧、结构变更全局广播（taskList）、订阅清理
/// 连接生命周期与帧收发见 TasksSocket.cs
/// </summary>
internal sealed partial class TaskSocketHub
{
    private static readonly TimeSpan SnapshotInterval = TimeSpan.FromMilliseconds(200);
    // 转发队列有界：停止读取的订阅者原本能让生产通道无界增长，单个停滞客户端也会拖住同任务的其他订阅者。
    // 满了丢最旧的进度帧——日志与选项请求不能丢，它们走同一队列，故按整帧丢最旧
    private const int OutgoingCapacity = 256;

    // 把连接挂到任务的订阅槽位，并确保转发循环已启动。挂载期间槽位被别人摘掉（最后一个订阅者刚离开）时重来一次
    private void Attach(WebSocket socket, DownloadTask task, ChannelWorkflowContext ctx)
    {
        while (true)
        {
            var subscription = subscriptions.GetOrAdd(task.Id, _ => new TaskSubscription( ));
            subscription.Sockets[socket] = 0;
            if (!subscriptions.TryGetValue(task.Id, out var current) || !ReferenceEquals(current, subscription))
            {
                subscription.Sockets.TryRemove(socket, out _);
                continue;
            }

            if (subscription.TryStart( ))
            {
                _ = ForwardAsync(task, ctx, subscription);
            }

            return;
        }
    }

    // 转发循环：可靠事件即时推，进度按周期帧推（仅变化时）；无订阅者时由 RemoveSubscription 取消
    private async Task ForwardAsync(DownloadTask task, ChannelWorkflowContext ctx, TaskSubscription subscription)
    {
        var token = subscription.Forwarder.Token;
        try
        {
            var outgoing = Channel.CreateBounded<EventFrame>(new BoundedChannelOptions(OutgoingCapacity) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
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
    }

    private static async Task ForwardEventsAsync(string scope, ChannelWorkflowContext ctx, ChannelWriter<EventFrame> writer, CancellationToken token)
    {
        await foreach (var evt in ctx.ReadAllAsync(token))
        {
            await writer.WriteAsync(new EventFrame("event", TaskId: scope, Event: evt), token);
        }
    }

    // 快照轮询：仅阶段内样本引用变化时推帧（ProgressBus 阶段内复用同一 ProgressState 实例
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
        if (!subscriptions.TryGetValue(id, out var subscription))
        {
            return;
        }

        foreach (var (socket, _) in subscription.Sockets)
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

    // 后台泵仅启动一次（单例生命周期内）：首次连接建立时触发，避免多连接重复开泵
    // 连接建立前发生的结构变更已缓存在 store 的变更通道里，泵启动时一并重放，不丢变更
    private void EnsurePump( )
    {
        if (Interlocked.Exchange(ref pumpStarted, 1) == 0)
        {
            _ = PumpStoreChangesAsync(AppEnv.CancellationToken);
        }
    }

    // 订阅 store 变更通道：每次结构变更（增删 / 状态切换 / 完成 / 清空）向所有连接广播最新列表
    // 进度不在此列（由按任务的 snapshot 帧高频推送），故列表帧仅在结构变化时发送，开销极低
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
        if (!subscriptions.TryGetValue(id, out var subscription))
        {
            return;
        }

        subscription.Sockets.TryRemove(socket, out _);
        if (!subscription.Sockets.IsEmpty)
        {
            return;
        }

        // 最后订阅者离开：整个槽位摘除并停转发循环。值比较的原子移除保证只摘自己看到的那个实例，
        // 不会删掉后来者新建的槽位
        if (((ICollection<KeyValuePair<ResourceId, TaskSubscription>>) subscriptions).Remove(new(id, subscription)))
        {
            subscription.Forwarder.Cancel( );
            subscription.Forwarder.Dispose( );
        }
    }

    private void RemoveAllSubscriptions(WebSocket socket)
    {
        foreach (var (id, subscription) in subscriptions)
        {
            if (subscription.Sockets.ContainsKey(socket))
            {
                RemoveSubscription(socket, id);
            }
        }
    }
}
