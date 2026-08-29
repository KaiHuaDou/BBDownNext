using System;
using System.Threading;
using System.Threading.Tasks;

namespace BBDown.GUI;

/// <summary>QueueRunner 的执行与并发调度部分；集合与状态变更经 dispatch 回投 UI 线程。</summary>
public sealed partial class QueueRunner
{
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
                var hasWaiting = false;
                dispatch(( ) => hasWaiting = waiting.Count > 0);
                if (hasWaiting)
                {
                    continue;
                }

                break;
            }

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
        catch (Exception ex)
        {
            // ExecuteAsync 已兜底任务异常（含取消）；此处仅防御窗口关闭时 dispatch 抛出的异常，记录后忽略避免拖垮调度循环
            if (ex is not OperationCanceledException)
            {
                Logger?.Invoke(state, $"调度回调异常（已忽略）：{ex.Message}");
            }
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
