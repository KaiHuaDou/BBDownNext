using System;
using System.Threading;

namespace BBDown.Live;

/// <summary>
/// SIGQUIT（Windows <c>Ctrl+Break</c> / Unix <c>Ctrl+\</c>）到「停止录制」的中枢。
/// 录制期间由 <see cref="Register"/> 挂上停止源，控制台 handler 只调 <see cref="TryRequestStop"/>，
/// 二者解耦以避免 handler 直接持有录制状态。
/// </summary>
internal static class LiveSignal
{
    private static CancellationTokenSource? active;

    /// <summary>
    /// 挂载停止源，返回的 scope 释放后 SIGQUIT 恢复默认语义（全局取消）。
    /// 同一时刻只支持一个录制任务，后注册者覆盖前者。
    /// </summary>
    public static IDisposable Register(CancellationTokenSource cts)
    {
        ArgumentNullException.ThrowIfNull(cts);
        Interlocked.Exchange(ref active, cts);
        return new Scope(cts);
    }

    /// <summary>
    /// 请求停止录制。返回 <c>false</c> 表示没有可停的录制（未注册 / 已停 / 已释放），
    /// 调用方应退化为全局取消——这正是二次 Ctrl+Break 变成强制退出的原因。
    /// </summary>
    public static bool TryRequestStop( )
    {
        var cts = Volatile.Read(ref active);
        if (cts is null)
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

    // 只在仍是自己时摘除，避免后注册者被先释放的 scope 误清
    private sealed class Scope(CancellationTokenSource cts) : IDisposable
    {
        public void Dispose( )
        {
            Interlocked.CompareExchange(ref active, null, cts);
        }
    }
}
