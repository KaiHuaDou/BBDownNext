#pragma warning disable CA2000 // current 是字典借用的引用，所有权归调用方，此处不得 Dispose

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace BBDown.Core.Live;

/// <summary>
/// SIGQUIT（Windows <c>Ctrl+Break</c> / Unix <c>Ctrl+\</c>）到「停止录制」的中枢
/// 录制期间由 <see cref="Register"/> 按会话标识挂载停止源，控制台 / GUI / serve 各自用同一标识停止对应录制
/// 会话标识以直播间为键（见 <see cref="LiveTarget.SessionId"/> 与 <see cref="LiveRoomInfo.SessionId"/>），
/// 短号与长号两种写法同占槽位，同一房间同时只允许一路录制
/// </summary>
public static class LiveSignal
{
    // 直播间 → 停止源：键即房间，覆盖多个直播间并发录制互不影响
    private static readonly ConcurrentDictionary<string, CancellationTokenSource> active = new( );

    /// <summary>
    /// 按会话标识挂载停止源，返回的 scope 释放后摘除该会话的挂载
    /// 同一标识已在录制时抛 <see cref="InvalidOperationException"/>：覆盖会让先注册者的摘除比较失败，槽位留成第二次的悬空挂载
    /// <paramref name="aliases"/> 是同一房间的其它写法（短号 / 长号）：只按输入串占位会被别名绕过，
    /// 两者一并占位才算闭合；与 <paramref name="sessionId"/> 相同时跳过，不会与自己冲突
    /// </summary>
    public static IDisposable Register(string sessionId, CancellationTokenSource cts, params string[] aliases)
    {
        ArgumentNullException.ThrowIfNull(cts);
        var keys = new List<string>(1 + aliases.Length) { sessionId };
        foreach (var alias in aliases)
        {
            if (!alias.Equals(sessionId, StringComparison.Ordinal))
            {
                keys.Add(alias);
            }
        }

        foreach (var key in keys)
        {
            if (active.TryAdd(key, cts))
            {
                continue;
            }

            // 回滚本次已占的键：不留半占的槽位，否则这次退出后那个房间会被永久占住
            Unregister(keys, cts);
            throw new InvalidOperationException($"{key} 已在录制中，不接受同一房间的并发录制");
        }

        return new LiveSignalScope(keys, cts);
    }

    /// <summary>
    /// 按会话标识请求停止录制。返回 <c>false</c> 表示该会话标识当前没有可停的录制（未注册 / 已停 / 已释放）
    /// 调用方应退化为全局取消——这正是二次 Ctrl+Break 变成强制退出的原因。已取消的停止源仍返回 <c>true</c>（重复取消返回同一结果）
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

    // 仅当槽位仍是自己时摘除：走 ICollection.Remove 的原子比较移除
    // 「先无条件 TryRemove 再判断回填」的两步形式在中间窗口会让 TryRequestStop 查不到条目
    // 调用方据此误判为无录制而退化成全局取消，误杀并发中的其它录制
    internal static void Unregister(IReadOnlyList<string> sessionIds, CancellationTokenSource cts)
    {
        foreach (var sessionId in sessionIds)
        {
            ((ICollection<KeyValuePair<string, CancellationTokenSource>>) active).Remove(new(sessionId, cts));
        }
    }
}

// scope 提升为顶层类型：记录 Register 返回的释放行为（按会话标识逐个摘除）
public sealed class LiveSignalScope(IReadOnlyList<string> sessionIds, CancellationTokenSource cts) : IDisposable
{
    private int disposed;

    public void Dispose( )
    {
        // 一个 scope 只摘一次：Dispose 可能被重复调用，重复摘除会误删同名房间后来者的挂载
        if (Interlocked.Exchange(ref disposed, 1) == 0)
        {
            LiveSignal.Unregister(sessionIds, cts);
        }
    }
}