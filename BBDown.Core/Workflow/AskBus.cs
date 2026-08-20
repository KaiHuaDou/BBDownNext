using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core.Logging;

namespace BBDown.Core.Workflow;

/// <summary>
/// 交互总线：下载链路的提问（逐集确认 / 选轨）经总线发布，宿主订阅决定展示与应答。
/// 与 MessageBus（消息）/ ProgressBus（进度）同构：Core 只产生提问，应答由宿主提供。
/// Scope 复用 MessageBus 作用域；无订阅者时 Ask 立即回落 null（同无控制台进程的回落语义）。
/// </summary>
public static class AskBus
{
    private static Action<OptionRequestEvent>? handlers;
    private static readonly ConcurrentDictionary<Guid, PendingAsk> pending = new( );
    private static readonly TimeSpan AskTimeout = TimeSpan.FromMinutes(5);

    public static void Subscribe(Action<OptionRequestEvent> handler)
    {
        handlers += handler;
    }

    public static void Unsubscribe(Action<OptionRequestEvent> handler)
    {
        handlers -= handler;
    }

    /// <summary>
    /// 提问并挂起直到应答 / 超时 / 取消。返回 null 表示宿主不支持交互（无订阅者立即回落，同现状 ReadLine null）。
    /// defaultOptionId 为宿主无法解析输入时的回落选项（CLI 回车 / 非法输入），须属于 options。
    /// </summary>
    public static async Task<AskAnswer?> Ask(string prompt, IReadOnlyList<AskOption> options, string? defaultOptionId = null, CancellationToken token = default)
    {
        if (handlers is null)
        {
            return null;
        }

        var requestId = Guid.NewGuid( );
        var evt = new OptionRequestEvent(requestId, MessageBus.CurrentScope ?? "", prompt, options, DateTimeOffset.Now + AskTimeout, defaultOptionId);
        var ask = new PendingAsk(evt.Scope, options, new TaskCompletionSource<AskAnswer?>(TaskCreationOptions.RunContinuationsAsynchronously));
        pending[requestId] = ask;
        // 上方已判空：订阅者进程级生命周期，Ask 挂起期间不退订，直接调用
        handlers(evt);
        try
        {
            return await ask.Tcs.Task.WaitAsync(AskTimeout, token);
        }
        catch (TimeoutException)
        {
            // 无宿主应答（订阅者不响应）：按不交互回落
            return null;
        }
        finally
        {
            pending.TryRemove(requestId, out _);
        }
    }

    /// <summary>
    /// 应答选项请求：校验 OptionId 属于请求选项集合；返回 false 表示请求不存在 / 已应答 / 选项非法。
    /// </summary>
    public static bool Answer(Guid requestId, AskAnswer answer)
    {
        if (!pending.TryGetValue(requestId, out var ask) || !ask.Options.Any(o => o.Id == answer.OptionId))
        {
            return false;
        }

        return ask.Tcs.TrySetResult(answer);
    }

    /// <summary>按 scope 取消全部挂起提问（任务停止 / 关服时调用），挂起的下载链路随之退出。</summary>
    public static void CancelPending(string scope)
    {
        foreach (var (id, ask) in pending)
        {
            if (ask.Scope == scope && pending.TryRemove(id, out _))
            {
                ask.Tcs.TrySetCanceled( );
            }
        }
    }
}

internal sealed class PendingAsk(string scope, IReadOnlyList<AskOption> options, TaskCompletionSource<AskAnswer?> tcs)
{
    public string Scope { get; } = scope;
    public IReadOnlyList<AskOption> Options { get; } = options;
    public TaskCompletionSource<AskAnswer?> Tcs { get; } = tcs;
}
