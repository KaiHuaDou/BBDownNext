using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

using System.Runtime.InteropServices;

using Ookii.Dialogs.Wpf;

namespace BBDown.GUI;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<TaskState> tasks = [];
    private readonly QueueRunner queue;
    private readonly Brush okBrush = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
    private readonly Brush badBrush = new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35));
    private readonly Brush hintBrush = new SolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E));
    private string lastConcurrency = "3";

    public MainWindow( )
    {
        InitializeComponent( );
        // RichTextBox 初始 Document 自带一个空段落，清掉避免日志顶部出现空行
        LogBox.Document.Blocks.Clear( );
        ApiBox.ItemsSource = new[] { "web", "tv", "app", "intl" };
        foreach ((var value, var label) in LiveQualityChoices)
        {
            LiveQualityBox.Items.Add(new ComboBoxItem { Content = label, Tag = value });
        }

        foreach ((var value, var label) in MuxChoices)
        {
            MuxBox.Items.Add(new ComboBoxItem { Content = label, Tag = value });
        }

        MuxBox.SelectedIndex = 0;

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
            Rect bounds = WindowState == WindowState.Minimized ? RestoreBounds : new Rect(Left, Top, Width, Height);
            ConfigData config = new( )
            {
                Options = ReadOptions( ),
                ExePath = ExePathBox.Text.Trim( ),
                Concurrency = int.TryParse(ConcurrencyBox.Text, out var value) ? value : 3,
                WindowLeft = bounds.Left,
                WindowTop = bounds.Top,
                WindowWidth = bounds.Width,
                WindowHeight = bounds.Height,
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
        VistaOpenFileDialog dialog = new( ) { Filter = "可执行文件 (*.exe)|*.exe", FileName = ExePathBox.Text.Trim( ) };
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

    private void OpenDirButtonClicked(object o, RoutedEventArgs e)
    {
        var dir = WorkDirBox.Text.Trim( );
        if (dir.Length > 0 && Directory.Exists(dir))
        {
            _ = Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
            return;
        }

        var fallback = Path.GetDirectoryName(Environment.ProcessPath) ?? "";
        if (Directory.Exists(fallback))
        {
            _ = Process.Start(new ProcessStartInfo(fallback) { UseShellExecute = true });
        }
        else
        {
            AppendLog("输出目录不存在，请先在“工作目录”填写有效路径");
        }
    }

    /// <summary>非负整数字段失焦校验，无效回退上次有效值（存于 Tag）。</summary>
    private void IntegerBoxLostKeyboardFocus(object o, KeyboardFocusChangedEventArgs e)
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

    private void WindowDragOver(object o, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.Text) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void WindowDrop(object o, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.Text) is string text)
        {
            TargetBox.Text = text.Trim( );
        }
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

            UpdateExeHint( );
            return;
        }

        ApplyOptions(config.Options);
        ExePathBox.Text = config.ExePath;
        ConcurrencyBox.Text = config.Concurrency.ToString( );
        lastConcurrency = ConcurrencyBox.Text;
        ApplyWindowBounds(config);
        UpdateExeHint( );
    }

    private void ApplyWindowBounds(ConfigData config)
    {
        if (config.WindowLeft is not { } left || config.WindowTop is not { } top ||
            config.WindowWidth is not { } width || config.WindowHeight is not { } height ||
            !IsOnScreen(left, top, width, height))
        {
            return;
        }

        Left = left;
        Top = top;
        Width = width;
        Height = height;
    }

    private static bool IsOnScreen(double left, double top, double width, double height)
    {
        // 虚拟屏幕覆盖多显示器；窗口至少留 100px 可见才算在屏内
        var screenLeft = GetSystemMetrics(SM_XVIRTUALSCREEN);
        var screenTop = GetSystemMetrics(SM_YVIRTUALSCREEN);
        var screenWidth = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        var screenHeight = GetSystemMetrics(SM_CYVIRTUALSCREEN);
        return left + width > screenLeft && left < screenLeft + screenWidth - 100 &&
               top + height > screenTop && top < screenTop + screenHeight - 100;
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;

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
