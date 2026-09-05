#pragma warning disable CA1001 // wakeup 仅走 WaitAsync 异步路径，不创建内核句柄，生命周期随窗口

using System;
using System.Threading;
using System.Threading.Tasks;

namespace BBDown.GUI;

/// <summary>QueueRunner 的执行与并发调度部分；集合与状态变更经 dispatch 回投 UI 线程。</summary>
public sealed partial class QueueRunner
{
    private async Task RunScheduleAsync( )
    {
        try
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
        }
        catch
        {
            // 窗口关闭后 dispatch 回投失败属预期：调度随进程终止，无需记录
        }
        finally
        {
            // 复位与滞留重查收拢到 UI 线程：与 RunNow/Enqueue/StartSchedule 的入队同线程串行执行，
            // 消除后台裸读 waiting.Count 的可见性窗口（可能漏看 UI 线程刚入队的任务，导致永久滞留）
            try
            {
                dispatch(( ) =>
                {
                    scheduling = false;
                    if (waiting.Count > 0)
                    {
                        StartSchedule( );
                    }
                });
            }
            catch (Exception)
            {
                // 窗口已关闭且 dispatch 不可用：直接复位标志，调度随进程终止
                scheduling = false;
            }
        }
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
            // 先于 UI 回投落位：关窗导致 dispatch 失败时，落盘逻辑仍能凭 exitCode 排除已收尾任务
            state.exitCode = exitCode;
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
        // 收尾即释放取消源：无内核句柄不强制，但保持生命周期与任务一致（Retry 会重建）
        state.TokenSource?.Dispose( );
        state.TokenSource = null;
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
