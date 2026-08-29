#pragma warning disable CA1308, CS8600, CS8602 // CA1308：API 通道等枚举名取小写作 UI 标签，以 Core 枚举为单一来源；CS8600/CS8602：Avalonia 源生成的 x:Name 控件字段可空

using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

using BBDown.Core;
using BBDown.Core.Download;
using BBDown.Core.Live;
using BBDown.Core.Logging;

namespace BBDown.GUI;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<TaskState> tasks = [];
    private readonly QueueRunner queue;
    private readonly Brush okBrush = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
    private readonly Brush hintBrush = new SolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E));
    private const int DefaultConcurrency = 3;
    private string lastConcurrency = DefaultConcurrency.ToString( );
    private volatile bool closed;

    // 窗口非最小化时的最近边界，最小化状态下关闭时回退到该值
    private double lastLeft, lastTop, lastWidth = 1120, lastHeight = 820;

    public MainWindow( )
    {
        InitializeComponent( );
        LogList.ItemsSource = logLines;
        // API 通道、内容字符、直播清晰度均以 Core 枚举/表为单一来源，避免列表在多处硬编码
        ApiBox.ItemsSource = Enum.GetNames<ApiType>( ).Select(n => n.ToLowerInvariant( )).ToArray( );
        ContentItems.ItemsSource = ContentSelector.Order.Select(e => new ContentOption(e.Ch, $"{e.Name} (_{e.Ch})")).ToList( );
        foreach (var (qn, name) in LiveQuality.Levels)
        {
            LiveQualityBox.Items.Add(new ComboBoxItem { Content = $"{qn} {name}", Tag = qn.ToString( ) });
        }

        LiveQualityBox.SelectedIndex = 0;

        foreach ((var value, var label) in MuxChoices)
        {
            MuxBox.Items.Add(new ComboBoxItem { Content = label, Tag = value });
        }

        MuxBox.SelectedIndex = 0;

        // 下载核心的日志直接进窗口日志区（替代原解析子进程 stdout），按级别着色；Scope 标注任务序号
        MessageBus.Subscribe(OnLogMessage);
        // 下载进度样本按 Scope（任务序号）回投到对应任务行
        ProgressBus.Subscribe(OnProgress);
        // 交互选项请求（逐集确认 / 选轨）回投 UI 线程弹窗应答
        AskBus.Subscribe(OnAsk);

        queue = new QueueRunner(Dispatcher.UIThread.Invoke);
        queue.Changed += OnQueueChanged;
        queue.Executor = ExecuteTaskAsync;
        queue.Logger = LogTaskError;
        TaskList.ItemsSource = tasks;
        LoadConfig( );
        RestoreQueue( );
        _ = RefreshLoginStatusAsync( );
        UpdateTargetHint( );
        AppendLog("就绪");

        DragDrop.SetAllowDrop(this, true);
        DragDrop.AddDragOverHandler(this, WindowDragOver);
        DragDrop.AddDropHandler(this, WindowDrop);
    }

    // MessageBus 回调跑在下载线程，需回投 UI 线程；closed 在 WindowClosed 置位，回投前判空避免向已销毁窗口 Post 崩溃
    private void OnLogMessage(LogMessage message)
    {
        var line = message.Scope is { } scope ? $"[任务{scope}] {message.Text}" : message.Text;
        var isError = message.Level == LogLevel.Error;
        if (!closed)
        {
            Dispatcher.UIThread.Post(( ) => AppendLog(line.TrimEnd('\n'), isError));
        }
    }

    private void WindowClosed(object? o, EventArgs e)
    {
        MessageBus.Unsubscribe(OnLogMessage);
        ProgressBus.Unsubscribe(OnProgress);
        AskBus.Unsubscribe(OnAsk);
        // 关闭窗口时取消全部挂起的交互提问，避免下载链路挂起 5 分钟超时
        foreach (var task in tasks)
        {
            AskBus.CancelPending(task.Index.ToString( ));
        }

        closed = true;
        queue.CancelRunning( );
        try
        {
            if (WindowState != WindowState.Minimized)
            {
                lastLeft = Position.X;
                lastTop = Position.Y;
                lastWidth = Width;
                lastHeight = Height;
            }

            ConfigData config = new( )
            {
                Options = ReadOptions( ),
                Concurrency = int.TryParse(ConcurrencyBox.Text, out var value) ? value : 3,
                WindowLeft = lastLeft,
                WindowTop = lastTop,
                WindowWidth = lastWidth,
                WindowHeight = lastHeight,
            };
            ConfigStore.Save(config);
        }
        catch (Exception ex)
        {
            AppendLog($"配置保存失败：{ex.Message}");
        }

        try
        {
            SaveQueue( );
        }
        catch (Exception ex)
        {
            AppendLog($"队列保存失败：{ex.Message}");
        }
    }

    /// <summary>把未完成（等待 / 运行中）的任务落盘，下次启动恢复。</summary>
    private void SaveQueue( )
    {
        var pending = queue.All
            .Where(t => t.Status is TaskStatus.Waiting or TaskStatus.Running)
            .Select(t => new QueuedTask(t.Params, t.Url));
        QueueStore.Save(pending);
    }

    /// <summary>恢复上次未完成的任务到等待队列。</summary>
    private void RestoreQueue( )
    {
        var pending = QueueStore.Load( );
        foreach (var task in pending)
        {
            queue.Enqueue(task.Options, task.Url);
        }

        if (pending.Count > 0)
        {
            AppendLog($"已恢复 {pending.Count} 个未完成任务到队列");
        }
    }

    private void TargetBoxTextChanged(object? o, TextChangedEventArgs e)
    {
        UpdateTargetHint( );
    }

    private void RunButtonClicked(object? o, RoutedEventArgs e)
    {
        if (!TryGetTarget(out var url))
        {
            return;
        }

        var queued = queue.RunNow(ReadOptions( ), url);
        AppendLog(queued ? $"并发已满，任务已加入队列等待执行：{url}" : $"任务已启动：{url}");
    }

    private void EnqueueButtonClicked(object? o, RoutedEventArgs e)
    {
        if (!TryGetTarget(out var url))
        {
            return;
        }

        queue.Enqueue(ReadOptions( ), url);
        AppendLog($"任务已加入队列：{url}");
    }

    private void ResetButtonClicked(object? o, RoutedEventArgs e)
    {
        ApplyOptions(new TaskParams( ));
        lastConcurrency = DefaultConcurrency.ToString( );
        ConcurrencyBox.Text = lastConcurrency;
        queue.Concurrency = DefaultConcurrency;
        AppendLog("选项已重置");
    }

    private void OpenDirButtonClicked(object? o, RoutedEventArgs e)
    {
        var dir = WorkDirBox.Text.Trim( );
        var path = dir.Length > 0 && Directory.Exists(dir)
            ? dir
            : (Path.GetDirectoryName(Environment.ProcessPath) ?? "");
        if (path.Length == 0 || !Directory.Exists(path))
        {
            AppendLog("输出目录不存在，请先在“工作目录”填写有效路径");
            return;
        }

        if (TopLevel.GetTopLevel(this) is { } topLevel)
        {
            _ = topLevel.Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(path));
        }
    }

    /// <summary>非负整数字段失焦校验，无效回退上次有效值（存于 Tag）。</summary>
    private void IntegerBoxLostFocus(object? o, RoutedEventArgs e)
    {
        if (o is not TextBox box)
        {
            return;
        }

        var fallback = box.Tag as string ?? "0";
        if (int.TryParse(box.Text, out var value) && value >= 0)
        {
            box.Tag = value.ToString( );
            return;
        }

        box.Text = fallback;
        AppendLog($"{box.Name} 需为非负整数，已回退为 {fallback}");
    }

    private void WindowDragOver(object? o, DragEventArgs e)
    {
        e.DragEffects = HasText(e.DataTransfer) ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private void WindowDrop(object? o, DragEventArgs e)
    {
        if (GetText(e.DataTransfer) is { } text)
        {
            TargetBox.Text = text.Trim( );
        }
    }

    private static bool HasText(IDataTransfer? transfer)
    {
        return transfer?.Formats.Contains(DataFormat.Text) == true;
    }

    private static string? GetText(IDataTransfer? transfer)
    {
        if (transfer is null)
        {
            return null;
        }

        foreach (var item in transfer.Items)
        {
            if (item.TryGetRaw(DataFormat.Text) is string text)
            {
                return text;
            }
        }

        return null;
    }

    private void LoadConfig( )
    {
        var config = ConfigStore.Load( );
        if (config is null)
        {
            ApplyOptions(new TaskParams( ));
            return;
        }

        ApplyOptions(config.Options);
        ConcurrencyBox.Text = config.Concurrency.ToString( );
        lastConcurrency = ConcurrencyBox.Text;
        ApplyWindowBounds(config);
    }

    private void ApplyWindowBounds(ConfigData config)
    {
        if (config.WindowLeft is not { } left || config.WindowTop is not { } top ||
            config.WindowWidth is not { } width || config.WindowHeight is not { } height ||
            !IsOnScreen(left, top, width, height))
        {
            return;
        }

        Position = new PixelPoint((int) left, (int) top);
        Width = width;
        Height = height;
    }

    private bool IsOnScreen(double left, double top, double width, double height)
    {
        // 窗口至少留 100px 可见才算在屏内；窗口未连接显示时跳过检查
        if (Screens.All.Count == 0)
        {
            return true;
        }

        foreach (var screen in Screens.All)
        {
            var bounds = screen.Bounds;
            if (left + width > bounds.X && left < bounds.X + bounds.Width - 100 &&
                top + height > bounds.Y && top < bounds.Y + bounds.Height - 100)
            {
                return true;
            }
        }

        return false;
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
