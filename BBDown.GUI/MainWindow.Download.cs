using System;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core;
using BBDown.Core.Download;
using BBDown.Core.Live;
using BBDown.Core.Logging;
using BBDown.Core.Pipeline;
using BBDown.Core.Util;

namespace BBDown.GUI;

/// <summary>单任务下载执行，控制 MainWindow.axaml.cs 行数。</summary>
public partial class MainWindow
{
    /// <summary>调度循环在后台线程执行；日志经 MessageBus 转发，BeginScope 标注任务序号供日志区加 [任务 N] 前缀。
    /// 后处理路径已随 TaskParams 落入 DownloadRequest（PostProcessPath），按任务生效，无需进程级配置。</summary>
    private async Task<int> ExecuteTaskAsync(TaskState state, CancellationToken token)
    {
        var req = state.Params.ToDownloadRequest(state.Url);
        // 调试日志是进程级开关（Config.DebugLog）：任一任务要求调试即开启，且只开不关，避免并发任务互相关闭
        if (req.Debug)
        {
            Config.SetDebugLog(true);
        }

        using (MessageBus.BeginScope(state.Index.ToString( )))
        {
            try
            {
                // 直播录制以任务序号注册会话（LiveSignal），停止按钮按序号精准停录，不经统一分发
                if (state.Kind == TaskKind.Live)
                {
                    if (!LiveInputResolver.TryParse(state.Url, out var live))
                    {
                        throw new InvalidOperationException("直播地址解析失败");
                    }

                    var liveSink = MakeSink(state);
                    await LiveDownload.RunAsync(req, live, state.Index.ToString( ), liveSink, ct: token);
                }
                else
                {
                    // b23 短链先展开再识别形态（与 CLI RunApp 一致），否则集合形态的短链会误入视频管道
                    var url = state.Url;
                    if (url.Contains("b23.tv", StringComparison.OrdinalIgnoreCase))
                    {
                        url = await HTTPUtil.GetWebLocationAsync(url, token);
                        req = req with { Url = url };
                    }

                    // 独立链路（专栏 / 文集 / 空间图文 / 音频 / 动态）统一经 WorkerDispatcher，与 CLI / serve 同一分发点
                    if (InputResolver.TryDispatch(url, out var id))
                    {
                        await WorkerDispatcher.RunAsync(id, req, MakeSink(state), null, token);
                    }
                    else
                    {
                        await DownloadPipeline.RunAsync(req, MakeSink(state), null, token);
                    }
                }

                return 0;
            }
            catch (OperationCanceledException)
            {
                AppendProcessLog(state.Index, "已取消", false);
                throw;
            }
            catch (Exception e)
            {
                AppendProcessLog(state.Index, $"失败：{e.Message}", true);
                return 1;
            }
        }
    }

    private PipelineSink MakeSink(TaskState state)
    {
        return new(
        Meta: info => SetTaskTitle(state, info.Title),
        Saved: path => AppendProcessLog(state.Index, $"已保存：{path}", false));
    }
}
