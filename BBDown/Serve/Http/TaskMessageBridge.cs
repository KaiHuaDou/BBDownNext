using BBDown.Core.Logging;
using BBDown.Core.Workflow;
using BBDown.Serve.Tasks;

namespace BBDown.Serve.Http;

/// <summary>
/// 消息 / 进度 / 交互桥接：订阅 MessageBus（日志消息）、ProgressBus（进度阶段边界）与
/// AskBus（选项请求），把 Scope（任务 id）匹配的消息 / 事件送进对应任务的事件流（WebSocket）。
/// 无 Scope（CLI 路径）或任务无事件上下文（未启用交互）时忽略。宿主生命周期与 serve 进程一致。
/// </summary>
internal sealed class TaskMessageBridge
{
    private readonly TaskStore store;

    public TaskMessageBridge(TaskStore store)
    {
        this.store = store;
        MessageBus.Subscribe(OnMessage);
        ProgressBus.Subscribe(OnProgress);
        AskBus.Subscribe(OnAsk);
    }

    private void OnMessage(LogMessage message)
    {
        // scope 即任务标识字符串（ResourceIdJsonConverter.Format 规范串），直接命中上下文表，不经解析
        if (message.Scope is null)
        {
            return;
        }

        store.GetContext(message.Scope)?.EnqueueMessage(message.Text, message.Time);
    }

    // 阶段边界（低频）入事件队列；阶段内样本高频不进队列——快照由 ProgressBus.Latest 承载，
    // 由事件转发器周期读取推送 snapshot 帧
    private void OnProgress(WorkflowEvent evt)
    {
        if (evt is not (ProgressRangeStartEvent or ProgressRangeEndEvent))
        {
            return;
        }

        var scope = evt switch
        {
            ProgressRangeStartEvent start => start.Scope,
            ProgressRangeEndEvent end => end.Scope,
            _ => "",
        };
        if (store.GetContext(scope) is { } ctx)
        {
            ctx.EnqueueEvent(evt);
        }
    }

    // 选项请求（低频）入事件队列，应答经 submitChoice 帧回 AskBus（TaskSocketHub）
    private void OnAsk(OptionRequestEvent evt)
    {
        if (store.GetContext(evt.Scope) is { } ctx)
        {
            ctx.EnqueueEvent(evt);
        }
    }
}
