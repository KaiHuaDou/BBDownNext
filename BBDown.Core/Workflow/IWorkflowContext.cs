using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BBDown.Core.Workflow;

/// <summary>
/// 下载链路向宿主回吐消息 / 交互的统一出口：CLI 控制台、serve WebSocket、GUI 各自实现消费端。
/// 文本消息统一走 <see cref="BBDown.Core.Logging.MessageBus"/>（日志）与 <see cref="EnqueueMessage"/>（任务消息），
/// 进度统一走 <see cref="ProgressBus"/>——Core 只产生，展示由宿主决定。
/// </summary>
public interface IWorkflowContext
{
    /// <summary>
    /// 把一条消息送进本任务的事件流（serve 桥接与下载链路任务消息的队列入口）。
    /// </summary>
    void EnqueueMessage(string text, DateTimeOffset time);

    /// <summary>
    /// 询问选项：挂起工作流直到外部应答、超时或取消，返回所选选项文本。
    /// </summary>
    Task<string> AskOptionAsync(string prompt, IReadOnlyList<string> options, CancellationToken token = default);
}
