using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

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
        tasks.Clear( );
        foreach (var state in queue.All)
        {
            tasks.Add(state);
        }

        QueueStatusText.Text = $"等待 {tasks.Count(t => t.Status == TaskStatus.Waiting)}" +
                               $" · 运行 {tasks.Count(t => t.Status == TaskStatus.Running)}" +
                               $" · 完成 {tasks.Count(t => t.Status is TaskStatus.Success or TaskStatus.Failed or TaskStatus.Cancelled)}";
    }

    private void TaskListSelectionChanged(object o, SelectionChangedEventArgs e)
    {
        RemoveButton.IsEnabled = TaskList.SelectedItem is TaskState { Status: TaskStatus.Waiting };
    }

    private void CancelTaskButtonClicked(object o, RoutedEventArgs e)
    {
        if (o is not Button { Tag: TaskState state } || state.Status != TaskStatus.Running)
        {
            return;
        }

        QueueRunner.CancelTask(state);
        AppendLog($"任务{state.Index} 已请求取消");
    }

    private void StartQueueButtonClicked(object o, RoutedEventArgs e)
    {
        if (!queue.HasWaiting)
        {
            AppendLog("队列中没有等待的任务");
            return;
        }

        queue.StartSchedule( );
        AppendLog("队列调度已启动");
    }

    private void RemoveButtonClicked(object o, RoutedEventArgs e)
    {
        if (TaskList.SelectedItem is not TaskState state)
        {
            return;
        }

        if (!queue.RemoveWaiting(state))
        {
            AppendLog("仅可移除等待中的任务");
        }
    }

    private void ClearButtonClicked(object o, RoutedEventArgs e)
    {
        queue.ClearFinished( );
    }

    private void ConcurrencyBoxLostKeyboardFocus(object o, KeyboardFocusChangedEventArgs e)
    {
        if (int.TryParse(ConcurrencyBox.Text, out var value) && value is >= 1 and <= 8)
        {
            lastConcurrency = value.ToString( );
            queue.Concurrency = value;
            return;
        }

        ConcurrencyBox.Text = lastConcurrency;
        AppendLog($"并发数无效，已回退为 {lastConcurrency}");
    }
}
