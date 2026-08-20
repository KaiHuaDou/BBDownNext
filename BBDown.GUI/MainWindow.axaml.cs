#pragma warning disable CA1308, CS8600, CS8602 // CA1308：API 通道等枚举名取小写作 UI 标签，以 Core 枚举为单一来源；CS8600/CS8602：Avalonia 源生成的 x:Name 控件字段可空

using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

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
using BBDown.Core.Pipeline;
using BBDown.Core.Util;
using BBDown.Core.Workflow;

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

        foreach ((var value, var label) in MuxChoices)
        {
            MuxBox.Items.Add(new ComboBoxItem { Content = label, Tag = value });
        }

        MuxBox.SelectedIndex = 0;

        // 下载核心的日志直接进窗口日志区（替代原解析子进程 stdout），按级别着色；Scope 标注任务序号
        MessageBus.Subscribe(OnLogMessage);
        // 下载进度样本按 Scope（任务序号）回投到对应任务行
        ProgressBus.Subscribe(OnProgress);

        queue = new QueueRunner(Dispatch);
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

    // 日志区渲染：MessageBus 消息按 Scope（任务序号）加前缀，Error 级标红；窗口关闭后退订
    private void OnLogMessage(LogMessage message)
    {
        var line = message.Scope is { } scope ? $"[任务{scope}] {message.Text}" : message.Text;
        var isError = message.Level == LogLevel.Error;
        if (!closed)
        {
            Dispatcher.UIThread.Post(( ) => AppendLog(line.TrimEnd('\n'), isError));
        }
    }

    // 进度事件回投任务行：按 Scope（任务序号）定位任务状态，UI 线程更新
    private void OnProgress(WorkflowEvent evt)
    {
        switch (evt)
        {
            case ProgressRangeStartEvent start:
                // 新阶段（分 P 切换 / 重下）：重置 ETA 基准，覆盖首帧样本到达前的旧剩余时间残留
                ResetTaskEta(tasks.FirstOrDefault(t => t.Index.ToString( ) == start.Scope));
                break;
            case ProgressSampleEvent sample:
                if (tasks.FirstOrDefault(t => t.Index.ToString( ) == sample.Scope) is { } state)
                {
                    SetTaskSample(state, sample.Ratio, sample.Speed);
                }

                break;
            case ProgressRangeEndEvent:
                // 任务进入混流等无进度阶段：进度条停在满条，任务收尾时随 Status 隐藏，无需额外动作
                break;
        }
    }

    // 阶段开始即重置 ETA 基准（lastRatio / etaStart 仅 UI 线程读写，回投 UI 线程执行）
    private void ResetTaskEta(TaskState? state)
    {
        if (closed || state is null)
        {
            return;
        }

        Dispatcher.UIThread.Post(( ) =>
        {
            if (closed)
            {
                return;
            }

            state.lastRatio = 0;
            state.etaStart = DateTime.UtcNow;
        });
    }

    private void WindowClosed(object? o, EventArgs e)
    {
        MessageBus.Unsubscribe(OnLogMessage);
        ProgressBus.Unsubscribe(OnProgress);
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

    private void Dispatch(Action action)
    {
        Dispatcher.UIThread.Invoke(action);
    }

    /// <summary>进度总线采样回投进度与速度 / 剩余时间到 UI 线程（speed 由链路折算为每秒速率）。</summary>
    private void SetTaskSample(TaskState state, double ratio, double speed)
    {
        if (closed)
        {
            return;
        }

        Dispatcher.UIThread.Post(( ) =>
        {
            if (closed)
            {
                return;
            }

            state.Progress = Math.Clamp(ratio, 0, 1);
            var now = DateTime.UtcNow;
            // 进度回退视为分 P 切换，重置 ETA 基准
            if (state.lastRatio == 0 || ratio < state.lastRatio)
            {
                state.etaStart = now;
            }

            state.lastRatio = ratio;

            // speed 为链路折算的每秒速率
            var detail = speed > 0 ? Utils.FormatSpeed((long) speed, 1) : "";
            if (Utils.FormatEta(ratio, now - state.etaStart) is { } eta)
            {
                detail = detail.Length == 0 ? $"剩余 {eta}" : $"{detail} · 剩余 {eta}";
            }

            state.Detail = detail;
        });
    }

    private async Task<int> ExecuteTaskAsync(TaskState state, CancellationToken token)
    {
        // 调度循环在后台线程执行；日志经 MessageBus 转发，BeginScope 标注任务序号供日志区加 [任务 N] 前缀
        // 后处理路径已随 TaskParams 落入 DownloadRequest（PostProcessPath），按任务生效，无需进程级配置
        var req = state.Params.ToDownloadRequest(state.Url);
        // 调试日志是进程级开关（Config.DebugLog）：任一任务要求调试即开启，且只开不关，避免并发任务互相关闭
        if (req.Debug)
        {
            Config.SetDebugLog(true);
        }

        using (MessageBus.BeginScope(state.Index.ToString( )))
        {
            try
            {
                switch (state.Kind)
                {
                    case TaskKind.Opus:
                        await OpusDownload.RunAsync(req, token);
                        break;
                    case TaskKind.Live:
                        if (!LiveInputResolver.TryParse(state.Url, out var live))
                        {
                            throw new InvalidOperationException("直播地址解析失败");
                        }

                        await LiveDownload.RunAsync(req, live, token);
                        break;
                    default:
                    {
                        var sink = new PipelineSink(
                        Meta: info => SetTaskTitle(state, info.Title),
                        Saved: path => AppendProcessLog(state.Index, $"已保存：{path}", false));
                        await DownloadPipeline.RunAsync(req, sink, null, token);
                        break;
                    }
                }

                return 0;
            }
            catch (OperationCanceledException)
            {
                AppendProcessLog(state.Index, "已取消", false);
                throw;
            }
            catch (Exception e)
            {
                AppendProcessLog(state.Index, $"失败：{e.Message}", true);
                return 1;
            }
        }
    }

    /// <summary>解析出标题后回投到任务列表（替代裸 Url 展示）。</summary>
    private void SetTaskTitle(TaskState state, string title)
    {
        if (closed)
        {
            return;
        }

        Dispatcher.UIThread.Post(( ) =>
        {
            if (!closed)
            {
                state.Title = title;
            }
        });
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
