using System.Threading;
using System.Threading.Tasks;

namespace BBDown.Pipeline;

internal static class DownloadPipeline
{
    // WorkSetup.Build 内部有二进制查找等进程级初始化，串行化后 serve 并发任务不会互相踩踏（P1-16）
    private static readonly Lock workContextGate = new( );

    /// <summary>
    /// 下载主干：准备运行参数 → 解析视频信息 → 逐分 P 下载。CLI 与 serve 共用同一条链路，
    /// 差异只有 <paramref name="sink"/>（serve 用它回填标题与进度，CLI 传 default）。
    /// </summary>
    internal static async Task RunAsync(DownloadRequest req, PipelineSink sink = default, CancellationToken ct = default)
    {
        // 冲突消解结果要随请求贯穿到下载阶段（RunConfig 不含交互相关字段，Build 内部消费不到），在管道入口修正一次
        req = WorkSetup.HandleConflictingOptions(req);

        RunConfig runConfig;
        lock (workContextGate)
        {
            runConfig = WorkSetup.Build(req);
        }

        var (effectiveReq, fetch) = await VideoInfo.FetchAsync(req, runConfig, ct);
        sink.Meta?.Invoke(fetch.VInfo);

        await PageQueue.RunAsync(effectiveReq, runConfig, fetch, sink, ct);
    }
}
