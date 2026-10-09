#pragma warning disable CS8602 // Avalonia 源生成的 x:Name 控件字段可空

using System;
using System.Collections.Generic;
using System.Linq;

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

using BBDown.Core.Live;

namespace BBDown.GUI;

/// <summary>任务队列侧的事件与刷新，控制 MainWindow.xaml.cs 行数。</summary>
public partial class MainWindow
{
    private void OnQueueChanged(object? o, EventArgs e)
    {
        RefreshTaskList( );
    }

    private void RefreshTaskList( )
    {
        var latest = queue.All.ToList( );
        var present = new HashSet<TaskState>(latest);

        // 引用级增量同步：只增删 / 移位不重建，保住列表选中态与滚动位置；行内状态变化由 TaskState 属性通知直达绑定
        for (var i = tasks.Count - 1; i >= 0; i--)
        {
            if (!present.Contains(tasks[i]))
            {
                tasks.RemoveAt(i);
            }
        }

        for (var i = 0; i < latest.Count; i++)
        {
            if (i < tasks.Count && ReferenceEquals(tasks[i], latest[i]))
            {
                continue;
            }

            var from = tasks.IndexOf(latest[i]);
            if (from >= 0)
            {
                tasks.Move(from, i);
            }
            else
            {
                tasks.Insert(i, latest[i]);
            }
        }

        lock (indexGate)
        {
            byIndex.Clear( );
            foreach (var state in tasks)
            {
                byIndex[state.Index] = state;
            }
        }

        TaskListEmptyHint.IsVisible = tasks.Count == 0;
        QueueStatusText.Text = $"等待 {tasks.Count(t => t.Status == TaskStatus.Waiting)}" +
                               $" · 运行 {tasks.Count(t => t.Status == TaskStatus.Running)}" +
                               $" · 成功 {tasks.Count(t => t.Status == TaskStatus.Success)}" +
                               $" · 失败 {tasks.Count(t => t.Status == TaskStatus.Failed)}" +
                               $" · 已取消 {tasks.Count(t => t.Status == TaskStatus.Cancelled)}";
    }

    private void CancelTaskButtonClicked(object? o, RoutedEventArgs e)
    {
        if (o is not Button { Tag: TaskState state } || state.Status != TaskStatus.Running)
        {
            return;
        }

        if (!queue.CancelTask(state))
        {
            AppendLog($"任务{state.Index} 已不在运行中");
            return;
        }

        AppendLog($"任务{state.Index} 已请求取消");
    }

    private void StopRecordButtonClicked(object? o, RoutedEventArgs e)
    {
        if (o is not Button { Tag: TaskState state } || state.LiveSessionId is not { } sessionId)
        {
            return;
        }

        // LiveSignal 按直播间定位录制会话，并发录制不同房间互不干扰
        AppendLog(LiveSignal.TryRequestStop(sessionId)
            ? $"任务{state.Index} 已请求停止录制并合并"
            : $"任务{state.Index} 当前未在录制或已停止");
    }

    private void StartQueueButtonClicked(object? o, RoutedEventArgs e)
    {
        if (!queue.HasWaiting)
        {
            AppendLog("队列中没有等待的任务");
            return;
        }

        AppendLog(queue.StartSchedule( ) ? "队列调度已启动" : "队列调度已在运行中");
    }

    private void RemoveItemButtonClicked(object? o, RoutedEventArgs e)
    {
        if (o is not Button { Tag: TaskState state })
        {
            return;
        }

        if (state.Status == TaskStatus.Running)
        {
            AppendLog("运行中的任务请先取消");
            return;
        }

        if (!queue.Remove(state))
        {
            AppendLog("任务已不在队列中");
        }
    }

    private void RetryButtonClicked(object? o, RoutedEventArgs e)
    {
        if (o is not Button { Tag: TaskState state })
        {
            return;
        }

        if (!queue.Retry(state))
        {
            AppendLog("任务已不在队列中");
            return;
        }

        AppendLog($"任务{state.Index} 已重新加入队列");
    }

    private void ClearButtonClicked(object? o, RoutedEventArgs e)
    {
        queue.ClearFinished( );
    }

    private void ConcurrencyBoxTextChanged(object? o, TextChangedEventArgs e)
    {
        // 输入即时反馈：非法值标红，合法值消除；回退与写入仍统一发生在失焦 / 关窗
        var valid = int.TryParse(ConcurrencyBox.Text, out var value)
                    && value is >= MinConcurrency and <= MaxConcurrency;
        if (valid)
        {
            ConcurrencyBox.Classes.Remove("invalid");
        }
        else
        {
            ConcurrencyBox.Classes.Add("invalid");
        }
    }

    private void ConcurrencyBoxLostFocus(object? o, RoutedEventArgs e)
    {
        ConcurrencyBox.Classes.Remove("invalid");
        if (int.TryParse(ConcurrencyBox.Text, out var value) && value is >= MinConcurrency and <= MaxConcurrency)
        {
            lastConcurrency = value.ToString( );
            queue.Concurrency = value;
            return;
        }

        ConcurrencyBox.Text = lastConcurrency;
        AppendLog($"并发数无效，已回退为 {lastConcurrency}");
    }
}
