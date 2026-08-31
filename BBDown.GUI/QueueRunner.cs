#pragma warning disable CA1001 // wakeup 仅走 WaitAsync 异步路径，不创建内核句柄，生命周期随窗口

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core.Live;
using BBDown.Core.Opus;

namespace BBDown.GUI;

public enum TaskStatus
{
    Waiting,
    Running,
    Success,
    Failed,
    Cancelled,
}

/// <summary>任务执行链路：视频下载 / 直播录制 / 专栏导出，决定 ExecuteTaskAsync 的分流。</summary>
public enum TaskKind
{
    Video,
    Live,
    Opus,
}

/// <summary>队列任务单元：参数快照 + 目标 + 状态 + 日志序号。</summary>
public sealed class TaskState : INotifyPropertyChanged
{
    private double progress;
    private string? title;
    private string? detail;

    /// <summary>速度 / 剩余时间采样的基准时刻，仅 UI 线程由采样回调读写。</summary>
    internal DateTime etaStart;

    /// <summary>上一次采样进度（0..1），用于检测分 P 切换导致的进度回退。</summary>
    internal double lastRatio;

    /// <summary>执行器返回码，后台线程在 UI 回投前写入；-1 表示未收尾，关窗落盘时据此排除已完成的任务。</summary>
    internal volatile int exitCode = -1;

    public required TaskParams Params { get; init; }
    public required string Url { get; init; }
    public required TaskKind Kind { get; init; }
    public required int Index { get; init; }
    public TaskStatus Status { get; set; }
    public CancellationTokenSource? TokenSource { get; set; }

    /// <summary>解析出的视频标题（Meta 回吐后填充）；空则列表回退显示 Url。</summary>
    public string? Title
    {
        get => title;
        set
        {
            if (title == value)
            {
                return;
            }

            title = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Title)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Display)));
        }
    }

    /// <summary>任务列表展示文本：有标题显标题，否则显 Url。</summary>
    public string Display => title ?? Url;

    /// <summary>运行中的速度 / 剩余时间文本（如「12.3 MB/s · 剩余 1m23s」），空则隐藏。</summary>
    public string? Detail
    {
        get => detail;
        set
        {
            if (detail == value)
            {
                return;
            }

            detail = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Detail)));
        }
    }

    /// <summary>当前分片下载进度（0..1）；仅在 UI 线程变更。</summary>
    public double Progress
    {
        get => progress;
        set
        {
            if (progress == value)
            {
                return;
            }

            progress = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Progress)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string StatusText => Status switch
    {
        TaskStatus.Waiting => "等待中",
        TaskStatus.Running => "运行中",
        TaskStatus.Success => "成功",
        TaskStatus.Failed => "失败",
        TaskStatus.Cancelled => "已取消",
        _ => "未知",
    };

    public override string ToString( )
    {
        return $"{StatusText} | {Url}";
    }
}

/// <summary>任务队列与并发调度；集合与状态只在 UI 线程变更（经 dispatch 回投），后台仅执行子进程。</summary>
public sealed partial class QueueRunner(Action<Action> dispatch)
{
    private readonly Action<Action> dispatch = dispatch;
    private readonly List<TaskState> waiting = [];
    private readonly List<TaskState> running = [];
    private readonly List<TaskState> finished = [];
    private readonly SemaphoreSlim wakeup = new(0);
    private int activeCount;
    private volatile bool scheduling;
    private int nextIndex = 1;
    private volatile int concurrency = 3;

    /// <summary>同时运行的任务数上限，运行时调大立即唤醒排队中的等待任务。</summary>
    public int Concurrency
    {
        get => concurrency;
        set
        {
            var previous = concurrency;
            concurrency = value;
            // 调大并发上限时主动放行一个等待槽，使排队任务立即扩容而非等到有任务完成
            if (value > previous)
            {
                wakeup.Release( );
            }
        }
    }

    /// <summary>任务执行器，返回子进程退出码；未设置时任务直接标记失败。</summary>
    public Func<TaskState, CancellationToken, Task<int>>? Executor { get; set; }

    /// <summary>执行异常日志回调（如启动失败原因）。</summary>
    public Action<TaskState, string>? Logger { get; set; }

    /// <summary>队列或任务状态变化时触发（保证在 UI 线程）。</summary>
    public event EventHandler? Changed;

    public IEnumerable<TaskState> All => waiting.Concat(running).Concat(finished);

    /// <summary>是否存在等待调度的任务。</summary>
    public bool HasWaiting => waiting.Count > 0;

    /// <summary>立即执行：入队尾并启动调度；并发已满时返回 true（任务排队等待）。</summary>
    public bool RunNow(TaskParams options, string url)
    {
        waiting.Add(CreateState(options, url));
        var queued = running.Count >= concurrency;
        Changed?.Invoke(this, EventArgs.Empty);
        StartSchedule( );
        return queued;
    }

    /// <summary>加入任务队列尾部，不启动调度。</summary>
    public void Enqueue(TaskParams options, string url)
    {
        waiting.Add(CreateState(options, url));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>启动队列调度，幂等。</summary>
    public void StartSchedule( )
    {
        if (scheduling)
        {
            return;
        }

        scheduling = true;
        _ = Task.Run(RunScheduleAsync);
    }

    /// <summary>移除指定任务：等待中或已完成直接移除；运行中不处理（用取消）。</summary>
    public bool Remove(TaskState state)
    {
        var removed = waiting.Remove(state) || finished.Remove(state);
        if (removed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }

        return removed;
    }

    /// <summary>把失败/已取消的任务重新入队尾并启动调度；不在已完成列表时返回 false。</summary>
    public bool Retry(TaskState state)
    {
        if (!finished.Remove(state))
        {
            return false;
        }

        state.Status = TaskStatus.Waiting;
        state.Progress = 0;
        state.TokenSource = null;
        state.exitCode = -1;
        waiting.Add(state);
        Changed?.Invoke(this, EventArgs.Empty);
        StartSchedule( );
        return true;
    }

    public void ClearFinished( )
    {
        if (finished.Count == 0)
        {
            return;
        }

        finished.Clear( );
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>取消全部运行中任务（关闭窗口时调用）。</summary>
    public void CancelRunning( )
    {
        foreach (var state in running)
        {
            state.TokenSource?.Cancel( );
        }
    }

    /// <summary>取消指定运行中的任务；非运行态不生效。</summary>
    public static void CancelTask(TaskState state)
    {
        state.TokenSource?.Cancel( );
    }

    private TaskState CreateState(TaskParams options, string url)
    {
        return new TaskState
        {
            Params = options,
            Url = url,
            Kind = DetectKind(url),
            Index = nextIndex++,
        };
    }

    private static TaskKind DetectKind(string url)
    {
        if (LiveInputResolver.TryParse(url, out _))
        {
            return TaskKind.Live;
        }

        if (OpusInputResolver.TryParse(url, out _))
        {
            return TaskKind.Opus;
        }

        return TaskKind.Video;
    }
}
