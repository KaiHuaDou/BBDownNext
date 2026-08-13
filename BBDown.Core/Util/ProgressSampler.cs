using System;
using System.Threading;

namespace BBDown.Core.Util;

// 进度采样器：把下载线程高频的 Report 降频为每秒一次的 onSample 回吐（总进度，本周期新增字节数），
// 供 serve / GUI 等控制台之外的观察者获取进度；onSample 为 null 时只记录进度不再采样。
public sealed class ProgressSampler : IDisposable
{
    private static readonly TimeSpan SampleInterval = TimeSpan.FromSeconds(1);

    // 下载线程高频写、定时器线程读，只能通过 Interlocked/Volatile 访问
    private double progressRatio;
    private long downloadedBytes;

    private readonly Lock gate = new( );
    private readonly Timer? sampleTimer;
    private readonly Action<double, long>? onSample;
    private long lastSampledBytes;
    private bool disposed;

    public ProgressSampler(Action<double, long>? onSample = null)
    {
        this.onSample = onSample;
        if (onSample is not null)
        {
            sampleTimer = new Timer(_ => Sample( ));
            sampleTimer.Change(SampleInterval, Timeout.InfiniteTimeSpan);
        }
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

    // 无比例场景（如直播没有总量）只报累计字节，ratio 保持 0
    public void Report(long downloaded)
    {
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
            onSample?.Invoke(Volatile.Read(ref progressRatio), delta);
            sampleTimer?.Change(SampleInterval, Timeout.InfiniteTimeSpan);
        }
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
            sampleTimer?.Dispose( );
        }
    }
}
