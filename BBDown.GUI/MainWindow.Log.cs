using System;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace BBDown.GUI;

/// <summary>RichTextBox 日志区：逐行追加、错误行红色、按段落数控制上限。</summary>
public partial class MainWindow
{
    private const int MaxLogLines = 5000;
    private static readonly Brush ErrorBrush = new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35));
    // 显式跟随系统前景，避免 FlowDocument 继承链导致白字不可见（深色系统下 WindowTextBrush 为白）
    private static readonly Brush NormalBrush = SystemColors.WindowTextBrush;

    private void AppendProcessLog(int index, string line, bool isError)
    {
        var prefix = isError ? "[错误] " : "";
        AppendLog($"[任务{index}] {prefix}{line}", isError);
    }

    private void LogTaskError(TaskState state, string message)
    {
        AppendLog($"[任务{state.Index}] {message}");
    }

    private void AppendLog(string line, bool isError = false)
    {
        if (!Dispatcher.CheckAccess( ))
        {
            if (!Dispatcher.HasShutdownStarted)
            {
                Dispatcher.BeginInvoke(( ) => AppendLog(line, isError));
            }

            return;
        }

        var document = LogBox.Document;
        Paragraph paragraph = new(new Run(line) { Foreground = isError ? ErrorBrush : NormalBrush })
        {
            Margin = new Thickness(0),
        };
        document.Blocks.Add(paragraph);
        while (document.Blocks.Count > MaxLogLines)
        {
            document.Blocks.Remove(document.Blocks.FirstBlock);
        }

        LogBox.ScrollToEnd( );
    }
}
