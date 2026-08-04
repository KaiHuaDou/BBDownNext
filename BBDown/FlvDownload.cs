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
using PageOutcome = BBDown.PageDownload.PageOutcome;

namespace BBDown;

internal static class FlvDownload
{
    private static async Task<PageOutcome> DownloadFlvAsync(ParsedResult parsedResult, DownloadOptions myOption, WorkContext ctx, PageContext pageCtx,
        List<Subtitle> subtitleInfo, DownloadConfig downloadConfig, DownloadTask? relatedTask, bool selected, CancellationToken ct = default)
    {
        var p = pageCtx.Page;
        List<AudioMaterial> audioMaterial = [];
        var reParsed = false;
        // clips/dfns 取自首次解析结果，重解析后仍沿用旧引用（与原实现一致）
        var clips = parsedResult.Clips;
        var dfns = parsedResult.Dfns;
        while (true)
        {
            parsedResult.VideoTracks = TrackSelect.SortTracks(parsedResult.VideoTracks, ctx.DfnPriority, ctx.EncodingPriority, myOption.VideoAscending, ctx.EncodingFirst);

            var vIndex = 0;
            if (myOption.Interactive && !reParsed && !selected)
            {
                vIndex = TrackSelect.PickDfn(dfns);
                // 重新解析
                parsedResult.VideoTracks.Clear();
                parsedResult = await ExtractTracksAsync(ctx.FetchedAid, p.aid, p.cid, p.epid,
                    myOption.UseTvApi, myOption.UseIntlApi, myOption.UseAppApi, ctx.FirstEncoding, ctx.Cfg, dfns[vIndex], ct);
                if (p.points.Count == 0)
                {
                    p.points = parsedResult.ExtraPoints;
                }

                reParsed = true;
                selected = true;
                continue;
            }

            CdnHost.Apply(myOption, clips, ctx.Cfg);

            Log($"共计 {parsedResult.VideoTracks.Count} 条流（共有 {clips.Count} 个分段）。");
            var index = 0;
            foreach (var v in parsedResult.VideoTracks)
            {
                LogColor($"{index++}. [{v.dfn}] [{v.res}] [{v.codecs}] [{v.fps}] [~{v.size / 1024 / v.dur * 8:00} kbps] [{FormatFileSize(v.size)}]".Replace("[] ", ""), false);
                if (myOption.OnlyShowInfo)
                {
                    clips.ForEach(Console.WriteLine);
                }
            }

            if (myOption.OnlyShowInfo)
            {
                return PageOutcome.Abort(selected);
            }

            var savePath = SavePath.Build(ctx, pageCtx, parsedResult.VideoTracks.ElementAtOrDefault(vIndex), null);
            if (File.Exists(savePath) && new FileInfo(savePath).Length != 0)
            {
                Log($"{savePath} 已存在，跳过下载...");
                relatedTask?.SavePaths.Add(savePath);
                if (pageCtx.PagesCount == 1 && Directory.Exists(pageCtx.TempDir))
                {
                    Directory.Delete(pageCtx.TempDir, true);
                }

                return PageOutcome.Abort(selected);
            }

            var clipPaths = await FetchClipsAsync(clips, pageCtx, downloadConfig, ct);

            Log($"下载 P{p.index} 完毕");
            Log("开始合并分片...");
            var videoPath = pageCtx.VideoPath;
            try
            {
                await Muxer.MergeFLV([.. clipPaths], videoPath, ct);
            }
            finally
            {
                foreach (var file in clipPaths)
                {
                    SafeDelete(file);
                    PartFile.Discard(file);
                }
            }

            if (myOption.SkipMux)
            {
                return PageOutcome.Abort(selected);
            }

            Log($"开始混流视频{(subtitleInfo.Count != 0 ? "和字幕" : "")}...");
            if (myOption.AudioOnly)
            {
                savePath = MuxFinish.ToAudioOnlyPath(savePath);
            }

            var code = await Muxer.MuxAV(false, p.bvid, videoPath, "", audioMaterial, savePath,
                pageCtx.Desc,
                pageCtx.Title,
                p.ownerName ?? "",
                pageCtx.EpisodeTitle,
                File.Exists(pageCtx.CoverPath) ? pageCtx.CoverPath : "",
                ctx.Lang,
                subtitleInfo, myOption.AudioOnly, myOption.VideoOnly, p.points, p.pubTime, myOption.NoMetadata, ct: ct);
            if (code != 0 || !File.Exists(savePath) || new FileInfo(savePath).Length == 0)
            {
                LogError("混流失败");
                return PageOutcome.Abort(selected);
            }

            MuxFinish.Cleanup(pageCtx, parsedResult.VideoTracks.Count != 0 ? videoPath : "", "", subtitleInfo, audioMaterial);
            return PageOutcome.Done(savePath, selected);
        }
    }

    private static async Task<List<string>> DownloadFlvClipsAsync(List<string> clips, PageContext pageCtx, DownloadConfig downloadConfig, CancellationToken ct = default)
    {
        var p = pageCtx.Page;
        var pad = string.Empty.PadRight(clips.Count.ToString().Length, '0');
        var clipPaths = new List<string>(clips.Count);
        for (var i = 0; i < clips.Count; i++)
        {
            var clipPath = Path.Combine(pageCtx.TempDir, $"{p.aid}.P{p.index}.{p.cid}.{i.ToString(pad)}.mp4");
            clipPaths.Add(clipPath);
            Log($"开始下载 P{p.index} 视频，片段（{(i + 1).ToString(pad)} / {clips.Count}）...");
            await DownloadAsync(clips[i], clipPath, downloadConfig, ct: ct);
        }

        return clipPaths;
    }

    internal static Task<PageOutcome> RunAsync(ParsedResult parsedResult, DownloadOptions myOption, WorkContext ctx, PageContext pageCtx,
        List<Subtitle> subtitleInfo, DownloadConfig downloadConfig, DownloadTask? relatedTask, bool selected, CancellationToken ct = default)
        => DownloadFlvAsync(parsedResult, myOption, ctx, pageCtx, subtitleInfo, downloadConfig, relatedTask, selected, ct);

    internal static Task<List<string>> FetchClipsAsync(List<string> clips, PageContext pageCtx, DownloadConfig downloadConfig, CancellationToken ct = default)
        => DownloadFlvClipsAsync(clips, pageCtx, downloadConfig, ct);
}
