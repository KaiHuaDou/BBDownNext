using System;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core.Download;
using BBDown.Core.Workflow;

namespace BBDown.Core.Pipeline;

public static class DownloadPipeline
{
    /// <summary>
    /// 下载主干：准备运行参数 → 解析视频信息 → 逐分 P 下载。CLI 与 serve 共用同一条链路，
    /// 差异只有 <paramref name="sink"/>（serve 用它回填标题与进度，CLI 传 default）与
    /// <paramref name="workflow"/>（serve 用它外发消息 / 进度事件，CLI 传 null 保持控制台输出）。
    /// </summary>
    public static async Task RunAsync(DownloadRequest req, PipelineSink sink = default, IWorkflowContext? workflow = null, CancellationToken ct = default)
    {
        // 冲突消解结果要随请求贯穿到下载阶段（RunConfig 不含交互相关字段，Build 内部消费不到），在管道入口修正一次
        req = WorkSetup.HandleConflictingOptions(req);

        var runConfig = WorkSetup.Build(req);

        var (effectiveReq, fetch) = await VideoInfo.FetchAsync(req, runConfig, ct);
        sink = ComposeSink(sink, workflow);
        sink.Meta?.Invoke(fetch.VInfo);

        await PageQueue.RunAsync(effectiveReq, runConfig, fetch, sink, ct);
    }

    // 把任务事件能力并进 sink：Meta / Saved 除原回调外镜像为任务消息（仅 serve 有 workflow；
    // CLI 传 null 原样返回）。进度统一走 ProgressBus，不在此处理。
    internal static PipelineSink ComposeSink(PipelineSink sink, IWorkflowContext? workflow)
    {
        if (workflow is null)
        {
            return sink;
        }

        return new PipelineSink(
            v =>
            {
                sink.Meta?.Invoke(v);
                workflow.EnqueueMessage($"任务信息：{v.Title}", DateTimeOffset.Now);
            },
            p =>
            {
                sink.Saved?.Invoke(p);
                workflow.EnqueueMessage($"已保存：{p}", DateTimeOffset.Now);
            });
    }
}
