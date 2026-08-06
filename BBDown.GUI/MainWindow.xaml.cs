using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

using Microsoft.Win32;

namespace BBDown.GUI;

public partial class MainWindow : Window
{
    private const int MaxLogLines = 5000;

    private readonly List<string> logLines = [];
    private readonly ObservableCollection<TaskState> tasks = [];
    private readonly QueueRunner queue;
    private readonly Brush okBrush = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
    private readonly Brush badBrush = new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35));
    private readonly Brush hintBrush = new SolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E));
    private string lastConcurrency = "3";

    public MainWindow( )
    {
        InitializeComponent( );
        ApiBox.ItemsSource = new[] { "web", "tv", "app", "intl" };
        foreach ((var value, var label) in LiveQualityChoices)
        {
            LiveQualityBox.Items.Add(new ComboBoxItem { Content = label, Tag = value });
        }

        queue = new QueueRunner(Dispatch);
        queue.Changed += OnQueueChanged;
        queue.Executor = ExecuteTaskAsync;
        queue.Logger = LogTaskError;
        TaskList.ItemsSource = tasks;
        LoadConfig( );
        UpdateTargetHint( );
        AppendLog("就绪");
    }

    private void WindowClosed(object o, EventArgs e)
    {
        queue.CancelRunning( );
        try
        {
            ConfigData config = new( )
            {
                Options = ReadOptions( ),
                ExePath = ExePathBox.Text.Trim( ),
                Concurrency = int.TryParse(ConcurrencyBox.Text, out var value) ? value : 3,
            };
            ConfigStore.Save(config);
        }
        catch (Exception ex)
        {
            AppendLog($"配置保存失败：{ex.Message}");
        }
    }

    private void ExeSelectButtonClicked(object o, RoutedEventArgs e)
    {
        OpenFileDialog dialog = new( ) { Filter = "可执行文件 (*.exe)|*.exe" };
        if (dialog.ShowDialog( ) == true)
        {
            ExePathBox.Text = dialog.FileName;
            UpdateExeHint( );
        }
    }

    private void ExeDetectButtonClicked(object o, RoutedEventArgs e)
    {
        var found = BBDownLocator.Find( );
        if (found is null)
        {
            AppendLog("未找到 BBDown.exe，请手动选择");
            return;
        }

        ExePathBox.Text = found;
        UpdateExeHint( );
        AppendLog($"自动检测到 BBDown.exe：{found}");
    }

    private void ExePathBoxLostKeyboardFocus(object o, KeyboardFocusChangedEventArgs e)
    {
        UpdateExeHint( );
    }

    private void TargetBoxTextChanged(object o, TextChangedEventArgs e)
    {
        UpdateTargetHint( );
    }

    private void RunButtonClicked(object o, RoutedEventArgs e)
    {
        if (!TryGetTarget(out var url))
        {
            return;
        }

        if (!IsExePathValid( ))
        {
            AppendLog("BBDown.exe 路径无效，请先选择或自动检测");
            return;
        }

        var queued = queue.RunNow(ReadOptions( ), url);
        AppendLog(queued ? $"并发已满，任务已加入队列等待执行：{url}" : $"任务已启动：{url}");
    }

    private void EnqueueButtonClicked(object o, RoutedEventArgs e)
    {
        if (!TryGetTarget(out var url))
        {
            return;
        }

        queue.Enqueue(ReadOptions( ), url);
        AppendLog($"任务已加入队列：{url}");
    }

    private void ResetButtonClicked(object o, RoutedEventArgs e)
    {
        ApplyOptions(new TaskParams( ));
        AppendLog("选项已重置");
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

    private void LoadConfig( )
    {
        var config = ConfigStore.Load( );
        if (config is null)
        {
            ApplyOptions(new TaskParams( ));
            var found = BBDownLocator.Find( );
            if (found is not null)
            {
                ExePathBox.Text = found;
                AppendLog($"自动检测到 BBDown.exe：{found}");
            }

            return;
        }

        ApplyOptions(config.Options);
        ExePathBox.Text = config.ExePath;
        ConcurrencyBox.Text = config.Concurrency.ToString( );
        lastConcurrency = ConcurrencyBox.Text;
    }

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
    }

    private void Dispatch(Action action)
    {
        Dispatcher.Invoke(action);
    }

    private async Task<int> ExecuteTaskAsync(TaskState state, CancellationToken token)
    {
        // 调度循环在后台线程执行，读 UI 控件须回 UI 线程
        var exePath = await Dispatcher.InvokeAsync(ExePathBox.Text.Trim).Task;
        var args = CliArgsBuilder.Build(state.Params, state.Url);
        var index = state.Index;
        try
        {
            return await ProcessRunner.RunAsync(exePath, args, (line, isError) => AppendProcessLog(index, line, isError), token);
        }
        catch (OperationCanceledException)
        {
            AppendProcessLog(index, "已取消", false);
            throw;
        }
    }

    private void LogTaskError(TaskState state, string message)
    {
        AppendLog($"[任务{state.Index}] {message}");
    }

    private void AppendProcessLog(int index, string line, bool isError)
    {
        var prefix = isError ? "[错误] " : "";
        AppendLog($"[任务{index}] {prefix}{line}");
    }

    private void AppendLog(string line)
    {
        if (!Dispatcher.CheckAccess( ))
        {
            if (!Dispatcher.HasShutdownStarted)
            {
                Dispatcher.BeginInvoke(( ) => AppendLog(line));
            }

            return;
        }

        logLines.Add(line);
        if (logLines.Count > MaxLogLines)
        {
            logLines.RemoveRange(0, logLines.Count - MaxLogLines);
            LogBox.Text = string.Join(Environment.NewLine, logLines);
        }
        else
        {
            LogBox.AppendText(line + Environment.NewLine);
        }

        LogBox.ScrollToEnd( );
    }

    private bool TryGetTarget(out string url)
    {
        url = TargetBox.Text.Trim( );
        if (url.Length == 0)
        {
            AppendLog("未填写下载目标");
            return false;
        }

        if (UrlDetector.Describe(url) is null)
        {
            AppendLog("下载目标无法识别，未加入队列");
            return false;
        }

        return true;
    }

    private bool IsExePathValid( )
    {
        var path = ExePathBox.Text.Trim( );
        return path.Length > 0 && File.Exists(path);
    }

    private void UpdateExeHint( )
    {
        var valid = IsExePathValid( );
        ExeHintText.Text = valid ? "" : "路径无效";
        ExeHintText.Foreground = valid ? hintBrush : badBrush;
    }

    private void UpdateTargetHint( )
    {
        var description = UrlDetector.Describe(TargetBox.Text);
        if (description is null)
        {
            TargetHintText.Text = "未能识别";
            TargetHintText.Foreground = hintBrush;
        }
        else
        {
            TargetHintText.Text = $"✓ {description}";
            TargetHintText.Foreground = okBrush;
        }
    }
}
