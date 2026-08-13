using System;
using System.Text;
using System.Threading;

using BBDown.Core.Util;

namespace BBDown;

// 控制台进度条渲染器：接收 ProgressSampler 每秒的采样回调，按 1/8 秒刷新一帧。
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

    public ProgressBar(CancellationToken ct = default)
    {
        cancelToken = ct;
        if (drawToConsole)
        {
            renderTimer = new Timer(_ => Render( ));
            renderTimer.Change(RenderInterval, Timeout.InfiniteTimeSpan);
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
                speedText = $" - {Utils.FormatSpeed(delta)}";
            }

            etaText = Utils.FormatEta(value, now - etaStart) is { } eta ? $" ETA {eta}" : string.Empty;
        }
    }

    private void Render( )
    {
        lock (gate)
        {
            if (disposed || cancelToken.IsCancellationRequested)
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
