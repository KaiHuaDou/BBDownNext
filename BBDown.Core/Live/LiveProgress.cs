using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading;

using BBDown.Core.Util;

using static BBDown.Core.Logger;

namespace BBDown.Core.Live;

/// <summary>
/// 直播录制的状态行。不能复用 <see cref="ProgressBar"/>：它的 Report 接受 0–1 完成比例，
/// 而直播没有总量。这里改为展示「已录时长 / 已写体积 / 瞬时速度」。
/// 用 \r 原地刷新单行，比逐字符退格重写更简单也更稳。
/// </summary>
public sealed class LiveProgress : IDisposable
{
    private static readonly TimeSpan RenderInterval = TimeSpan.FromSeconds(0.5);
    private static readonly TimeSpan SampleInterval = TimeSpan.FromSeconds(1);
    // 输出重定向时状态行改为定期打日志，否则日志文件里只会剩最后一行
    private static readonly TimeSpan RedirectedLogInterval = TimeSpan.FromSeconds(60);

    private readonly Stopwatch elapsed = Stopwatch.StartNew( );
    private readonly string qualityText;
    private readonly bool drawToConsole = !Console.IsOutputRedirected;
    private readonly Lock gate = new( );
    private readonly Timer? renderTimer;
    private readonly Timer sampleTimer;

    // 写盘线程高频累加，定时器线程读
    private long totalBytes;
    private int segmentIndex;

    // 以下字段只在持有 gate 时访问
    private string renderedText = string.Empty;
    private string speedText = "0.00 KB/s";
    private long lastSampledBytes;
    private DateTime lastRedirectedLog = DateTime.Now;
    private bool disposed;

    public LiveProgress(string qualityText)
    {
        this.qualityText = qualityText;
        if (drawToConsole)
        {
            renderTimer = new Timer(_ => Render( ));
            Schedule(renderTimer, RenderInterval);
            Logger.BeforeWrite = ClearLine;
        }

        sampleTimer = new Timer(_ => Sample( ));
        Schedule(sampleTimer, SampleInterval);
    }

    public long TotalBytes => Interlocked.Read(ref totalBytes);

    public void Add(long bytes)
    {
        Interlocked.Add(ref totalBytes, bytes);
    }

    public void StartSegment(int index)
    {
        Interlocked.Exchange(ref segmentIndex, index);
    }

    /// <summary>
    /// 擦掉状态行，让紧随其后的日志从行首开始。日志打完由下一帧自动重画。
    /// </summary>
    public void ClearLine( )
    {
        // 提前返回不只是省事：重定向时 Sample 会在持有 gate 的情况下调 Log，
        // 而 Log 又会回调到这里，先拿 gate 再拿 Logger 锁与反向顺序撞上就是死锁。
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

    private void Sample( )
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            var total = Interlocked.Read(ref totalBytes);
            var delta = Math.Max(total - lastSampledBytes, 0);
            lastSampledBytes = total;
            speedText = $"{Utils.FormatFileSize(delta)}/s";

            if (!drawToConsole && DateTime.Now - lastRedirectedLog >= RedirectedLogInterval)
            {
                lastRedirectedLog = DateTime.Now;
                Log(Compose(total));
            }

            Schedule(sampleTimer, SampleInterval);
        }
    }

    private void Render( )
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            Draw(Compose(Interlocked.Read(ref totalBytes)));
            Schedule(renderTimer, RenderInterval);
        }
    }

    private string Compose(long total)
    {
        var clock = elapsed.Elapsed.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
        return $"录制中 {clock} | {Utils.FormatFileSize(total)} | {speedText} | 分段 {Volatile.Read(ref segmentIndex)} | {qualityText}";
    }

    // \r 回到行首整行重写，比逐字符退格重写简单，也不会因长度变化吐出乱码。
    // 新内容比旧内容短时，用空格补齐旧字符，避免上一帧的残余留在屏幕上。
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

    private static void Schedule(Timer? timer, TimeSpan dueTime)
    {
        timer?.Change(dueTime, Timeout.InfiniteTimeSpan);
    }

    public void Dispose( )
    {
        Logger.BeforeWrite = null;
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            Draw(string.Empty);
            renderTimer?.Dispose( );
            sampleTimer.Dispose( );
            elapsed.Stop( );
        }
    }
}
