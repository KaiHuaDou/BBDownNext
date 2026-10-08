using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core.Download;
using BBDown.Core.Entity;
using BBDown.Core.Mux;
using BBDown.Core.Workflow;

using static BBDown.Core.Download.DownloadUtil;
using static BBDown.Core.Logger;
using static BBDown.Core.Parser;
using static BBDown.Core.Util.RetryUtil;
using static BBDown.Core.Util.Utils;

namespace BBDown.Core.Media;

public static class FlvDownload
{
    internal static async Task<PageOutcome> RunAsync(ParsedResult parsedResult, DownloadSession session, TrackSelection selection, CancellationToken ct = default)
    {
        var (myOption, ctx, pageCtx, _, _, _) = session;
        var p = pageCtx.Page;
        var reParsed = false;
        while (true)
        {
            // 循环内重取分片：交互重解析会替换 parsedResult，须随之刷新，否则仍下载首次解析的分段
            var clips = parsedResult.Clips;
            parsedResult.VideoTracks = TrackSelect.SortTracks(parsedResult.VideoTracks, ctx.Run.DfnPriority, ctx.Run.EncodingPriority, myOption.VideoAscending, ctx.Run.EncodingFirst);

            // 交互选清晰度：首次由用户选并记录 dfn 序号；下载失败重试时凭回传序号恢复（selection.VIndex）
            // 两者都走「按 dfn 重解析」，保证重试不把用户所选档位静默换成默认档
            if (await ResolveInteractiveDfnAsync(reParsed, parsedResult, session, selection, ct) is { } resolution)
            {
                parsedResult = resolution.Result;
                selection = resolution.Selection;
                reParsed = true;
                continue;
            }

            CdnHost.Apply(myOption, clips, ctx.Fetch.Cfg);

            TrackSelect.PrintFlvTracksInfo(parsedResult, clips, myOption.OnlyShowInfo);

            if (myOption.OnlyShowInfo)
            {
                return PageOutcome.Abort(selection);
            }

            // 本链路可产出（音视频 / 弹幕 / 独立封面）全无：字幕已在 PrepareAsync 产出，直接中止
            if (!myOption.Content.HasChainWork( ))
            {
                return PageOutcome.Abort(selection);
            }

            var selectedVideo = parsedResult.VideoTracks.ElementAtOrDefault(0);

            var savePath = SavePath.Build(ctx, pageCtx, selectedVideo, null);

            // 弹幕接口与流格式（DASH / FLV）无关，两条链路都须产出
            if (await PageAssets.TryDownloadDanmakuAsync(session, savePath, selection, ct) is { } danmakuAbort)
            {
                return danmakuAbort;
            }

            // 独立封面（c）与 DASH 链路同一收口：独立重试，耗尽仅跳过
            if (await PageAssets.TryDownloadCoverAsync(session, savePath, selection, ct) is { } coverAbort)
            {
                return coverAbort;
            }

            // 纯弹幕 / 纯封面等无音视频内容：附属产物已在上方写入，直接中止
            if (myOption.Content.IsAssetOnly( ))
            {
                return PageOutcome.Abort(selection);
            }

            if (IsCodecUnsupported(selectedVideo))
            {
                LogError($"分段 (FLV) 源不含 {selectedVideo!.Codecs} 编码，请改用 -e avc 重新下载");
                return PageOutcome.Abort(selection);
            }

            if (MuxFinish.TrySkipExisting(session, savePath, selection) is { } skipped)
            {
                return skipped;
            }

            // 主媒体下载窗口：只有片段下载时进度条才显示（阶段内采样经 ProgressBus 上报）
            List<string> clipPaths;
            using (var stage = ProgressBus.BeginStage("下载"))
            {
                // 片段下载是整 P 必要步骤，独立重试；耗尽则整 P 失败（不影响其他分 P）
                clipPaths = await RetryAsync(
                    async ( ) => await DownloadClipsAsync(clips, pageCtx, session.Config, ct),
                    myOption.MaxRetry, $"P{p.Index} 片段", ct, ex => PageDownload.ShouldRetry(ex, ct));
            }

            Log($"下载 P{p.Index} 完毕");
            Log("开始合并分片...");
            var videoPath = pageCtx.VideoPath;
            try
            {
                // 合并分片是整 P 必要步骤，独立重试；片段在重试全部耗尽后才清理
                await RetryAsync(
                    async ( ) => await Muxer.MergeFLV([.. clipPaths], videoPath, ctx.Run.Tools, ct),
                    myOption.MaxRetry, $"P{p.Index} 合并分片", ct, ex => PageDownload.ShouldRetry(ex, ct));
            }
            finally
            {
                // Discard 已含目标文件与 .download 临时文件的清理
                foreach (var file in clipPaths)
                {
                    Discard(file);
                }
            }

            // 非 AVC 已在上游拒绝，无 HEVC 标记；FLV 源不产出额外配音轨
            var inputs = new MuxFinish.MuxInputs(savePath, videoPath, "", [], myOption.Mux, IsHevc: false);
            // 混流是整 P 必要收尾，独立重试；耗尽则整 P 失败（不影响其他分 P）
            return await RetryAsync(
                async ( ) => await MuxFinish.RunAsync(session, inputs, selection, ct),
                myOption.MaxRetry, $"P{p.Index} 混流", ct, ex => PageDownload.ShouldRetry(ex, ct));
        }
    }

