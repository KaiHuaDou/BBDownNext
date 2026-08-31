#pragma warning disable CS8602 // Avalonia 源生成的 x:Name 控件字段可空

using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;

namespace BBDown.GUI;

/// <summary>日志区行数据：文本 + 行前景色；错误行为红色，普通行为主题默认前景。</summary>
public sealed record LogLine(string Text, IBrush Brush);

/// <summary>日志区：逐行追加、错误行红色、按行数控制上限，ListBox 虚拟化渲染。</summary>
public partial class MainWindow
{
    private const int MaxLogLines = 5000;
    private static readonly IBrush ErrorBrush = new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35));
    private readonly ObservableCollection<LogLine> logLines = [];

    /// <summary>普通行前景：沿用窗口主题文本色（深色背景下非黑即白、始终可见），避免 null 被渲染成黑色不可见。</summary>
    private IBrush NormalBrush => Foreground ?? new SolidColorBrush(Color.FromRgb(0xEE, 0xEE, 0xEE));

    private void AppendProcessLog(int index, string line, bool isError)
    {
        var prefix = isError ? "[错误] " : "";
        AppendLog($"[任务{index}] {prefix}{line}", isError);
    }

    private void LogTaskError(TaskState state, string message)
    {
        AppendLog($"[任务{state.Index}] {message}");
    }

    private void ExportLogButtonClicked(object? o, RoutedEventArgs e)
    {
        // 与配置 / 队列持久化同目录（exe 所在目录，portable），避免导出到随安装位置变化的 BaseDirectory
        var path = Path.Combine(GuiPaths.ExeDirectory( ), $"BBDown.GUI.log.{DateTime.Now:yyyyMMdd_HHmmss}.txt");
        try
        {
            File.WriteAllLines(path, logLines.Select(line => line.Text));
            AppendLog($"日志已导出到 {path}");
        }
        catch (Exception ex)
        {
            AppendLog($"日志导出失败：{ex.Message}", isError: true);
        }
    }

    private void AppendLog(string line, bool isError = false)
    {
        if (!Dispatcher.UIThread.CheckAccess( ))
        {
            if (!closed)
            {
                Dispatcher.UIThread.Post(( ) => AppendLog(line, isError));
            }

            return;
        }

        logLines.Add(new LogLine(line, isError ? ErrorBrush : NormalBrush));
        while (logLines.Count > MaxLogLines)
        {
            logLines.RemoveAt(0);
        }

        LogList.ScrollIntoView(logLines[^1]);
    }
}
