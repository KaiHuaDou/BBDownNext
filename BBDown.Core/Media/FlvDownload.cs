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
using static BBDown.Core.Util.Utils;

namespace BBDown.Core.Media;

public static class FlvDownload
{
    internal static async Task<PageOutcome> RunAsync(ParsedResult parsedResult, DownloadSession session, TrackSelection selection, CancellationToken ct = default)
    {
        var (myOption, ctx, pageCtx, subtitleInfo, downloadConfig, sink) = session;
        var p = pageCtx.Page;
        var (selected, _, _) = selection;
        List<AudioMaterial> audioMaterial = [];
        var reParsed = false;
        while (true)
        {
            // 循环内重取分片/清晰度：交互重解析会替换 parsedResult，须随之刷新，否则仍下载首次解析的分段
            var clips = parsedResult.Clips;
            var dfns = parsedResult.Dfns;
            parsedResult.VideoTracks = TrackSelect.SortTracks(parsedResult.VideoTracks, ctx.Run.DfnPriority, ctx.Run.EncodingPriority, myOption.VideoAscending, ctx.Run.EncodingFirst);

            // 交互选清晰度：首次由用户选并记录 dfn 序号；下载失败重试时凭回传序号恢复（selection.VIndex），
            // 两者都走「按 dfn 重解析」，保证重试不把用户所选档位静默换成默认档
            if (myOption.InteractiveQuality && !reParsed)
            {
                if (!selected)
                {
                    if (dfns.Count == 0)
                    {
                        LogWarn("FLV 源未返回清晰度列表，跳过交互选择");
                    }
                    else
                    {
                        selection = selection with { Selected = true, VIndex = await TrackSelect.PickDfnAsync(dfns, ct) };
                    }
                }

                // dfns 为空或序号越界时按默认档下载，避免索引越界
                var dfn = dfns.ElementAtOrDefault(selection.VIndex);
                if (dfn == null)
                {
                    LogWarn("FLV 源未返回清晰度列表，跳过交互选择");
                }
                else
                {
                    parsedResult.VideoTracks.Clear( );
                    parsedResult = await ExtractTracksAsync(ctx.Fetch.FetchedId, p.Aid, p.Cid, p.EpId,
                        myOption.Api, ctx.Run.FirstEncoding, ctx.Fetch.Cfg, dfn, ct);
                    if (p.Points.Count == 0)
                    {
                        p.Points = parsedResult.ExtraPoints;
                    }

                    reParsed = true;
                    continue;
                }
            }

            CdnHost.Apply(myOption, clips, ctx.Fetch.Cfg);

            TrackSelect.PrintFlvTracksInfo(parsedResult, clips, myOption.OnlyShowInfo);

            if (myOption.OnlyShowInfo)
            {
                return PageOutcome.Abort(selection);
            }

            // 纯字幕等无音视频内容：FLV 源不产弹幕/封面，字幕已在 PrepareAsync 产出，直接中止
            if (!myOption.Content.HasAny(DownloadContent.Audio | DownloadContent.Video))
            {
                return PageOutcome.Abort(selection);
            }

            var selectedVideo = parsedResult.VideoTracks.ElementAtOrDefault(0);
            if (IsCodecUnsupported(selectedVideo))
            {
                LogError($"分段(FLV)源无法承载 {selectedVideo!.Codecs} 编码，请改用 -e avc 重新下载");
                return PageOutcome.Abort(selection);
            }

            var savePath = SavePath.Build(ctx, pageCtx, selectedVideo, null);
            if (MuxFinish.TrySkipExisting(session, savePath, selection) is { } skipped)
            {
                return skipped;
            }

            // 主媒体下载窗口：只有片段下载时进度条才显示（阶段内采样经 ProgressBus 上报）
            List<string> clipPaths;
            using (var stage = ProgressBus.BeginStage("下载"))
            {
                clipPaths = await DownloadClipsAsync(clips, pageCtx, downloadConfig, ct);
            }

            Log($"下载 P{p.Index} 完毕");
            Log("开始合并分片...");
            var videoPath = pageCtx.VideoPath;
            try
            {
                await Muxer.MergeFLV([.. clipPaths], videoPath, ctx.Run.Tools, ct);
            }
            finally
            {
                foreach (var file in clipPaths)
                {
                    SafeDelete(file);
                    Discard(file);
                }
            }

            // 非 AVC 已在上游拒绝，无 HEVC 标记
            var inputs = new MuxFinish.MuxInputs(savePath, videoPath, "", audioMaterial, myOption.Mux, IsHevc: false);
            return await MuxFinish.RunAsync(session, inputs, selection, ct);
        }
    }

    internal static bool IsCodecUnsupported(Video? video)
    {
        return video is { Codecs: "HEVC" or "AV1" };
    }

    // 分片并行下载上限：片段间并行度。片段内 downloader 并行连接与片段间并行合计不超过
    // DownloaderAdapter.MaxRangeConcurrency，避免 4 片段 x 32 连接打出 128 条连接
    private const int MaxClipParallelism = 4;

    private static async Task<List<string>> DownloadClipsAsync(List<string> clips, PageContext pageCtx, DownloadConfig downloadConfig, CancellationToken ct = default)
    {
        var p = pageCtx.Page;
        var pad = string.Empty.PadRight(clips.Count.ToString( ).Length, '0');
        var clipPaths = new string[clips.Count];
        // 片段间并行与片段内 downloader 连接合计不超过 DownloaderAdapter.MaxRangeConcurrency
        downloadConfig.ParallelCount = DownloaderAdapter.MaxRangeConcurrency / MaxClipParallelism;
        var options = new ParallelOptions { MaxDegreeOfParallelism = MaxClipParallelism, CancellationToken = ct };
        await Parallel.ForEachAsync(Enumerable.Range(0, clips.Count), options, async (i, token) =>
        {
            var clipPath = Path.Combine(pageCtx.TempDir, $"{p.Aid}.P{p.Index}.{p.Cid}.{i.ToString(pad)}.mp4");
            clipPaths[i] = clipPath;
            Log($"开始下载 P{p.Index} 视频，片段（{(i + 1).ToString(pad)} / {clips.Count}）...");
            await DownloadAsync(clips[i], clipPath, downloadConfig, ct: token);
        });
        return [.. clipPaths];
    }
}
