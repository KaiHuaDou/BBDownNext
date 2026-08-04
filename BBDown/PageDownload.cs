using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core;
using BBDown.Core.Entity;

using static BBDown.DownloadUtil;
using static BBDown.Core.Entity.Entity;
using static BBDown.Core.Logger;
using static BBDown.Core.Parser;
using static BBDown.Core.Util.FileNameUtil;
using static BBDown.Utils;

namespace BBDown;

internal static class PageDownload
{
    // Aborted 为 true 表示该分 P 应立即结束（不再登记 SavePath）；Selected 需回传以跨重试保留用户已手动选轨的状态
    internal readonly record struct PageOutcome(bool Aborted, string SavePath, bool Selected)
    {
        public static PageOutcome Abort(bool selected)
        {
            return new(true, "", selected);
        }

        public static PageOutcome Done(string savePath, bool selected)
        {
            return new(false, savePath, selected);
        }
    }

    private static async Task<PageOutcome> DownloadPageAsync(Page p, DownloadOptions myOption, WorkContext ctx, List<Page> selectedPagesInfo, DownloadTask? relatedTask = null, CancellationToken ct = default)
    {
        var pageCtx = BuildPageContext(p, ctx, selectedPagesInfo);
        List<Subtitle> subtitleInfo = [];
        var selected = false; //用户是否已经手动选择过了轨道
        var retryCount = 0;
        var outcome = PageOutcome.Abort(selected);
        while (true)
        {
            try
            {
                LogDebug("获取章节信息...");
                p.points = await ChapterMeta.FetchPointsAsync(p.cid, p.aid, ctx.Cfg);

                if (!myOption.OnlyShowInfo)
                {
                    subtitleInfo = await PageAssets.PrepareAsync(myOption, ctx, pageCtx, ct);
                    if (myOption.SubOnly)
                    {
                        MuxFinish.TryDeleteEmptyDir(pageCtx.TempDir);
                        return PageOutcome.Abort(selected);
                    }
                }

                //调用解析
                var parsedResult = await ExtractTracksAsync(ctx.FetchedAid, p.aid, p.cid, p.epid,
                    myOption.UseTvApi, myOption.UseIntlApi, myOption.UseAppApi, ctx.FirstEncoding, ctx.Cfg, ct: ct);
                if (p.points.Count == 0)
                {
                    p.points = parsedResult.ExtraPoints;
                }

                if (Config.DebugLog)
                {
                    File.WriteAllText(Path.Combine(ctx.WorkDir, $"debug_{DateTime.Now:yyyyMMddHHmmssfff}.json"), parsedResult.RawResponse);
                }

                var downloadConfig = BuildDownloadConfig(myOption, ctx.Cfg, relatedTask);
                outcome = await DispatchAsync(parsedResult, myOption, ctx, pageCtx, subtitleInfo, downloadConfig, relatedTask, selected, ct);

                selected = outcome.Selected;
                if (outcome.Aborted)
                {
                    return outcome;
                }

                if (!string.IsNullOrWhiteSpace(outcome.SavePath))
                {
                    relatedTask?.SavePaths.Add(outcome.SavePath);
                }
            }
            catch (Exception ex) when (ShouldRetry(ex))
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

    // Ctrl+C 触发的取消不能被当成"下载异常"退避重试，否则用户按下之后还要再等两轮退避（P1-20）
    internal static bool IsCancellation(Exception ex)
    {
        return ex is OperationCanceledException
               || (ex is AggregateException agg && agg.InnerExceptions.Any(e => e is OperationCanceledException));
    }

    internal static bool ShouldRetry(Exception ex)
    {
        return !IsRangeUnsupported(ex) && !IsCancellation(ex);
    }

    private static PageContext BuildPageContext(Page p, WorkContext ctx, List<Page> selectedPagesInfo)
    {
        var vInfo = ctx.VInfo!;
        var selectedPagesCount = selectedPagesInfo.Count;
        var tempDir = Path.Combine(ctx.WorkDir, p.aid);
        return new PageContext(
            Page: p,
            // 原始标题，落盘前统一交给 GetValidFileName 清洗；这里保持原样是因为它还要写进容器元数据
            Title: vInfo.Title,
            Desc: string.IsNullOrEmpty(p.desc) ? vInfo.Desc : p.desc,
            EpisodeTitle: BuildEpisodeTitle(p, selectedPagesCount, vInfo.IsBangumi, vInfo.IsBangumiEnd),
            TempDir: tempDir,
            VideoPath: Path.Combine(tempDir, $"{p.aid}.P{p.index}.{p.cid}.mp4"),
            AudioPath: Path.Combine(tempDir, $"{p.aid}.P{p.index}.{p.cid}.m4a"),
            CoverPath: Path.Combine(tempDir, $"{p.aid}.jpg"),
            CoverUrl: vInfo.Pic is { Length: 0 } ? p.cover! : vInfo.Pic,
            PubTime: vInfo.PubTime,
            PagesCount: selectedPagesCount,
            DeleteCoverAfterMux: ShouldDeleteCover(p, selectedPagesInfo));
    }

    internal static string BuildEpisodeTitle(Page p, int pagesCount, bool isBangumi, bool isBangumiEnd)
    {
        return pagesCount > 1 || (isBangumi && !isBangumiEnd) ? p.title : "";
    }

    internal static bool ShouldDeleteCover(Page p, List<Page> selectedPagesInfo)
    {
        return selectedPagesInfo.Count == 1
            || p.index == selectedPagesInfo[^1].index
            || p.aid != selectedPagesInfo[^1].aid;
    }

    internal static DownloadConfig BuildDownloadConfig(DownloadOptions myOption, AppConfig cfg, DownloadTask? relatedTask)
    {
        return new DownloadConfig
        {
            UseAria2c = myOption.UseAria2c,
            Aria2cArgs = myOption.Aria2cArgs,
            NoForceHttp = myOption.NoForceHttp,
            SingleThread = myOption.SingleThread,
            RelatedTask = relatedTask,
            Cookie = cfg.Cookie,
        };
    }

    private static async Task<PageOutcome> DownloadTracksAsync(ParsedResult parsedResult, DownloadOptions myOption, WorkContext ctx, PageContext pageCtx,
        List<Subtitle> subtitleInfo, DownloadConfig downloadConfig, DownloadTask? relatedTask, bool selected, CancellationToken ct = default)
    {
        if ((parsedResult.VideoTracks.Count != 0 || parsedResult.AudioTracks.Count != 0) && parsedResult.Clips.Count == 0)
        {
            return await DashDownload.RunAsync(parsedResult, myOption, ctx, pageCtx, subtitleInfo, downloadConfig, relatedTask, selected, ct);
        }

        if (parsedResult.Clips.Count != 0 && parsedResult.Dfns.Count != 0)
        {
            return await FlvDownload.RunAsync(parsedResult, myOption, ctx, pageCtx, subtitleInfo, downloadConfig, relatedTask, selected, ct);
        }

        LogError("解析此分 P 失败（使用 --debug 以查看详细信息）");
        if (parsedResult.RawResponse.Length < 100)
        {
            LogError(parsedResult.RawResponse);
        }

        LogDebug("{0}", parsedResult.RawResponse);
        return PageOutcome.Done("", selected);
    }

    internal static Task<PageOutcome> RunAsync(Page p, DownloadOptions myOption, WorkContext ctx, List<Page> selectedPagesInfo, DownloadTask? relatedTask = null, CancellationToken ct = default)
        => DownloadPageAsync(p, myOption, ctx, selectedPagesInfo, relatedTask, ct);

    internal static Task<PageOutcome> DispatchAsync(ParsedResult parsedResult, DownloadOptions myOption, WorkContext ctx, PageContext pageCtx,
        List<Subtitle> subtitleInfo, DownloadConfig downloadConfig, DownloadTask? relatedTask, bool selected, CancellationToken ct = default)
        => DownloadTracksAsync(parsedResult, myOption, ctx, pageCtx, subtitleInfo, downloadConfig, relatedTask, selected, ct);
}
