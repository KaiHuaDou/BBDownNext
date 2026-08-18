using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BBDown.Core.Tests;

public class ProgressSamplerTests
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

        using var sampler = new ProgressSampler((ratio, delta) => samples.Enqueue((ratio, delta)));
        sampler.Report(2.5, 4096);
        await WaitAsync(( ) => !samples.IsEmpty, ct);
        // 越界的比例被夹到 [0,1]
        Assert.All(samples, s => Assert.Equal(1, s.Ratio));

        var seen = samples.Count;
        sampler.Report(0.5, 10240);
        await WaitAsync(( ) => samples.Count > seen, ct);

        Assert.Equal(10240, samples.Sum(s => s.Delta));
    }

    [Fact]
    public async Task Dispose_IsIdempotentAndStopsSampling( )
    {
        var ct = TestContext.Current.CancellationToken;
        var count = 0;

        var sampler = new ProgressSampler((_, _) => Interlocked.Increment(ref count));
        sampler.Report(0.5, 1024);
        await WaitAsync(( ) => Volatile.Read(ref count) > 0, ct);

        sampler.Dispose( );
        sampler.Dispose( );
        var afterDispose = Volatile.Read(ref count);

        // 跨多个采样周期观测：定时器已停，计数不应再增长。
        // 逐周期断言以便真有额外采样时立即暴露（fail-fast），不再依赖固定延时窗
        for (var i = 0; i < 6; i++)
        {
            await Task.Delay(ProgressSampler.SampleInterval, ct);
            Assert.Equal(afterDispose, Volatile.Read(ref count));
        }
    }
}
