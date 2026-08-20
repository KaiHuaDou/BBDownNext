using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;

namespace BBDown.Core.Workflow;

/// <summary>
/// 工作流事件队列：消息 / 进度阶段边界 / 选项请求经有界队列承载，serve 事件转发读取。
/// 高频进度样本不占队列（走 ProgressBus 快照），低频事件不丢失。
/// </summary>
public sealed class ChannelWorkflowContext
{
    // 可靠事件队列：低频不丢事件；写满时降级丢弃，不阻塞下载链路
    private readonly Channel<WorkflowEvent> reliable = Channel.CreateBounded<WorkflowEvent>(new BoundedChannelOptions(1024)
    {
        FullMode = BoundedChannelFullMode.Wait,
        SingleReader = false,
        SingleWriter = false,
    });

    /// <summary>
    /// 读取可靠事件序列（消息 / 阶段边界 / 选项请求）；进度状态请读 <see cref="ProgressBus.Latest"/>。
    /// </summary>
    public IAsyncEnumerable<WorkflowEvent> ReadAllAsync(CancellationToken token)
    {
        return reliable.Reader.ReadAllAsync(token);
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
    /// 把任意工作流事件（进度阶段边界 / 选项请求）送进本任务的事件队列；写满降级丢弃，不阻塞下载链路。
    /// </summary>
    public void EnqueueEvent(WorkflowEvent evt)
    {
        if (!reliable.Writer.TryWrite(evt))
        {
            Logger.LogDebug("工作流事件通道已满，丢弃事件：{0}", evt.GetType( ).Name);
        }
    }
}
