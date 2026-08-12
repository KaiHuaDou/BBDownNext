using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BBDown.GUI;

public enum TaskStatus
{
    Waiting,
    Running,
    Success,
    Failed,
    Cancelled,
}

/// <summary>队列任务单元：参数快照 + 目标 + 状态 + 日志序号。</summary>
public sealed class TaskState
{
    public required TaskParams Params { get; init; }
    public required string Url { get; init; }
    public required int Index { get; init; }
    public TaskStatus Status { get; set; }
    public CancellationTokenSource? TokenSource { get; set; }

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
public sealed class QueueRunner(Action<Action> dispatch)
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

    /// <summary>同时运行的任务数上限，运行时变更立即生效。</summary>
    public int Concurrency
    {
        get => concurrency;
        set => concurrency = value;
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

    /// <summary>移除等待中的任务；返回是否移除成功。</summary>
    public bool RemoveWaiting(TaskState state)
    {
        var removed = waiting.Remove(state);
        if (removed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }

        return removed;
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
            Index = nextIndex++,
        };
    }

    private async Task RunScheduleAsync( )
    {
        while (true)
        {
            await AcquireSlotAsync( );
            TaskState? state = null;
            dispatch(( ) =>
            {
                if (waiting.Count > 0)
                {
                    state = waiting[0];
                    waiting.RemoveAt(0);
                    running.Add(state);
                    state.Status = TaskStatus.Running;
                    state.TokenSource = new CancellationTokenSource( );
                    Changed?.Invoke(this, EventArgs.Empty);
                }
            });

            if (state is null)
            {
                ReleaseSlot( );
                break;
            }

            // 执行不阻塞调度循环，否则同一时刻只能跑一个任务；并发上限由槽控制
            _ = ExecuteAndReleaseAsync(state);
        }

        scheduling = false;
    }

    private async Task ExecuteAndReleaseAsync(TaskState state)
    {
        try
        {
            await ExecuteAsync(state);
        }
        catch (Exception)
        {
            // ExecuteAsync 已兜底任务异常，此处仅防御窗口关闭时 dispatch 抛出的异常
        }
        finally
        {
            ReleaseSlot( );
        }
    }

    private async Task ExecuteAsync(TaskState state)
    {
        try
        {
            if (Executor is null)
            {
                throw new InvalidOperationException("任务执行器未设置");
            }

            var token = state.TokenSource?.Token ?? CancellationToken.None;
            var exitCode = await Executor(state, token);
            dispatch(( ) =>
            {
                state.Status = exitCode == 0 ? TaskStatus.Success : TaskStatus.Failed;
                MoveToFinished(state);
                Changed?.Invoke(this, EventArgs.Empty);
            });
        }
        catch (OperationCanceledException)
        {
            dispatch(( ) =>
            {
                state.Status = TaskStatus.Cancelled;
                MoveToFinished(state);
                Changed?.Invoke(this, EventArgs.Empty);
            });
        }
        catch (Exception ex)
        {
            dispatch(( ) =>
            {
                state.Status = TaskStatus.Failed;
                MoveToFinished(state);
                Changed?.Invoke(this, EventArgs.Empty);
            });
            Logger?.Invoke(state, ex.Message);
        }
    }

    private void MoveToFinished(TaskState state)
    {
        running.Remove(state);
        finished.Add(state);
    }

    private async Task AcquireSlotAsync( )
    {
        while (true)
        {
            var current = Volatile.Read(ref activeCount);
            if (current < concurrency && Interlocked.CompareExchange(ref activeCount, current + 1, current) == current)
            {
                return;
            }

            await wakeup.WaitAsync( );
        }
    }

    private void ReleaseSlot( )
    {
        Interlocked.Decrement(ref activeCount);
        wakeup.Release( );
    }
}
