using System;
using System.Text;
using System.Threading;

using BBDown.Core;
using BBDown.Core.Util;

namespace BBDown;

// 控制台进度条渲染器：接收 ProgressSampler 每 200 毫秒的采样回调，按 1/8 秒刷新一帧。
public sealed class ProgressBar : IDisposable
{
    private const int BarWidth = 40;
    private const string SpinnerFrames = @"|/-\";
    private static readonly TimeSpan RenderInterval = TimeSpan.FromSeconds(1.0 / 8);

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
    private bool disposed;
    private bool suspended;

    public ProgressBar(CancellationToken ct = default)
    {
        cancelToken = ct;
        if (drawToConsole)
        {
            renderTimer = new Timer(_ => Render( ));
            renderTimer.Change(RenderInterval, Timeout.InfiniteTimeSpan);
            // 退格重绘假定光标停在本行末尾，日志若直接跟在进度条后面会把光标推走，下一帧就把 spinner 打到日志行首。
            // 注册日志前置钩子：写日志前先擦掉进度条行，让日志从行首开始（与 LiveProgress 同一机制）。
            Logger.BeforeWrite = ClearLine;
            // 逐集确认 / 选轨等交互读输入前暂停渲染，避免进度条覆盖提示与用户输入
            Interaction.BeforeRead = Suspend;
            Interaction.AfterRead = Resume;
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

    // ProgressSampler 每秒回调一次，更新进度、速度与剩余时间显示
    public void OnSample(double value, long delta)
    {
        lock (gate)
        {
            ratio = value;
            var now = DateTime.UtcNow;
            // 进度回退视为分 P 切换，重置 ETA 基准
            if (lastRatio == 0 || value < lastRatio)
            {
                etaStart = now;
            }

            lastRatio = value;

            if (delta > 0)
            {
                speedText = $" - {Utils.FormatSpeed(delta, ProgressSampler.SampleInterval.TotalSeconds)}";
            }

            etaText = Utils.FormatEta(value, now - etaStart) is { } eta ? $" ETA {eta}" : string.Empty;
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
        // 先摘钩再拿 gate：Logger 持自身锁回调 ClearLine 拿本 gate，反向持 gate 去改 Logger 状态会死锁
        Logger.BeforeWrite = null;
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
