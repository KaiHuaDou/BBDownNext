using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core.Download;
using BBDown.Core.Entity;
using BBDown.Core.Mux;

using static BBDown.Core.Logger;
using static BBDown.Core.Parser;
using static BBDown.Core.Util.RetryUtil;
using static BBDown.Core.Util.Utils;

namespace BBDown.Core.Media;

public static class PageDownload
{
    internal static async Task<PageOutcome> RunAsync(Page p, DownloadRequest myOption, WorkContext ctx, List<Page> selectedPagesInfo, PipelineSink sink = default, CancellationToken ct = default)
    {
        var pageCtx = BuildPageContext(p, ctx, selectedPagesInfo);
        List<Subtitle> subtitleInfo = [];
        var selection = TrackSelection.Default;
        var outcome = PageOutcome.Abort(TrackSelection.Default);

        // 拉播放信息 + 解析是整 P 的必要前置，单独包一层重试（耗尽则整 P 失败）；试看判定在重试外，充电权限不会因重试改变
        var (playerInfo, parsedResult) = await RetryAsync(async ( ) =>
        {
            var playerTask = ChapterMeta.FetchPlayerV2Async(p.Cid, p.Aid, ctx.Fetch.Cfg, ct);
            var parsedTask = ExtractTracksAsync(ctx.Fetch.FetchedId, p.Aid, p.Cid, p.EpId,
                myOption.Api, ctx.Run.FirstEncoding, ctx.Fetch.Cfg, ct: ct);
            var playerInfo = await playerTask;
            var parsedResult = await parsedTask;
            return (playerInfo, parsedResult);
        }, myOption.MaxRetry, $"P{p.Index} 解析", ct, ex => ShouldRetry(ex, ct));

        p.Points = playerInfo.Points;
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
        var session = new DownloadSession(myOption, ctx, pageCtx, [], BuildDownloadConfig(myOption, ctx.Fetch.Cfg, ctx.Run.Tools), sink);
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

        return outcome;
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
        return !IsCancellation(ex, ct) && ex is not ChargedPreviewException;
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
            CoverUrl: vInfo.Pic is { Length: 0 } ? p.Cover ?? "" : vInfo.Pic,
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

    internal static DownloadConfig BuildDownloadConfig(DownloadRequest myOption, AppConfig cfg, ToolPaths tools)
    {
        return new DownloadConfig
        {
            UseAria2c = myOption.UseAria2c,
            Aria2cArgs = myOption.Aria2cArgs,
            NoForceHttp = myOption.NoForceHttp,
            SingleThread = myOption.SingleThread,
            Aria2cPath = tools.Aria2c,
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

        // 两个分支都不命中：响应正常但未解析出任何轨道（风控降级 / 接口变更等）。
        // 此前静默返回 Done 会让整个任务以「成功」退出（退出码 0），脚本无法感知失败；改为抛异常，
        // 由外层重试（瞬态限流可自愈）并在重试耗尽后上报为分 P 失败
        LogError("解析此分 P 失败（使用 --debug 以查看详细信息）");
        if (parsedResult.RawResponse.Length < 100)
        {
            LogError(parsedResult.RawResponse);
        }

        LogDebug("{0}", parsedResult.RawResponse);
        throw new InvalidOperationException($"P{session.PageCtx.Page.Index} 解析此分 P 失败（未解析到任何音视频轨道）");
    }
}
