using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace BBDown.Core.Workflow;

/// <summary>
/// 工作流通信的通用实现：可靠事件（消息 / 选项）走有界队列，进度经 ProgressBus 全局通道。
/// 高频样本不占队列、低频事件不丢失。CLI 消费循环与 serve 事件转发共用同一读取路径。
/// </summary>
public sealed class ChannelWorkflowContext : IWorkflowContext
{
    // 可靠事件队列：低频不丢事件；写满时 EnqueueMessage 降级丢弃，不阻塞下载链路
    private readonly Channel<WorkflowEvent> reliable = Channel.CreateBounded<WorkflowEvent>(new BoundedChannelOptions(1024)
    {
        FullMode = BoundedChannelFullMode.Wait,
        SingleReader = false,
        SingleWriter = false,
    });
    private readonly ConcurrentDictionary<Guid, PendingChoice> pendingChoices = new( );
    private readonly TimeSpan askTimeout;

    public ChannelWorkflowContext(TimeSpan? askTimeout = null)
    {
        this.askTimeout = askTimeout ?? TimeSpan.FromMinutes(5);
    }

    /// <summary>
    /// 读取可靠事件序列（消息 / 选项）；进度状态请读 <see cref="ProgressBus.Latest"/>。
    /// </summary>
    public IAsyncEnumerable<WorkflowEvent> ReadAllAsync(CancellationToken token)
    {
        return reliable.Reader.ReadAllAsync(token);
    }

    /// <summary>
    /// 应答选项请求：校验选项属于请求集合后完成挂起的 <see cref="AskOptionAsync"/>。
    /// 返回 false 表示请求不存在、已应答或选项非法。
    /// </summary>
    public bool SubmitChoice(Guid requestId, string choice)
    {
        if (!pendingChoices.TryGetValue(requestId, out var pending))
        {
            return false;
        }

        if (!pending.Options.Contains(choice, StringComparer.Ordinal))
        {
            return false;
        }

        if (pendingChoices.TryRemove(requestId, out _))
        {
            return pending.Tcs.TrySetResult(choice);
        }

        return false;
    }

    /// <summary>
    /// 把全部挂起选项转取消（任务停止 / 关服时调用），挂起的下载链路随之退出。
    /// </summary>
    public void CancelPendingChoices( )
    {
        foreach (var (id, pending) in pendingChoices)
        {
            if (pendingChoices.TryRemove(id, out _))
            {
                pending.Tcs.TrySetCanceled( );
            }
        }
    }

    /// <summary>
    /// 把一条消息送进本任务的事件流。写满即降级丢弃：消息属低频事件，通道写满只发生在消费端停滞时，
    /// 不阻塞下载链路。
    /// </summary>
    public void EnqueueMessage(string text, DateTimeOffset time)
    {
        if (!reliable.Writer.TryWrite(new MessageEvent(text, time)))
        {
            Logger.LogDebug("工作流消息通道已满，丢弃消息：{0}", text);
        }
    }

    /// <summary>
    /// 把任意工作流事件（如进度阶段边界）送进本任务的事件队列；写满降级丢弃，不阻塞下载链路。
    /// </summary>
    public void EnqueueEvent(WorkflowEvent evt)
    {
        if (!reliable.Writer.TryWrite(evt))
        {
            Logger.LogDebug("工作流事件通道已满，丢弃事件：{0}", evt.GetType( ).Name);
        }
    }

    /// <summary>
    /// 询问选项：挂起直到外部应答、超时或取消，返回所选选项文本。
    /// </summary>
    public async Task<string> AskOptionAsync(string prompt, IReadOnlyList<string> options, CancellationToken token = default)
    {
        var id = Guid.NewGuid( );
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        string[] optionsArray = [.. options];
        var deadline = DateTimeOffset.Now + askTimeout;
        pendingChoices[id] = new PendingChoice(tcs, optionsArray);
        try
        {
            await reliable.Writer.WriteAsync(new OptionRequestEvent(id, prompt, optionsArray, deadline), token);
            // 挂起等待外部应答；askTimeout 由服务端统一裁决，token 取消贯通
            return await tcs.Task.WaitAsync(askTimeout, token);
        }
        finally
        {
            pendingChoices.TryRemove(id, out _);
        }
    }
}

internal sealed record PendingChoice(TaskCompletionSource<string> Tcs, string[] Options);
