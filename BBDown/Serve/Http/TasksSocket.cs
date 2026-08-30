using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

using BBDown.Core;
using BBDown.Core.Workflow;
using BBDown.Serve.Tasks;

namespace BBDown.Serve.Http;

/// <summary>
/// 任务事件 WebSocket 通道（/hubs/tasks）：订阅任务后接收消息 / 进度快照 / 选项请求，
/// 经 submitChoice 帧应答选项。帧协议见 API.md「WebSocket 事件流」。
/// </summary>
internal sealed class TaskSocketHub(TaskStore store)
{
    private const int MaxConnectionsPerIp = 5;
    private const int MaxFrameBytes = 64 * 1024;
    private static readonly TimeSpan SnapshotInterval = TimeSpan.FromMilliseconds(200);

    // 订阅表：任务 → 订阅该任务的连接；转发循环在表非空时存在（见 ForwardAsync）
    private readonly ConcurrentDictionary<ResourceId, ConcurrentDictionary<WebSocket, byte>> subscriptions = new( );
    private readonly ConcurrentDictionary<ResourceId, CancellationTokenSource> forwarders = new( );
    // 每连接发送锁：广播与回执帧可能并发写同一连接，WebSocket 不保证并发写安全
    private readonly ConcurrentDictionary<WebSocket, SemaphoreSlim> socketGates = new( );
    private readonly ConcurrentDictionary<string, int> connections = new( );
    private readonly TaskStore store = store;
    // 全局连接表：所有已建立 WS 的连接，用于广播任务列表（taskList）帧，与按任务订阅的表分离
    private readonly ConcurrentDictionary<WebSocket, byte> allSockets = new( );
    // 后台泵启动标记：首次连接建立时启动一次，订阅 store 变更通道向所有连接广播列表帧
    private int pumpStarted;

    /// <summary>
    /// 每 IP 并发连接上限判定（升级前调用，超限返回 false 由端点回 429）。
    /// </summary>
    public bool TryEnter(string? ip)
    {
        if (ip is null)
        {
            return true;
        }

        var count = connections.AddOrUpdate(ip, 1, (_, v) => v + 1);
        if (count <= MaxConnectionsPerIp)
        {
            return true;
        }

        // 超限回滚计数，避免拒绝的握手残留配额
        connections.AddOrUpdate(ip, count, (_, v) => Math.Max(0, v - 1));
        return false;
    }

    public void Leave(string? ip)
    {
        if (ip is null)
        {
            return;
        }

        connections.AddOrUpdate(ip, 0, (_, v) => Math.Max(0, v - 1));
    }

    /// <summary>
    /// 连接主循环：读客户端帧（subscribe / unsubscribe / submitChoice / ping），
    /// 连接关闭时清理其全部订阅并停止空闲转发。
    /// </summary>
    public async Task HandleAsync(WebSocket socket, CancellationToken token)
    {
        allSockets[socket] = 0;
        EnsurePump();
        try
        {
            // 连接建立即推送当前全量列表，前端无需先轮询即可渲染（事件流初始同步）
            await SendAsync(socket, TaskListFrame( ), token);
            await ReceiveLoopAsync(socket, token);
        }
        finally
        {
            allSockets.TryRemove(socket, out _);
            RemoveAllSubscriptions(socket);
            socketGates.TryRemove(socket, out var gate);
            gate?.Dispose( );
        }
    }

    private async Task ReceiveLoopAsync(WebSocket socket, CancellationToken token)
    {
        var buffer = new byte[MaxFrameBytes];
        while (socket.State == WebSocketState.Open)
        {
            var json = await ReceiveFrameAsync(socket, buffer, token);
            if (json is null)
            {
                break;   // 关闭帧或帧超限
            }

            ClientFrame? frame;
            try
            {
                frame = JsonSerializer.Deserialize<ClientFrame>(json, ServeFramesJsonSerializerContext.Default.ClientFrame);
            }
            catch (JsonException)
            {
                // 非法 JSON 只影响单帧：发错误帧并继续读，不终止整条连接
                await SendAsync(socket, new EventFrame("error", Error: "帧格式无效"), token);
                continue;
            }

            if (frame is null || frame.Kind is null)
            {
                continue;
            }

            switch (frame.Kind)
            {
                case "subscribe":
                    await SubscribeAsync(socket, frame.TaskId, token);
                    break;
                case "unsubscribe":
                    Unsubscribe(socket, frame.TaskId);
                    break;
                case "submitChoice":
                    await SubmitChoiceAsync(socket, frame, token);
                    break;
                case "ping":
                    break;
            }
        }
    }

