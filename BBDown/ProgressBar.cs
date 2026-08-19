using System;
using System.Text;
using System.Threading;

using BBDown.Core;
using BBDown.Core.Logging;
using BBDown.Core.Util;
using BBDown.Core.Workflow;

namespace BBDown;

// 控制台进度条渲染器：订阅 ProgressBus 的进度事件（阶段开始/样本/结束），按 1/8 秒刷新一帧。
public sealed class ProgressBar : IDisposable
{
    private const int BarWidth = 40;
    private const string SpinnerFrames = @"|/-\";
    private static readonly TimeSpan RenderInterval = TimeSpan.FromSeconds(1.0 / 8);
    // 采样间隔 200ms；超过 1 秒无新采样视为下载已结束（进入混流等阶段），清行停止渲染
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(1);

    private readonly Lock gate = new( );
    private readonly Timer? renderTimer;
    private readonly CancellationToken cancelToken;
    private readonly bool drawToConsole = !Console.IsOutputRedirected;

    // 以下字段只在持有 gate 时访问
    private double ratio;
    private string speedText = string.Empty;
    private string etaText = string.Empty;
    private string renderedText = string.Empty;
    private int spinnerIndex;
    private DateTime etaStart;
    private double lastRatio;
    private long lastSampleTick;
    private bool downloading;
    private bool rendering;
    private bool disposed;
    private bool suspended;

    public ProgressBar(CancellationToken ct = default)
    {
        cancelToken = ct;
        ProgressBus.Subscribe(OnProgress);
        if (drawToConsole)
        {
            renderTimer = new Timer(_ => Render( ));
            renderTimer.Change(RenderInterval, Timeout.InfiniteTimeSpan);
            // 退格重绘假定光标停在本行末尾，日志若直接跟在进度条后面会把光标推走，下一帧就把 spinner 打到日志行首。
            // 注册日志前置钩子：写日志前先擦掉进度条行，让日志从行首开始（与 LiveProgress 同一机制）。
            ConsoleHost.BeforeWrite = ClearLine;
            // 逐集确认 / 选轨等交互读输入前暂停渲染，避免进度条覆盖提示与用户输入
            Interaction.BeforeRead = Suspend;
            Interaction.AfterRead = Resume;
        }
    }

    // 进度事件分发：阶段开始/结束驱动进度条显隐，样本驱动更新
    private void OnProgress(WorkflowEvent evt)
    {
        switch (evt)
        {
            case ProgressRangeStartEvent:
                SetDownloading(true);
                break;
            case ProgressSampleEvent sample:
                OnSample(sample);
                break;
            case ProgressRangeEndEvent:
                SetDownloading(false);
                break;
        }
    }

    // 交互读输入前暂停：停掉渲染定时器并擦掉当前行，让提示与用户输入独占本行
    public void Suspend( )
    {
        if (!drawToConsole)
        {
            return;
        }

        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            suspended = true;
            renderTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            Draw(string.Empty);
        }
    }

    // 交互读输入后恢复渲染
    public void Resume( )
    {
        if (!drawToConsole)
        {
            return;
        }

        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            suspended = false;
            renderTimer?.Change(RenderInterval, Timeout.InfiniteTimeSpan);
        }
    }

    // 擦掉进度条行，让紧随其后的日志从行首开始。日志打完由下一帧自动重画。
    public void ClearLine( )
    {
        // 重定向时不渲染进度条，无事可擦，直接跳过避免无谓锁竞争
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

    // 主媒体下载窗口：true 进入下载（恢复渲染），false 下载结束（清行停止渲染）。
    // 解析 / 混流 / 封面弹幕等附属下载都不开窗，进度条只在明确下载音视频文件时出现
    private void SetDownloading(bool value)
    {
        if (!drawToConsole)
        {
            return;
        }

        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            downloading = value;
            if (value)
            {
                // 标记本帧为“刚采样”，避免 Render 在首个真实采样到达前误判空闲而清行
                lastSampleTick = Environment.TickCount64;
                rendering = true;
                renderTimer?.Change(RenderInterval, Timeout.InfiniteTimeSpan);
            }
            else
            {
                Draw(string.Empty);
                renderTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                rendering = false;
            }
        }
    }

    // 阶段内样本每 200ms 到达一次，更新进度、速度与剩余时间显示（speed 由链路折算好）
    private void OnSample(ProgressSampleEvent sample)
    {
        lock (gate)
        {
            // 窗口外（封面/弹幕等附属下载）不驱动进度条
            if (!downloading)
            {
                return;
            }

            ratio = sample.Ratio;
            lastSampleTick = Environment.TickCount64;
            var now = DateTime.UtcNow;
            // 进度回退视为分 P 切换，重置 ETA 基准
            if (lastRatio == 0 || sample.Ratio < lastRatio)
            {
                etaStart = now;
            }

            lastRatio = sample.Ratio;

            if (sample.Speed > 0)
            {
                speedText = $" - {Utils.FormatSpeed((long) sample.Speed, 1)}";
            }

            etaText = Utils.FormatEta(sample.Ratio, now - etaStart) is { } eta ? $" ETA {eta}" : string.Empty;

            // 有采样即视为下载进行中：若此前因空闲停过渲染，恢复定时器
            if (!rendering)
            {
                rendering = true;
                renderTimer?.Change(RenderInterval, Timeout.InfiniteTimeSpan);
            }
        }
    }

    private void Render( )
    {
        lock (gate)
        {
            if (disposed || cancelToken.IsCancellationRequested || suspended)
            {
                return;
            }

            // 窗口外或下载结束（进入混流等阶段）后采样停止：擦掉残留的进度条并停掉渲染。
            // 采样停止的空闲判定是兜底，正常路径由 SetDownloading(false) 即时清行
            if (!downloading || Environment.TickCount64 - lastSampleTick > IdleTimeout.TotalMilliseconds)
            {
                Draw(string.Empty);
                rendering = false;
                return;
            }

            var filled = (int) (ratio * BarWidth);
            spinnerIndex = (spinnerIndex + 1) % SpinnerFrames.Length;
            Draw($"             [{new string('#', filled)}{new string('-', BarWidth - filled)}] {ratio * 100,3:0.00}% {SpinnerFrames[spinnerIndex]}{speedText}{etaText}");
            renderTimer?.Change(RenderInterval, Timeout.InfiniteTimeSpan);
        }
    }

    // 只回退并重写与上一帧不同的那段后缀，整行重画会闪。
    private void Draw(string text)
    {
        if (!drawToConsole || cancelToken.IsCancellationRequested)
        {
            return;
        }

        var commonPrefixLength = 0;
        var commonLength = Math.Min(renderedText.Length, text.Length);
        while (commonPrefixLength < commonLength && text[commonPrefixLength] == renderedText[commonPrefixLength])
        {
            commonPrefixLength++;
        }

        StringBuilder output = new( );
        output.Append('\b', renderedText.Length - commonPrefixLength);
        output.Append(text[commonPrefixLength..]);

        // 新内容更短时，多出来的旧字符要用空格抹掉
        var overlapCount = renderedText.Length - text.Length;
        if (overlapCount > 0)
        {
            output.Append(' ', overlapCount);
            output.Append('\b', overlapCount);
        }

        Console.Write(output);
        renderedText = text;
    }

    public void Dispose( )
    {
        // 先摘钩再拿 gate：渲染器持自身锁回调 ClearLine 拿本 gate，反向持 gate 去改渲染器状态会死锁
        ProgressBus.Unsubscribe(OnProgress);
        ConsoleHost.BeforeWrite = null;
        Interaction.BeforeRead = null;
        Interaction.AfterRead = null;
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
