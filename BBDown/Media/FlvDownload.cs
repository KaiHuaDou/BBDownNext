using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core.Entity;
using BBDown.Download;
using BBDown.Mux;
using BBDown.Util;

using static BBDown.Core.Entity.Entity;
using static BBDown.Core.Logger;
using static BBDown.Core.Parser;
using static BBDown.Download.DownloadUtil;
using static BBDown.Util.Utils;

namespace BBDown.Media;

internal static class FlvDownload
{
    internal static async Task<PageOutcome> RunAsync(ParsedResult parsedResult, DownloadSession session, bool selected, CancellationToken ct = default)
    {
        var (myOption, ctx, pageCtx, subtitleInfo, downloadConfig, _) = session;
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
                parsedResult.VideoTracks.Clear( );
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

            TrackSelect.PrintFlvTracksInfo(parsedResult, clips, myOption.OnlyShowInfo);

            if (myOption.OnlyShowInfo)
            {
                return PageOutcome.Abort(selected);
            }

            var selectedVideo = parsedResult.VideoTracks.ElementAtOrDefault(vIndex);
            if (IsCodecUnsupported(selectedVideo))
            {
                LogError($"分段(FLV)源无法承载 {selectedVideo!.codecs} 编码，请改用 -e avc 重新下载");
                return PageOutcome.Abort(selected);
            }

            var savePath = SavePath.Build(ctx, pageCtx, selectedVideo, null);
            if (MuxFinish.TrySkipExisting(session, savePath, selected) is { } skipped)
            {
                return skipped;
            }

            var clipPaths = await DownloadClipsAsync(clips, pageCtx, downloadConfig, ct);

            Log($"下载 P{p.index} 完毕");
            Log("开始合并分片...");
            var videoPath = pageCtx.VideoPath;
            try
            {
                await Muxer.MergeFLV([.. clipPaths], videoPath, ctx.Tools, ct);
            }
            finally
            {
                foreach (var file in clipPaths)
                {
                    SafeDelete(file);
                    PartFile.Discard(file);
                }
            }

            // 非 AVC 已在上游拒绝，混流标记恒为 false
            var inputs = new MuxFinish.MuxInputs(savePath, videoPath, "", audioMaterial, UseMp4box: false, IsHevc: false);
            return await MuxFinish.RunAsync(session, inputs, selected, ct);
        }
    }

    internal static bool IsCodecUnsupported(Video? video)
    {
        return video is { codecs: "HEVC" or "AV1" };
    }

    private static async Task<List<string>> DownloadClipsAsync(List<string> clips, PageContext pageCtx, DownloadConfig downloadConfig, CancellationToken ct = default)
    {
        var p = pageCtx.Page;
        var pad = string.Empty.PadRight(clips.Count.ToString( ).Length, '0');
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
}
