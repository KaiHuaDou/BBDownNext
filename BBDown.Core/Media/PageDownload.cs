using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core;
using BBDown.Core.Entity;
using BBDown.Core.Mux;
using BBDown.Core.Download;

using static BBDown.Core.Logger;
using static BBDown.Core.Parser;
using static BBDown.Core.Download.DownloadUtil;
using static BBDown.Core.Util.Utils;

namespace BBDown.Core.Media;

public static class PageDownload
{
    internal static async Task<PageOutcome> RunAsync(Page p, DownloadRequest myOption, WorkContext ctx, List<Page> selectedPagesInfo, PipelineSink sink = default, CancellationToken ct = default)
    {
        var pageCtx = BuildPageContext(p, ctx, selectedPagesInfo);
        List<Subtitle> subtitleInfo = [];
        // 交互选轨状态跨重试保留：选过的序号经 PageOutcome 回传，重试恢复，避免静默降级到第 0 条轨道
        var selection = TrackSelection.Default;
        var retryCount = 0;
        var outcome = PageOutcome.Abort(TrackSelection.Default);
        while (true)
        {
            try
            {
                LogDebug("获取播放器信息...");
                var playerInfo = await ChapterMeta.FetchPlayerV2Async(p.Cid, p.Aid, ctx.Fetch.Cfg, ct);
                p.Points = playerInfo.Points;

                //调用解析
                var parsedResult = await ExtractTracksAsync(ctx.Fetch.FetchedId, p.Aid, p.Cid, p.EpId,
                    myOption.Api, ctx.Run.FirstEncoding, ctx.Fetch.Cfg, ct: ct);
                if (p.Points.Count == 0)
                {
                    p.Points = parsedResult.ExtraPoints;
                }

                if (Config.DebugLog)
                {
                    File.WriteAllText(Path.Combine(ctx.Run.WorkDir, $"debug_{DateTime.Now:yyyyMMddHHmmssfff}.json"), parsedResult.RawResponse);
                }

                if (IsTruncatedPreview(playerInfo.UpowerExclusive, p.Dur, parsedResult.Duration))
                {
                    LogWarn(string.IsNullOrEmpty(playerInfo.UpowerTitle) ? "充电专属视频" : playerInfo.UpowerTitle);
                    LogWarn($"当前账号未充电该 UP 主，只能获取 {FormatTime(parsedResult.Duration, true)} 的试看片段（完整视频 {FormatTime(p.Dur, true)}）", false);
                    // 这三个开关都不产出视频文件，中止反而挡掉用户诊断问题的手段
                    if (myOption.OnlyShowInfo || !myOption.Content.HasAny(DownloadContent.Audio | DownloadContent.Video))
                    {
                        LogWarn("当前仅输出信息/封面/弹幕，不受影响", false);
                    }
                    else if (myOption.AllowPreview)
                    {
                        pageCtx = pageCtx with { IsPreview = true };
                    }
                    else
                    {
                        throw new ChargedPreviewException($"P{p.Index}（{p.Aid}）为充电视频试看片段，已跳过。");
                    }
                }

                // 先以空字幕占位建好 session（此时 pageCtx 已含最终 IsPreview 标记），再交给 PrepareAsync 填充字幕
                var session = new DownloadSession(myOption, ctx, pageCtx, [], BuildDownloadConfig(myOption, ctx.Fetch.Cfg, ctx.Run.Tools, sink), sink);
                if (!myOption.OnlyShowInfo)
                {
                    subtitleInfo = await PageAssets.PrepareAsync(session, ct);
                }

                session = session with { Subtitles = subtitleInfo };
                outcome = await DispatchAsync(parsedResult, session, selection, ct);
                if (pageCtx.IsPreview)
                {
                    outcome = outcome with { Preview = true };
                }

                selection = new TrackSelection(outcome.Selected, outcome.VIndex, outcome.AIndex);
                if (outcome.Aborted)
                {
                    return outcome;
                }

                if (!string.IsNullOrWhiteSpace(outcome.SavePath))
                {
                    sink.Saved?.Invoke(outcome.SavePath);
                }
            }
            catch (Exception ex) when (ShouldRetry(ex, ct))
            {
                if (++retryCount > 2)
                {
                    throw;
                }

                LogError(ex.Message);
                var backoff = TimeSpan.FromSeconds(1 << retryCount);
                LogWarn($"下载失败，{backoff.TotalSeconds:0} 秒后重试...");
                await Task.Delay(backoff, ct);
                continue;
            }

            break;
        }

        return outcome;
    }

    // 服务器不支持 Range 时重试多少次都是同样结果，直接放行让用户看到换单线程的提示
    // Parallel.ForEachAsync 会把分片异常裹进 AggregateException
    internal static bool IsRangeUnsupported(Exception ex)
    {
        return ex is NotSupportedException
               || (ex is AggregateException agg && agg.InnerExceptions.Any(e => e is NotSupportedException));
    }

