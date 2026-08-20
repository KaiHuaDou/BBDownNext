using System;
using System.Collections.Concurrent;
using System.Threading;

using BBDown.Core.Logging;

namespace BBDown.Core.Workflow;

/// <summary>
/// 进度事件总线：下载链路在进度阶段内发射进度样本，宿主订阅决定展示。
/// 进度是阶段性的——阶段边界（BeginStage / Dispose）低频语义事件，宿主据此显隐；
/// 阶段内样本高频（快照语义），阶段外 Publish 静默忽略。Scope 复用 MessageBus 作用域。
/// </summary>
public static class ProgressBus
{
    private static Action<WorkflowEvent>? handlers;
    private static readonly ConcurrentDictionary<string, ProgressState> latestByScope = new( );

    public static void Subscribe(Action<WorkflowEvent> handler)
    {
        handlers += handler;
    }

    public static void Unsubscribe(Action<WorkflowEvent> handler)
    {
        handlers -= handler;
    }

    /// <summary>
    /// 进入进度阶段：发射开始事件（Scope 取当前任务作用域），返回的阶段句柄 Dispose 时发射结束事件。
    /// 同任务重入时先结束旧阶段；阶段句柄非线程安全，仅限单任务执行流持有。
    /// </summary>
    public static IDisposable BeginStage(string stageName)
    {
        var scope = MessageBus.CurrentScope ?? "";
        EndActive(scope);
        latestByScope[scope] = new ProgressState { StageName = stageName };
        handlers?.Invoke(new ProgressRangeStartEvent(scope, stageName));
        return new ProgressStage(scope);
    }

    /// <summary>
    /// 上报阶段内增量字节：累计由本总线按 scope 维护（同一任务多下载器并发互不覆盖，
    /// 多实例交替上报不再回退），speed 为折算速率（Byte/s）。无活跃阶段时静默忽略。
    /// 同步回调订阅者，订阅端须保证短快（渲染锁短，不阻塞采样线程）。
    /// </summary>
    public static void Publish(double ratio, long bytesDelta, double speed, string? detail = null)
    {
        var scope = MessageBus.CurrentScope ?? "";
        if (!latestByScope.TryGetValue(scope, out var state) || state.StageName is null)
        {
            return;
        }

        var sample = new ProgressSampleEvent(scope, ratio, state.AddBytes(bytesDelta), speed, detail);
        state.Sample = sample;
        handlers?.Invoke(sample);
    }

    /// <summary>
    /// 按任务标识取进度状态（快照式消费，serve 周期帧用）；无记录为 null。
    /// </summary>
    public static ProgressState? Latest(string scope)
    {
        return latestByScope.TryGetValue(scope, out var state) ? state : null;
    }

    internal static void EndStage(string scope)
    {
        if (!latestByScope.TryRemove(scope, out _))
        {
            return;
        }

        handlers?.Invoke(new ProgressRangeEndEvent(scope));
    }

    private static void EndActive(string scope)
    {
        if (!latestByScope.TryRemove(scope, out _))
        {
            return;
        }

        handlers?.Invoke(new ProgressRangeEndEvent(scope));
    }
}

/// <summary>
/// 任务进度状态：活跃阶段名 + 最新样本 + 阶段内累计字节。
/// 并发采样经 Interlocked 累计；Sample 引用读写原子，读侧为快照语义。阶段结束后条目被移除。
/// </summary>
public sealed class ProgressState
{
    private long totalBytes;

    public string? StageName { get; set; }
    public ProgressSampleEvent? Sample { get; set; }

    /// <summary>阶段内累计字节。</summary>
    public long TotalBytes => Interlocked.Read(ref totalBytes);

    internal long AddBytes(long delta)
    {
        return Interlocked.Add(ref totalBytes, delta);
    }
}

internal sealed class ProgressStage(string scope) : IDisposable
{
    public void Dispose( )
    {
        ProgressBus.EndStage(scope);
    }
}
