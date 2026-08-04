using System;
using System.Text;
using System.Threading;

namespace BBDown.Util;

internal sealed class ProgressBar : IDisposable, IProgress<double>
{
    private const int BarWidth = 40;
    private const string SpinnerFrames = @"|/-\";
    private static readonly TimeSpan RenderInterval = TimeSpan.FromSeconds(1.0 / 8);
    private static readonly TimeSpan SampleInterval = TimeSpan.FromSeconds(1);

    // 下载线程高频写、定时器线程读，只能通过 Interlocked/Volatile 访问
    private double progressRatio;
    private long downloadedBytes;

    private readonly Lock gate = new( );
    private readonly Timer? renderTimer;
    private readonly Timer? sampleTimer;
    private readonly Action<double, long>? onSample;
    private readonly bool drawToConsole = !Console.IsOutputRedirected;
    private readonly CancellationToken cancelToken;

    // 以下字段只在持有 gate 时访问
    private string renderedText = string.Empty;
    private string speedText = string.Empty;
    private long lastSampledBytes;
    private int spinnerIndex;
    private bool disposed;

    // 每个采样周期回调一次 (总进度，本周期新增字节数)，供控制台之外的观察者（如 serve 模式的下载任务）
    // 获取进度；为 null 时进度条只负责画控制台。
    public ProgressBar(Action<double, long>? onSample = null, CancellationToken ct = default)
    {
        this.onSample = onSample;
        cancelToken = ct;
        // 输出被重定向时画进度条只会往目标文件里灌一堆退格符，所以不画；
        // 但观察者不关心 stdout 去了哪，此时仍需继续采样
        if (drawToConsole)
        {
            renderTimer = new Timer(_ => Render( ));
        }

        if (drawToConsole || onSample is not null)
        {
            sampleTimer = new Timer(_ => Sample( ));
        }

        Schedule(renderTimer, RenderInterval);
        Schedule(sampleTimer, SampleInterval);
    }

    public void Report(double value)
    {
        Interlocked.Exchange(ref progressRatio, Math.Clamp(value, 0, 1));
    }

    public void Report(double value, long downloaded)
    {
        Report(value);
        Interlocked.Exchange(ref downloadedBytes, downloaded);
    }

    private void Sample( )
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            // 只读一次：重复读会把两次读取之间新到的字节记进 lastSampledBytes 却没算进 delta，导致累计值偏少
            var total = Interlocked.Read(ref downloadedBytes);
            var delta = Math.Max(total - lastSampledBytes, 0);
            lastSampledBytes = total;
            if (delta > 0)
            {
                speedText = $" - {Utils.FormatFileSize(delta)}/s";
            }

            onSample?.Invoke(Volatile.Read(ref progressRatio), delta);
            Schedule(sampleTimer, SampleInterval);
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

            var ratio = Volatile.Read(ref progressRatio);
            var filled = (int) (ratio * BarWidth);
            spinnerIndex = (spinnerIndex + 1) % SpinnerFrames.Length;
            Draw($"             [{new string('#', filled)}{new string('-', BarWidth - filled)}] {ratio * 100,3:0.00}% {SpinnerFrames[spinnerIndex]}{speedText}");
            Schedule(renderTimer, RenderInterval);
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

    // 只在持有 gate 且未 disposed 时调用：Timer.Change 在 Dispose 之后会抛 ObjectDisposedException
    private static void Schedule(Timer? timer, TimeSpan dueTime)
    {
        timer?.Change(dueTime, Timeout.InfiniteTimeSpan);
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
            sampleTimer?.Dispose( );
        }
    }
}
