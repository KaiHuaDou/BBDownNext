using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core;
using BBDown.Core.Entity;
using BBDown.Download;
using BBDown.Mux;
using BBDown.Util;

using static BBDown.Core.Entity.Entity;
using static BBDown.Core.Logger;
using static BBDown.Download.DownloadUtil;

namespace BBDown.Media;

internal static class DashDownload
{
    internal static async Task<PageOutcome> RunAsync(ParsedResult parsedResult, DownloadSession session, bool selected, CancellationToken ct = default)
    {
        var (myOption, ctx, pageCtx, subtitleInfo, downloadConfig, sink) = session;
        var p = pageCtx.Page;

        if (parsedResult.VideoTracks.Count == 0)
        {
            LogWarn("没有符合要求的视频流");
        }

        if (parsedResult.AudioTracks.Count == 0)
        {
            LogWarn("没有符合要求的音频流");
        }

        // 内容集要求 a/v 但解析不到任何音视频轨 → 中止；单一轨缺失属自然失效，有另一轨则照常产出
        if (parsedResult.VideoTracks.Count == 0 && parsedResult.AudioTracks.Count == 0
            && myOption.Content.HasAny(DownloadContent.Audio | DownloadContent.Video))
        {
            return PageOutcome.Abort(selected);
        }

        if (!myOption.Content.Has(DownloadContent.Video))
        {
            parsedResult.VideoTracks.Clear( );
        }

        if (!myOption.Content.Has(DownloadContent.Audio))
        {
            parsedResult.AudioTracks.Clear( );
            parsedResult.BackgroundAudioTracks.Clear( );
            parsedResult.RoleAudioList.Clear( );
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
        if (myOption.InteractiveQuality && !selected)
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

        if (myOption.Content.Has(DownloadContent.Danmaku) && await PageAssets.DownloadDanmakuAsync(session, savePath, ct))
        {
            return PageOutcome.Abort(selected);
        }

        if (myOption.Content.Has(DownloadContent.Cover))
        {
            var newCoverPath = Path.ChangeExtension(savePath, Path.GetExtension(pageCtx.CoverUrl));
            await DownloadFileAsync(pageCtx.CoverUrl, newCoverPath, downloadConfig, ct);
            MuxFinish.TryDeleteEmptyDir(pageCtx.TempDir);
            sink.Saved?.Invoke(newCoverPath);
            if (!myOption.Content.HasAny(DownloadContent.Audio | DownloadContent.Video))
            {
                return PageOutcome.Abort(selected);
            }
        }

        // 纯字幕 / 纯评论等无音视频内容：字幕已在 PrepareAsync 产出，此处统一中止
        if (!myOption.Content.HasAny(DownloadContent.Audio | DownloadContent.Video))
        {
            return PageOutcome.Abort(selected);
        }

        Log("已选择的流：");
        TrackSelect.PrintSelectedTrackInfo(selectedVideo, selectedAudio, p.dur);

        CdnHost.Apply(myOption, selectedVideo, selectedAudio, ctx.Fetch.Cfg);

        if (MuxFinish.TrySkipExisting(session, savePath, selected) is { } skipped)
        {
            return skipped;
        }

        var videoPath = pageCtx.VideoPath;
        var audioPath = pageCtx.AudioPath;
        List<AudioMaterial> audioMaterial = [];
        var useMp4box = myOption.UseMP4box;
        if (selectedVideo != null)
        {
            // 杜比视界 (id=126), 若 FFmpeg 版本小于 5.0, 使用 mp4box 封装
            if (selectedVideo.id == Config.DolbyVisionQn && !useMp4box && !ChapterMeta.CheckFFmpegDOVI(ctx.Run.Tools))
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
            // 配音流数可能少于主音频，序号越界时跳过该角色的配音
            var roleAudio = role.audio.ElementAtOrDefault(aIndex);
            if (roleAudio == null)
            {
                LogWarn($"P{p.index} 配音 [{role.title}] 没有序号 {aIndex} 的音频流，已跳过");
                continue;
            }

            role.path = Path.Combine(pageCtx.TempDir, Path.GetFileName(role.path));
            Log($"开始下载 P{p.index} 配音 [{role.title}]...");
            await DownloadAsync(roleAudio.baseUrl, role.path, downloadConfig, ct: ct);
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

        var inputs = new MuxFinish.MuxInputs(savePath, videoPath, audioPath, audioMaterial, useMp4box, selectedVideo?.codecs == "HEVC");
        return await MuxFinish.RunAsync(session, inputs, selected, ct);
    }
}