    internal static bool IsCodecUnsupported(Video? video)
    {
        return video is { Codecs: "HEVC" or "AV1" };
    }

    // 交互选清晰度：首次由用户选并记录 dfn 序号；下载失败重试时凭回传序号恢复（selection.VIndex）
    // 两者都走「按 dfn 重解析」，保证重试不把用户所选档位静默换成默认档
    // 返回 null 表示无需 / 无法重解析（未启用交互、已重解析过或清晰度列表缺失），调用方继续用现有轨道
    private static async Task<(ParsedResult Result, TrackSelection Selection)?> ResolveInteractiveDfnAsync(
        bool alreadyResolved, ParsedResult parsedResult, DownloadSession session, TrackSelection selection, CancellationToken ct)
    {
        var (myOption, ctx, pageCtx, _, _, _) = session;
        if (!myOption.InteractiveQuality || alreadyResolved)
        {
            return null;
        }

        var dfns = parsedResult.Dfns;
        if (!selection.Selected)
        {
            if (dfns.Count == 0)
            {
                LogWarn("FLV 源未返回清晰度列表，跳过交互选择");
                return null;
            }

            selection = selection with { Selected = true, VIndex = await TrackSelect.PickDfnAsync(dfns, ct) };
        }

        // 序号越界时按默认档下载，避免索引越界
        var dfn = dfns.ElementAtOrDefault(selection.VIndex);
        if (dfn == null)
        {
            LogWarn("FLV 源未返回清晰度列表，跳过交互选择");
            return null;
        }

        parsedResult.VideoTracks.Clear( );
        var resolved = await ExtractTracksAsync(ctx.Fetch.FetchedId, pageCtx.Page.Aid, pageCtx.Page.Cid, pageCtx.Page.EpId,
            myOption.Api, ctx.Run.FirstEncoding, ctx.Fetch.Cfg, dfn, ct);
        if (pageCtx.Page.Points.Count == 0)
        {
            pageCtx.Page.Points = resolved.ExtraPoints;
        }

        return (resolved, selection);
    }

    // 分片并行下载上限：片段间并行度。片段内 downloader 并行连接与片段间并行合计不超过
    // DownloaderAdapter.MaxRangeConcurrency，避免片段间 x 片段内连接数超出 CDN 对单客户端的连接限制
    private const int MaxClipParallelism = 2;

    private static async Task<List<string>> DownloadClipsAsync(List<string> clips, PageContext pageCtx, DownloadConfig downloadConfig, CancellationToken ct = default)
    {
        var p = pageCtx.Page;
        var pad = string.Empty.PadRight(clips.Count.ToString( ).Length, '0');
        var clipPaths = new string[clips.Count];
        // 片段间并行与片段内连接合计不超过 DownloaderAdapter.MaxRangeConcurrency
        // 并行度下调由 with 副本保存，不改写会话级共享实例（该实例在片段下载之后仍被引用）
        var clipConfig = downloadConfig with { ParallelCount = DownloaderAdapter.MaxRangeConcurrency / MaxClipParallelism };
        var options = new ParallelOptions { MaxDegreeOfParallelism = MaxClipParallelism, CancellationToken = ct };
        await Parallel.ForEachAsync(Enumerable.Range(0, clips.Count), options, async (i, token) =>
        {
            var clipPath = Path.Combine(pageCtx.TempDir, $"{p.Aid}.P{p.Index}.{p.Cid}.{i.ToString(pad)}.mp4");
            clipPaths[i] = clipPath;
            Log($"开始下载 P{p.Index} 视频，片段（{(i + 1).ToString(pad)} / {clips.Count}）...");
            await DownloadAsync(clips[i], clipPath, clipConfig, ct: token);
        });
        return [.. clipPaths];
    }
}
