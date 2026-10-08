using System;
using System.Threading;
using System.Threading.Tasks;

using Avalonia.Threading;

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
    /// <summary>调度循环在后台线程执行；日志经 MessageBus 转发，BeginScope 标注任务序号供日志区加 [任务 N] 前缀
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
                // b23 短链先展开再识别形式（与 CLI RunApp 一致），否则直播 / 集合形式的短链会误入视频管道
                var url = state.Url;
                if (url.Contains("b23.tv", StringComparison.OrdinalIgnoreCase))
                {
                    url = await HTTPUtil.GetWebLocationAsync(url, token);
                    req = req with { Url = url };
                }

                // 直播单独链路：录制会话以任务序号注册（LiveSignal），停止按钮按序号精准停录
                // 直播形式以展开后的 url 重判，入队时的 Kind 不可作为路由依据
                if (LiveInputResolver.TryParse(url, out var live))
                {
                    MarkLive(state);
                    await LiveDownload.RunAsync(req, live, state.Index.ToString( ), MakeSink(state), ct: token);
                }
                else if (InputResolver.TryDispatch(url, out var id))
                {
                    await WorkerDispatcher.RunAsync(id, req, MakeSink(state), null, token);
                }
                else
                {
                    await DownloadPipeline.RunAsync(req, MakeSink(state), null, token);
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

    // b23 短链展开后才暴露直播形式：回投 UI 线程补记 Kind，停止按钮 / 不确定进度条按整项绑定的转换器随之联动
    private static void MarkLive(TaskState state)
    {
        if (state.Kind == TaskKind.Live)
        {
            return;
        }

        Dispatcher.UIThread.Post(( ) => state.Kind = TaskKind.Live);
    }

    private PipelineSink MakeSink(TaskState state)
    {
        return new(
        Meta: info => SetTaskTitle(state, info.Title),
        Saved: path => AppendProcessLog(state.Index, $"已保存：{path}", false));
    }
}
