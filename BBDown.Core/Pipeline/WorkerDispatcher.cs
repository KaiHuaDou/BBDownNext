using System.Threading;
using System.Threading.Tasks;

using BBDown.Core.Download;
using BBDown.Core.Live;
using BBDown.Core.Workflow;

namespace BBDown.Core.Pipeline;

/// <summary>
/// 资源类型 → 执行器的唯一分发点：CLI（RunApp）与 serve（TaskWorker）共用，
/// 消除两套形态分流。直播 / 专栏 / 音频 / 集合为独立链路（不构造 WorkContext、
/// 不探测 ffmpeg 的链路自行探测），其余（视频 / 列表）走视频下载管道。
/// </summary>
public static class WorkerDispatcher
{
    public static async Task RunAsync(ResourceId id, DownloadRequest req, PipelineSink sink, ChannelWorkflowContext? workflow, CancellationToken ct = default)
    {
        switch (id)
        {
            case ResourceId.LiveRoom room:
                // sessionId 供 LiveSignal 停录（Ctrl+Break / 停止端点按此定位录制会话）
                await LiveDownload.RunAsync(req, new LiveTarget(room.RoomId.ToString( )), ResourceIdJsonConverter.Format(id), sink, ct);
                break;
            case ResourceId.OpusArticle:
                await OpusDownload.RunAsync(req, sink, ct);
                break;
            case ResourceId.ReadList rl:
                await ReadListDownload.RunAsync(rl.RlId, req, sink, ct);
                break;
            case ResourceId.SpaceOpus so:
                await SpaceOpusDownload.RunAsync(so.Mid, req, sink, ct);
                break;
            case ResourceId.SpaceDynamic sd:
                // /dynamic 与 /upload/opus 共用同一实现（同一动态流数据源，均仅提取图文）
                await SpaceOpusDownload.RunAsync(sd.Mid, req, sink, ct);
                break;
            case ResourceId.SpaceAudio sa:
                await SpaceAudioDownload.RunAsync(sa.Mid, req, sink, ct);
                break;
            default:
                // workflow 仅对视频管道有意义（serve 的事件流上下文），非视频域不传
                await DownloadPipeline.RunAsync(req, sink, workflow, ct);
                break;
        }
    }
}
