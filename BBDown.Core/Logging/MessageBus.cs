using System;
using System.Threading;

namespace BBDown.Core.Logging;

/// <summary>
/// 业务消息总线：Core 内唯一消息发射点，无渲染、无锁、无 Console。
/// 宿主（CLI / GUI / serve）经 <see cref="Subscribe"/> 订阅消息并自行决定展示。
/// 订阅用委托组合而非 event，避免事件对 EventArgs 的约束（消息是值对象，非事件参数）。
/// </summary>
public static class MessageBus
{
    private static Action<LogMessage>? handlers;
    private static readonly AsyncLocal<string?> currentScope = new( );

    public static void Subscribe(Action<LogMessage> handler)
    {
        handlers += handler;
    }

    public static void Unsubscribe(Action<LogMessage> handler)
    {
        handlers -= handler;
    }

    /// <summary>当前作用域（任务标识）；由 <see cref="BeginScope"/> 设置。</summary>
    public static string? CurrentScope => currentScope.Value;

    /// <summary>
    /// 进入任务作用域：serve / GUI 任务执行期间调用，此后 Publish 的消息自动携带该标识；
    /// 返回的句柄 Dispose 时恢复先前作用域（可嵌套）。AsyncLocal 封装于此（同 ILogger.BeginScope 模式）。
    /// </summary>
    public static IDisposable BeginScope(string scope)
    {
        var previous = currentScope.Value;
        currentScope.Value = scope;
        return new ScopeLease(previous);
    }

    /// <summary>
    /// 发射一条消息。订阅端须自带锁保证渲染原子性；异常在此隔离——渲染故障不得中断下载链路
    /// （日志发射点遍布业务代码），单个订阅者抛异常不阻断其余订阅者，也不向上冒泡。
    /// </summary>
    public static void Publish(LogLevel level, string text, bool emphasized = false, bool enter = true, bool showTime = true)
    {
        var handler = handlers;
        if (handler is null)
        {
            return;
        }

        var message = new LogMessage(level, text, DateTimeOffset.Now, emphasized, currentScope.Value, enter, showTime);
        foreach (var subscriber in handler.GetInvocationList( ))
        {
            try
            {
                ((Action<LogMessage>) subscriber).Invoke(message);
            }
            catch
            {
                // 渲染故障无上报通道（回打日志会递归），只能静默
            }
        }
    }

    internal static void RestoreScope(string? previous)
    {
        currentScope.Value = previous;
    }
}

internal sealed class ScopeLease(string? previous) : IDisposable
{
    public void Dispose( )
    {
        MessageBus.RestoreScope(previous);
    }
}
