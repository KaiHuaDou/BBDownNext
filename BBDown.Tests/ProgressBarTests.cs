using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core;

namespace BBDown.Tests;

public class ProgressBarTests
{
    private static async Task WaitAsync(Func<bool> condition, CancellationToken ct)
    {
        for (var i = 0; i < 200 && !condition( ); i++)
        {
            await Task.Delay(50, ct);
        }
    }

    // 采样回调必须把累计字节数切成互不重叠的增量，否则 serve 模式累加出来的总量会漂
    [Fact]
    public async Task Sample_SplitsCumulativeBytesIntoDeltas( )
    {
        var ct = TestContext.Current.CancellationToken;
        var samples = new ConcurrentQueue<(double Ratio, long Delta)>( );

        using var bar = new ProgressBar((ratio, delta) => samples.Enqueue((ratio, delta)));
        bar.Report(2.5, 4096);
        await WaitAsync(( ) => !samples.IsEmpty, ct);
        // 越界的比例被夹到 [0,1]
        Assert.All(samples, s => Assert.Equal(1, s.Ratio));

        var seen = samples.Count;
        bar.Report(0.5, 10240);
        await WaitAsync(( ) => samples.Count > seen, ct);

        Assert.Equal(10240, samples.Sum(s => s.Delta));
    }

    [Fact]
    public async Task Dispose_IsIdempotentAndStopsSampling( )
    {
        var ct = TestContext.Current.CancellationToken;
        var count = 0;

        var bar = new ProgressBar((_, _) => Interlocked.Increment(ref count));
        bar.Report(0.5, 1024);
        await WaitAsync(( ) => Volatile.Read(ref count) > 0, ct);

        bar.Dispose( );
        bar.Dispose( );
        var afterDispose = Volatile.Read(ref count);

        await Task.Delay(TimeSpan.FromSeconds(1.5), ct);
        Assert.Equal(afterDispose, Volatile.Read(ref count));
    }

    [Fact]
    public void ApplySample_KeepsLastSpeedWhenNothingArrived( )
    {
        var task = new DownloadTask(new ResourceId.Av(1), "https://example.com", 0);

        task.ApplySample(0.3, 2048);
        task.ApplySample(0.5, 0);

        Assert.Equal(0.5, task.Progress);
        Assert.Equal(2048, task.DownloadSpeed);
        Assert.Equal(2048, task.TotalDownloadedBytes);
    }

    [Fact]
    public void ApplySample_AccumulatesTotalBytes( )
    {
        var task = new DownloadTask(new ResourceId.Av(1), "https://example.com", 0);

        task.ApplySample(0.3, 2048);
        task.ApplySample(0.6, 1024);

        Assert.Equal(1024, task.DownloadSpeed);
        Assert.Equal(3072, task.TotalDownloadedBytes);
    }
}
