using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core;
using BBDown.Core.Entity;
using BBDown.Core.Download;
using BBDown.Core.Mux;
using BBDown.Core.Util;

using static BBDown.Core.Logger;
using static BBDown.Core.Download.DownloadUtil;

namespace BBDown.Core.Media;

public static class DashDownload
{
    internal static async Task<PageOutcome> RunAsync(ParsedResult parsedResult, DownloadSession session, TrackSelection selection, CancellationToken ct = default)
    {
        var (myOption, ctx, pageCtx, subtitleInfo, downloadConfig, sink) = session;
        var p = pageCtx.Page;
        var (selected, vIndex, aIndex) = selection;

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
            return PageOutcome.Abort(selection);
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
            TrackSelect.PrintAllTracksInfo(parsedResult, p.Dur, myOption.OnlyShowInfo);
        }

        // 仅展示 跳过下载
        if (myOption.OnlyShowInfo)
        {
            return PageOutcome.Abort(selection);
        }

        if (myOption.InteractiveQuality && !selected)
        {
            TrackSelect.PickTracks(parsedResult, ref vIndex, ref aIndex);
            selection = selection with { Selected = true, VIndex = vIndex, AIndex = aIndex };
        }

        var selectedVideo = parsedResult.VideoTracks.ElementAtOrDefault(vIndex);
        var selectedAudio = parsedResult.AudioTracks.ElementAtOrDefault(aIndex);
        var selectedBackgroundAudio = parsedResult.BackgroundAudioTracks.ElementAtOrDefault(aIndex);

        LogDebug("Format Before: " + ctx.SavePathFormat);
        var savePath = SavePath.Build(ctx, pageCtx, selectedVideo, selectedAudio);
        LogDebug("Format After: " + savePath);

        if (myOption.Content.Has(DownloadContent.Danmaku) && await PageAssets.DownloadDanmakuAsync(session, savePath, ct))
        {
            return PageOutcome.Abort(selection);
        }

        if (myOption.Content.Has(DownloadContent.Cover))
        {
            var newCoverPath = Path.ChangeExtension(savePath, Path.GetExtension(pageCtx.CoverUrl));
            await DownloadFileAsync(pageCtx.CoverUrl, newCoverPath, downloadConfig, ct);
            MuxFinish.TryDeleteEmptyDir(pageCtx.TempDir);
            sink.Saved?.Invoke(newCoverPath);
            if (!myOption.Content.HasAny(DownloadContent.Audio | DownloadContent.Video))
            {
                return PageOutcome.Abort(selection);
            }
        }

        // 纯字幕 / 纯评论等无音视频内容：字幕已在 PrepareAsync 产出，此处统一中止
        if (!myOption.Content.HasAny(DownloadContent.Audio | DownloadContent.Video))
        {
            return PageOutcome.Abort(selection);
        }

        Log("已选择的流：");
        TrackSelect.PrintSelectedTrackInfo(selectedVideo, selectedAudio, p.Dur);

        CdnHost.Apply(myOption, selectedVideo, selectedAudio, ctx.Fetch.Cfg);

        if (MuxFinish.TrySkipExisting(session, savePath, selection) is { } skipped)
        {
            return skipped;
        }

        var videoPath = pageCtx.VideoPath;
        var audioPath = pageCtx.AudioPath;
        List<AudioMaterial> audioMaterial = [];
        var mux = myOption.Mux;
        var backgroundPath = "";
        if (selectedVideo != null)
        {
            // 杜比视界 (id=126), 若 FFmpeg 版本小于 5.0, 使用 mp4box 封装
            if (selectedVideo.Id == Config.DolbyVisionQn && mux == MuxMode.Mpeg4 && !ChapterMeta.CheckFFmpegDOVI(ctx.Run.Tools))
            {
                LogWarn("您的 FFmpeg 版本小于 5.0，杜比视界将使用 MP4Box 混流...");
                mux = MuxMode.Mp4box;
            }

            Log($"开始下载 P{p.Index} 视频...");
            await DownloadAsync(selectedVideo.BaseUrl, videoPath, downloadConfig, ct: ct);
        }

