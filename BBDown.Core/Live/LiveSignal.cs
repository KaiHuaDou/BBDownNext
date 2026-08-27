#pragma warning disable CA2000 // current 是字典借用的引用，所有权归 LiveDownload，此处不得 Dispose

using System;
using System.Collections.Concurrent;
using System.Threading;

namespace BBDown.Core.Live;

/// <summary>
/// SIGQUIT（Windows <c>Ctrl+Break</c> / Unix <c>Ctrl+\</c>）到「停止录制」的中枢。
/// 录制期间由 <see cref="LiveSignal.Register"/> 按会话标识挂载停止源，控制台 / GUI / serve 各自用同一标识停止对应录制，
/// 从而支持同一进程内并发录制多个直播间、互不影响。
/// </summary>
public static class LiveSignal
{
    // 按会话标识多注册表：key 由调用方保证唯一（如任务序号 / 房间号 / serve 任务 id）
    private static readonly ConcurrentDictionary<string, CancellationTokenSource> active = new( );

    /// <summary>
    /// 按会话标识挂载停止源，返回的 scope 释放后摘除该会话的挂载。同一标识再次注册会覆盖前者（调用方保证标识唯一，覆盖仅防异常残留）。
    /// </summary>
    public static IDisposable Register(string sessionId, CancellationTokenSource cts)
    {
        ArgumentNullException.ThrowIfNull(cts);
        active[sessionId] = cts;
        return new LiveSignalScope(sessionId, cts);
    }

    /// <summary>
    /// 按会话标识请求停止录制。返回 <c>false</c> 表示该会话标识当前没有可停的录制（未注册 / 已停 / 已释放），
    /// 调用方应退化为全局取消——这正是二次 Ctrl+Break 变成强制退出的原因。已取消的停止源仍返回 <c>true</c>（取消幂等）。
    /// </summary>
    public static bool TryRequestStop(string sessionId)
    {
        if (!active.TryGetValue(sessionId, out var cts) || cts is null)
        {
            return false;
        }

        try
        {
            if (cts.IsCancellationRequested)
            {
                return false;
            }

            cts.Cancel( );
            return true;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    // 仅当槽位仍是自己时摘除，避免后注册者被先释放的 scope 误清（与旧 Interlocked.CompareExchange 语义一致）
    internal static void Unregister(string sessionId, CancellationTokenSource cts)
    {
        active.TryRemove(sessionId, out var current);
        if (!ReferenceEquals(current, cts) && current is not null)
        {
            active[sessionId] = current;
        }
    }
}

// scope 提升为顶层类型：承载 Register 返回的释放语义（按会话标识摘除）
public sealed class LiveSignalScope(string sessionId, CancellationTokenSource cts) : IDisposable
{
    public void Dispose( )
    {
        LiveSignal.Unregister(sessionId, cts);
    }
}
