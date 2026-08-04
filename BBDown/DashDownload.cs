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

internal static class DashDownload
{
    private static async Task<PageOutcome> DownloadDashAsync(ParsedResult parsedResult, DownloadOptions myOption, WorkContext ctx, PageContext pageCtx,
        List<Subtitle> subtitleInfo, DownloadConfig downloadConfig, DownloadTask? relatedTask, bool selected, CancellationToken ct = default)
    {
        var p = pageCtx.Page;

        if (parsedResult.VideoTracks.Count == 0)
        {
            LogWarn("没有符合要求的视频流");
            if (myOption.VideoOnly)
            {
                return PageOutcome.Abort(selected);
            }
        }

        if (parsedResult.AudioTracks.Count == 0)
        {
            LogWarn("没有符合要求的音频流");
            if (myOption.AudioOnly)
            {
                return PageOutcome.Abort(selected);
            }
        }

        if (myOption.AudioOnly)
        {
            parsedResult.VideoTracks.Clear();
        }

        if (myOption.VideoOnly)
        {
            parsedResult.AudioTracks.Clear();
            parsedResult.BackgroundAudioTracks.Clear();
            parsedResult.RoleAudioList.Clear();
        }

        TrackSelect.SortDashTracks(parsedResult, ctx, myOption);

        if (!myOption.HideStreams)
        {
            TrackSelect.PrintAllTracksInfo(parsedResult, p.dur, myOption.OnlyShowInfo);
        }

        // 仅展示 跳过下载
        if (myOption.OnlyShowInfo)
        {
            return PageOutcome.Abort(selected);
        }

        var vIndex = 0; // 用户手动选择的视频序号
        var aIndex = 0; // 用户手动选择的音频序号
        if (myOption.Interactive && !selected)
        {
            TrackSelect.PickTracks(parsedResult, ref vIndex, ref aIndex);
            selected = true;
        }

        var selectedVideo = parsedResult.VideoTracks.ElementAtOrDefault(vIndex);
        var selectedAudio = parsedResult.AudioTracks.ElementAtOrDefault(aIndex);
        var selectedBackgroundAudio = parsedResult.BackgroundAudioTracks.ElementAtOrDefault(aIndex);

        LogDebug("Format Before: " + ctx.SavePathFormat);
        var savePath = SavePath.Build(ctx, pageCtx, selectedVideo, selectedAudio);
        LogDebug("Format After: " + savePath);

        if (ctx.DownloadDanmaku && await PageAssets.DownloadDanmakuAsync(myOption, ctx, pageCtx, savePath, downloadConfig, ct))
        {
            return PageOutcome.Abort(selected);
        }

        if (myOption.CoverOnly)
        {
            var newCoverPath = Path.ChangeExtension(savePath, Path.GetExtension(pageCtx.CoverUrl));
            await DownloadFileAsync(pageCtx.CoverUrl, newCoverPath, downloadConfig, ct);
            MuxFinish.TryDeleteEmptyDir(pageCtx.TempDir);
            relatedTask?.SavePaths.Add(newCoverPath);
        }

        Log("已选择的流：");
        TrackSelect.PrintSelectedTrackInfo(selectedVideo, selectedAudio, p.dur);

        CdnHost.Apply(myOption, selectedVideo, selectedAudio, ctx.Cfg);

        if (File.Exists(savePath) && new FileInfo(savePath).Length != 0)
        {
            Log($"{savePath} 已存在，跳过下载...");
            relatedTask?.SavePaths.Add(savePath);
            File.Delete(pageCtx.CoverPath);
            MuxFinish.TryDeleteEmptyDir(pageCtx.TempDir);
            return PageOutcome.Abort(selected);
        }

        var videoPath = pageCtx.VideoPath;
        var audioPath = pageCtx.AudioPath;
        List<AudioMaterial> audioMaterial = [];
        var useMp4box = myOption.UseMP4box;
        if (selectedVideo != null)
        {
            // 杜比视界 (id=126), 若 FFmpeg 版本小于 5.0, 使用 mp4box 封装
            if (selectedVideo.id == Config.DolbyVisionQn && !useMp4box && !ChapterMeta.CheckFFmpegDOVI())
            {
                LogWarn("您的 FFmpeg 版本小于 5.0，杜比视界将使用 MP4Box 混流...");
                useMp4box = true;
            }

            Log($"开始下载 P{p.index} 视频...");
            await DownloadAsync(selectedVideo.baseUrl, videoPath, downloadConfig, ct: ct);
        }

        if (selectedAudio != null)
        {
            Log($"开始下载 P{p.index} 音频...");
            await DownloadAsync(selectedAudio.baseUrl, audioPath, downloadConfig, ct: ct);
        }

        if (selectedBackgroundAudio != null)
        {
            var backgroundPath = Path.Combine(pageCtx.TempDir, $"{p.aid}.{p.cid}.P{p.index}.back_ground.m4a");
            Log($"开始下载 P{p.index} 背景配音...");
            await DownloadAsync(selectedBackgroundAudio.baseUrl, backgroundPath, downloadConfig, ct: ct);
            audioMaterial.Add(new AudioMaterial { title = "背景音频", personName = "", path = backgroundPath });
        }

        foreach (var role in parsedResult.RoleAudioList)
        {
            role.path = Path.Combine(pageCtx.TempDir, Path.GetFileName(role.path));
            Log($"开始下载 P{p.index} 配音 [{role.title}]...");
            await DownloadAsync(role.audio[aIndex].baseUrl, role.path, downloadConfig, ct: ct);
            audioMaterial.Add(new AudioMaterial { title = role.title, personName = role.personName, path = role.path });
        }

        Log($"P{p.index} 下载完成");
        if (parsedResult.VideoTracks.Count == 0)
        {
            videoPath = "";
        }

        if (parsedResult.AudioTracks.Count == 0)
        {
            audioPath = "";
        }

        if (myOption.SkipMux)
        {
            return PageOutcome.Abort(selected);
        }

        Log($"开始混流音视频{(subtitleInfo.Count != 0 ? "和字幕" : "")}...");
        if (myOption.AudioOnly)
        {
            savePath = MuxFinish.ToAudioOnlyPath(savePath);
        }

        var isHevc = selectedVideo?.codecs == "HEVC";
        var code = await Muxer.MuxAV(useMp4box, p.bvid, videoPath, audioPath, audioMaterial, savePath,
            pageCtx.Desc,
            pageCtx.Title,
            p.ownerName ?? "",
            pageCtx.EpisodeTitle,
            File.Exists(pageCtx.CoverPath) ? pageCtx.CoverPath : "",
            ctx.Lang,
            subtitleInfo, myOption.AudioOnly, myOption.VideoOnly, p.points, p.pubTime, myOption.NoMetadata, isHevc, ct);
        if (code != 0 || !File.Exists(savePath) || new FileInfo(savePath).Length == 0)
        {
            LogError("混流失败");
            return PageOutcome.Abort(selected);
        }

        MuxFinish.Cleanup(pageCtx, videoPath, audioPath, subtitleInfo, audioMaterial);
        return PageOutcome.Done(savePath, selected);
    }

    internal static Task<PageOutcome> RunAsync(ParsedResult parsedResult, DownloadOptions myOption, WorkContext ctx, PageContext pageCtx,
        List<Subtitle> subtitleInfo, DownloadConfig downloadConfig, DownloadTask? relatedTask, bool selected, CancellationToken ct = default)
        => DownloadDashAsync(parsedResult, myOption, ctx, pageCtx, subtitleInfo, downloadConfig, relatedTask, selected, ct);
}
