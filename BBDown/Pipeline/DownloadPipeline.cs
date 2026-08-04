using System.Threading;
using System.Threading.Tasks;

namespace BBDown.Pipeline;

internal static class DownloadPipeline
{
    // WorkSetup.Build 内部有二进制查找等进程级初始化，串行化后 serve 并发任务不会互相踩踏（P1-16）
    private static readonly Lock workContextGate = new( );

    /// <summary>
    /// 下载主干：准备运行参数 → 解析视频信息 → 逐分 P 下载。CLI 与 serve 共用同一条链路，
    /// 差异只有 <paramref name="relatedTask"/>（serve 用它回填标题与进度）。
    /// </summary>
    internal static async Task RunAsync(DownloadOptions myOption, DownloadTask? relatedTask = null, CancellationToken ct = default)
    {
        WorkContext ctx;
        lock (workContextGate)
        {
            ctx = WorkSetup.Build(myOption);
        }

        ctx = await VideoInfo.FetchAsync(myOption, ctx, ct);
        if (relatedTask is not null)
        {
            relatedTask.Title = ctx.VInfo!.Title;
            relatedTask.Pic = ctx.VInfo.Pic;
            relatedTask.VideoPubTime = ctx.VInfo.PubTime;
        }

        await PageQueue.RunAsync(myOption, ctx, relatedTask, ct);
    }
}