    // 只有用户真的取消了（ct 已请求取消）才判定为取消；HttpClient 超时等瞬态故障被包装成
    // OperationCanceledException 但用户令牌并未取消，必须当作可重试的失败（§2.2）
    internal static bool IsCancellation(Exception ex, CancellationToken ct)
    {
        if (!ct.IsCancellationRequested)
        {
            return false;
        }

        return ex is OperationCanceledException
               || (ex is AggregateException agg && agg.InnerExceptions.Any(e => e is OperationCanceledException));
    }

    // 充电权限不会因为重试而改变，重试只会让用户白等两轮退避
    internal static bool ShouldRetry(Exception ex, CancellationToken ct)
    {
        return !IsRangeUnsupported(ex) && !IsCancellation(ex, ct) && ex is not ChargedPreviewException;
    }

    // 双条件：稿件确为充电专属（is_upower_exclusive 是稿件属性，与账号无关），且 playurl 下发时长明显短于 view 声称的完整时长。
    // 30 秒下限用于避开 timelength(ms) 与 duration(整秒) 的固有封装误差；真实试看片段与完整稿件差距动辄数十分钟。
    internal static bool IsTruncatedPreview(bool upowerExclusive, int fullDuration, int actualDuration)
    {
        return upowerExclusive && fullDuration > 0 && actualDuration > 0
               && actualDuration < fullDuration * 0.9 && fullDuration - actualDuration >= 30;
    }

    internal static PageContext BuildPageContext(Page p, WorkContext ctx, List<Page> selectedPagesInfo)
    {
        var vInfo = ctx.Fetch.VInfo!;
        var selectedPagesCount = selectedPagesInfo.Count;
        var tempDir = Path.Combine(ctx.Run.WorkDir, p.Aid);
        return new PageContext(
            Page: p,
            // 原始标题，落盘前统一交给 GetValidFileName 清洗；这里保持原样是因为它还要写进容器元数据
            Title: vInfo.Title,
            Desc: string.IsNullOrEmpty(p.Desc) ? vInfo.Desc : p.Desc,
            EpisodeTitle: BuildEpisodeTitle(p, selectedPagesCount, vInfo.IsBangumi, vInfo.IsBangumiEnd),
            TempDir: tempDir,
            VideoPath: Path.Combine(tempDir, $"{p.Aid}.P{p.Index}.{p.Cid}.mp4"),
            AudioPath: Path.Combine(tempDir, $"{p.Aid}.P{p.Index}.{p.Cid}.m4a"),
            CoverPath: Path.Combine(tempDir, $"{p.Aid}.jpg"),
            CoverUrl: vInfo.Pic is { Length: 0 } ? p.Cover! : vInfo.Pic,
            PubTime: vInfo.PubTime,
            PagesCount: selectedPagesCount,
            DeleteCoverAfterMux: ShouldDeleteCover(p, selectedPagesInfo));
    }

    internal static string BuildEpisodeTitle(Page p, int pagesCount, bool isBangumi, bool isBangumiEnd)
    {
        return pagesCount > 1 || (isBangumi && !isBangumiEnd) ? p.Title : "";
    }

    internal static bool ShouldDeleteCover(Page p, List<Page> selectedPagesInfo)
    {
        return selectedPagesInfo.Count == 1
            || p.Index == selectedPagesInfo[^1].Index
            || p.Aid != selectedPagesInfo[^1].Aid;
    }

    internal static DownloadConfig BuildDownloadConfig(DownloadRequest myOption, AppConfig cfg, ToolPaths tools, PipelineSink sink = default)
    {
        return new DownloadConfig
        {
            UseAria2c = myOption.UseAria2c,
            Aria2cArgs = myOption.Aria2cArgs,
            NoForceHttp = myOption.NoForceHttp,
            SingleThread = myOption.SingleThread,
            Aria2cPath = tools.Aria2c,
            OnSample = sink.Sample,
            Cookie = cfg.Cookie,
        };
    }

    private static async Task<PageOutcome> DispatchAsync(ParsedResult parsedResult, DownloadSession session, TrackSelection selection, CancellationToken ct = default)
    {
        if ((parsedResult.VideoTracks.Count != 0 || parsedResult.AudioTracks.Count != 0) && parsedResult.Clips.Count == 0)
        {
            return await DashDownload.RunAsync(parsedResult, session, selection, ct);
        }

        if (parsedResult.Clips.Count != 0 && parsedResult.Dfns.Count != 0)
        {
            return await FlvDownload.RunAsync(parsedResult, session, selection, ct);
        }

        LogError("解析此分 P 失败（使用 --debug 以查看详细信息）");
        if (parsedResult.RawResponse.Length < 100)
        {
            LogError(parsedResult.RawResponse);
        }

        LogDebug("{0}", parsedResult.RawResponse);
        return PageOutcome.Done("", selection);
    }
}
