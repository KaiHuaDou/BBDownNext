using System;
using System.Threading;

using BBDown.Core.Logging;
using BBDown.Core.Util;
using BBDown.Core.Workflow;

using static BBDown.Core.Logger;

namespace BBDown.Cli;

/// <summary>
/// 直播录制状态行：订阅 ProgressBus 的阶段事件渲染单行状态（CLI 专属）。
/// 样本 Detail 承载「时长 / 分段 / 清晰度」，体积与速度取自样本字段；\r 原地刷新单行。
/// </summary>
public sealed class LiveProgress : IDisposable
{
    private static readonly TimeSpan RenderInterval = TimeSpan.FromSeconds(0.5);
    // 输出重定向时状态行改为定期打日志，否则日志文件里只会剩最后一行
    private static readonly TimeSpan RedirectedLogInterval = TimeSpan.FromSeconds(60);

    private readonly bool drawToConsole = !Console.IsOutputRedirected;
    private readonly Lock gate = new( );
    private readonly Timer? renderTimer;

    // 以下字段只在持有 gate 时访问
    private ProgressSampleEvent? sample;
    private string renderedText = string.Empty;
    private DateTime lastRedirectedLog = DateTime.Now;
    private bool rendering;
    private bool disposed;

    public LiveProgress( )
    {
        ProgressBus.Subscribe(OnProgress);
        if (drawToConsole)
        {
            renderTimer = new Timer(_ => Render( ));
            renderTimer.Change(RenderInterval, Timeout.InfiniteTimeSpan);
            ConsoleHost.BeforeWrite = ClearLine;
        }
    }

    // 阶段边界驱动显隐，样本驱动更新；重定向时定期落一行日志
    private void OnProgress(WorkflowEvent evt)
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            switch (evt)
            {
                case ProgressRangeStartEvent:
                    sample = null;
                    rendering = true;
                    renderTimer?.Change(RenderInterval, Timeout.InfiniteTimeSpan);
                    break;
                case ProgressSampleEvent value:
                    sample = value;
                    rendering = true;
                    renderTimer?.Change(RenderInterval, Timeout.InfiniteTimeSpan);
                    if (!drawToConsole && DateTime.Now - lastRedirectedLog >= RedirectedLogInterval)
                    {
                        lastRedirectedLog = DateTime.Now;
                        Log(Compose(value));
                    }

                    break;
                case ProgressRangeEndEvent:
                    Draw(string.Empty);
                    renderTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                    rendering = false;
                    break;
            }
        }
    }

    private void Render( )
    {
        lock (gate)
        {
            if (disposed || !rendering || sample is not { } current)
            {
                return;
            }

            Draw(Compose(current));
            renderTimer?.Change(RenderInterval, Timeout.InfiniteTimeSpan);
        }
    }

    // 行内容：Detail（时长 / 分段 / 清晰度）+ 体积 + 速度
    private static string Compose(ProgressSampleEvent value)
    {
        return $"{value.Detail} | {Utils.FormatFileSize(value.TotalBytes)} | {Utils.FormatSpeed((long) value.Speed, 1)}";
    }

    /// <summary>
    /// 擦掉状态行，让紧随其后的日志从行首开始。日志打完由下一帧自动重画。
    /// </summary>
    public void ClearLine( )
    {
        // 提前返回不只是省事：重定向时采样会调 Log，而 Log 又会回调到这里，
        // 先拿 gate 再拿 Logger 锁与反向顺序撞上就是死锁。
        // 这条分支保证「Logger 锁 → gate」只可能发生在 drawToConsole 为真时。
        if (!drawToConsole)
        {
            return;
        }

        lock (gate)
        {
            if (!disposed)
            {
                Draw(string.Empty);
            }
        }
    }

    // \r 回到行首整行重写；新内容比旧内容短时用空格补齐，避免上一帧的残余留在屏幕上
    private void Draw(string text)
    {
        if (!drawToConsole)
        {
            return;
        }

        if (text.Length == 0)
        {
            Console.Write("\r" + new string(' ', renderedText.Length) + "\r");
            renderedText = string.Empty;
            return;
        }

        Console.Write("\r" + text);
        if (text.Length < renderedText.Length)
        {
            Console.Write(new string(' ', renderedText.Length - text.Length));
        }

        renderedText = text;
    }

    public void Dispose( )
    {
        ProgressBus.Unsubscribe(OnProgress);
        ConsoleHost.BeforeWrite = null;
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            Draw(string.Empty);
            renderTimer?.Dispose( );
        }
    }
}