        if (selectedAudio != null)
        {
            Log($"开始下载 P{p.Index} 音频...");
            await DownloadAsync(selectedAudio.BaseUrl, audioPath, downloadConfig, ct: ct);
        }

        if (selectedBackgroundAudio != null)
        {
            backgroundPath = Path.Combine(pageCtx.TempDir, $"{p.Aid}.{p.Cid}.P{p.Index}.back_ground.m4a");
            Log($"开始下载 P{p.Index} 背景配音...");
            await DownloadAsync(selectedBackgroundAudio.BaseUrl, backgroundPath, downloadConfig, ct: ct);
            audioMaterial.Add(new AudioMaterial { Title = "背景音频", PersonName = "", Path = backgroundPath });
        }

        foreach (var role in parsedResult.RoleAudioList)
        {
            // 配音流数可能少于主音频，序号越界时跳过该角色的配音
            var roleAudio = role.Audio.ElementAtOrDefault(aIndex);
            if (roleAudio == null)
            {
                LogWarn($"P{p.Index} 配音 [{role.Title}] 没有序号 {aIndex} 的音频流，已跳过");
                continue;
            }

            role.Path = Path.Combine(pageCtx.TempDir, Path.GetFileName(role.Path));
            Log($"开始下载 P{p.Index} 配音 [{role.Title}]...");
            await DownloadAsync(roleAudio.BaseUrl, role.Path, downloadConfig, ct: ct);
            audioMaterial.Add(new AudioMaterial { Title = role.Title, PersonName = role.PersonName, Path = role.Path });
        }

        Log($"P{p.Index} 下载完成");
        // 外部后处理（可选）：配置了 --post-process 时对每条轨调用已配置的处理进程，
        // 加密与否由处理方自行判断；成功产物覆盖原轨，未配置 / 失败 / 超时一律静默保留原文件
        if (selectedVideo != null)
        {
            await TryPostProcessAsync(session, videoPath, "video", p.Aid, p.Cid, ct);
        }

        if (selectedAudio != null)
        {
            await TryPostProcessAsync(session, audioPath, "audio", p.Aid, p.Cid, ct);
        }

        if (selectedBackgroundAudio != null)
        {
            await TryPostProcessAsync(session, backgroundPath, "background", p.Aid, p.Cid, ct);
        }

        foreach (var role in parsedResult.RoleAudioList)
        {
            var roleAudio = role.Audio.ElementAtOrDefault(aIndex);
            if (roleAudio != null)
            {
                await TryPostProcessAsync(session, role.Path, "role", p.Aid, p.Cid, ct);
            }
        }

        if (parsedResult.VideoTracks.Count == 0)
        {
            videoPath = "";
        }

        if (parsedResult.AudioTracks.Count == 0)
        {
            audioPath = "";
        }

        var inputs = new MuxFinish.MuxInputs(savePath, videoPath, audioPath, audioMaterial, mux, selectedVideo?.Codecs == "HEVC");
        return await MuxFinish.RunAsync(session, inputs, selection, ct);
    }

    // 对每条轨发起外部后处理（加密与否由处理方判断）；产物校验通过后覆盖原轨，其余情况静默
    private static async Task TryPostProcessAsync(DownloadSession session, string path, string kind, string aid, string cid, CancellationToken ct)
    {
        if (!PostProcessClient.Enabled)
        {
            return;
        }

        var destPath = path + ".out.mp4";
        if (await PostProcessClient.TryProcessAsync(aid, cid, kind, path, destPath, session.Ctx.Run.Tools.Ffmpeg, ct)
            && File.Exists(destPath) && new FileInfo(destPath).Length > 0)
        {
            File.Move(destPath, path, true);
        }
    }
}