    // 读一帧：分片累加，超 MaxFrameBytes 判非法关闭；Close 帧返回 null
    private static async Task<string?> ReceiveFrameAsync(WebSocket socket, byte[] buffer, CancellationToken token)
    {
        await using var memory = new MemoryStream( );
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(buffer, token);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            if (memory.Length + result.Count > MaxFrameBytes)
            {
                return null;
            }

            memory.Write(buffer, 0, result.Count);
        }
        while (!result.EndOfMessage);

        return Encoding.UTF8.GetString(memory.ToArray( ));
    }

    private async Task SubscribeAsync(WebSocket socket, string? taskId, CancellationToken token)
    {
        // taskId 统一先转 scope（规范 id 经 TryParse 还原后取 Format；record ToString 形式兜底直配），
        // 再按字符串匹配任务
        var scope = ResolveScope(taskId);
        // 任务已结束（含收尾窗口内）不提供订阅：事件流只覆盖任务执行期间，结束后的残留事件无意义
        if (scope is null || store.GetByScope(scope) is not { } task
            || task.Status == DownloadStatus.Finished || store.GetContext(scope) is not { } ctx)
        {
            await SendAsync(socket, new EventFrame("error", Error: "任务不存在或已结束"), token);
            return;
        }

        var set = subscriptions.GetOrAdd(task.Id, _ => new ConcurrentDictionary<WebSocket, byte>( ));
        set[socket] = 0;
        StartForwarder(task.Id, ctx);
        // 快照恢复：订阅时先推一次当前进度样本
        if (ProgressBus.Latest(scope)?.Sample is { } snapshot)
        {
            await SendAsync(socket, new EventFrame("snapshot", TaskId: ResourceIdJsonConverter.Format(task.Id), Snapshot: snapshot), token);
        }
    }

    // 任务标识 → scope（ResourceIdJsonConverter.Format 规范串）：规范 id 经 TryParse 还原后取 Format，
    // 与 /get-tasks 返回的 id 形态一致；record 形态原样兜底。opus 等多值 id 经 Format 归一，避免订阅失效
    private static string? ResolveScope(string? taskId)
    {
        if (taskId is null)
        {
            return null;
        }

        return ResourceId.TryParse(taskId, out var id) ? ResourceIdJsonConverter.Format(id) : taskId;
    }

    private void Unsubscribe(WebSocket socket, string? taskId)
    {
        // 与订阅同源解析：统一转 scope 后按字符串查任务
        if (ResolveScope(taskId) is { } scope && store.GetByScope(scope) is { } task)
        {
            RemoveSubscription(socket, task.Id);
        }
    }

    private void StartForwarder(ResourceId id, ChannelWorkflowContext ctx)
    {
        var tokenSource = new CancellationTokenSource( );
        if (!forwarders.TryAdd(id, tokenSource))
        {
            tokenSource.Dispose( );
            return;
        }

        _ = ForwardAsync(id, ctx, tokenSource.Token);
    }

    // 转发循环：可靠事件即时推，进度按周期帧推（仅变化时）；无订阅者时结束并允许下次订阅重启
    private async Task ForwardAsync(ResourceId id, ChannelWorkflowContext ctx, CancellationToken token)
    {
        try
        {
            var outgoing = Channel.CreateUnbounded<EventFrame>( );
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token);
            var sender = Task.Run(( ) => SendLoopAsync(id, outgoing.Reader, linked.Token), linked.Token);
            var events = Task.Run(( ) => ForwardEventsAsync(id, ctx, outgoing.Writer, linked.Token), linked.Token);
            var snapshots = Task.Run(( ) => ForwardSnapshotsAsync(id, outgoing.Writer, linked.Token), linked.Token);
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
            forwarders.TryRemove(id, out _);
        }
    }

    private static async Task ForwardEventsAsync(ResourceId id, ChannelWorkflowContext ctx, ChannelWriter<EventFrame> writer, CancellationToken token)
    {
        await foreach (var evt in ctx.ReadAllAsync(token))
        {
            await writer.WriteAsync(new EventFrame("event", TaskId: ResourceIdJsonConverter.Format(id), Event: evt), token);
        }
    }

    // 快照轮询：仅阶段内样本引用变化时推帧（ProgressBus 阶段内复用同一 ProgressState 实例，
    // 每次 Publish 生成新 ProgressSampleEvent；按样本引用比较才能捕捉变化，按 state 比较会漏推）
    private static async Task ForwardSnapshotsAsync(ResourceId id, ChannelWriter<EventFrame> writer, CancellationToken token)
    {
        ProgressSampleEvent? last = null;
        while (!token.IsCancellationRequested)
        {
            await Task.Delay(SnapshotInterval, token);
            var state = ProgressBus.Latest(ResourceIdJsonConverter.Format(id));
            if (state?.Sample is { } sample && !ReferenceEquals(sample, last))
            {
                last = sample;
                await writer.WriteAsync(new EventFrame("snapshot", TaskId: ResourceIdJsonConverter.Format(id), Snapshot: sample), token);
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

    private async Task SubmitChoiceAsync(WebSocket socket, ClientFrame frame, CancellationToken token)
    {
        if (frame.TaskId is null || frame.RequestId is null || frame.Choice is null
            || ResolveScope(frame.TaskId) is not { } scope || store.GetContext(scope) is not { })
        {
            await SendAsync(socket, new EventFrame("choiceResult", RequestId: frame.RequestId, Ok: false, Error: "任务不存在"), token);
            return;
        }

        var ok = AskBus.Answer(frame.RequestId.Value, new AskAnswer(frame.Choice));
        await SendAsync(socket, new EventFrame("choiceResult", RequestId: frame.RequestId, Ok: ok, Error: ok ? null : "选项非法或已应答"), token);
    }

    private async Task SendAsync(WebSocket socket, EventFrame frame, CancellationToken token)
    {
        var gate = socketGates.GetOrAdd(socket, _ => new SemaphoreSlim(1, 1));
        var json = JsonSerializer.Serialize(frame, ServeFramesJsonSerializerContext.Default.EventFrame);
        await gate.WaitAsync(token);
        try
        {
            await socket.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, token);
        }
        finally
        {
            gate.Release( );
        }
    }

    // 当前全量列表帧：running + finished，供连接建立时初始同步与每次结构变更广播
    private EventFrame TaskListFrame( )
    {
        return new EventFrame("taskList", Tasks: new DownloadTaskSnapshot(store.RunningSnapshot( ), store.FinishedSnapshot( )));
    }

    // 后台泵仅启动一次（单例生命周期内）：首次连接建立时触发，避免多连接重复开泵。
    // 连接建立前发生的结构变更已缓存在 store 的变更通道里，泵启动时一并重放，不丢变更。
    private void EnsurePump()
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

    /// <summary>
    /// 握手 Origin 校验：无 Origin（非浏览器客户端）放行；等于 --cors-origin 放行；回环来源放行；其余拒绝（CSWSH）。
    /// </summary>
    internal static bool IsAllowedOrigin(string? origin, ServeConfig config)
    {
        if (string.IsNullOrEmpty(origin))
        {
            return true;
        }

        if (string.Equals(origin, config.CorsOrigin, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return Uri.TryCreate(origin, UriKind.Absolute, out var uri)
               && (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                   || (IPAddress.TryParse(uri.Host, out var ip) && IPAddress.IsLoopback(ip)));
    }
}

/// <summary>
/// 客户端 → 服务端帧：kind 为 subscribe / unsubscribe / submitChoice / ping。
/// </summary>
internal sealed record ClientFrame(string? Kind, string? TaskId, Guid? RequestId, string? Choice);

/// <summary>
/// 服务端 → 客户端帧：kind 为 event / snapshot / choiceResult / error / taskList。
/// taskList 携带全量任务列表（running + finished），由 store 结构变更时广播，使前端免轮询。
/// </summary>
internal sealed record EventFrame(
    string Kind,
    string? TaskId = null,
    WorkflowEvent? Event = null,
    ProgressSampleEvent? Snapshot = null,
    Guid? RequestId = null,
    bool Ok = false,
    string? Error = null,
    DownloadTaskSnapshot? Tasks = null);
