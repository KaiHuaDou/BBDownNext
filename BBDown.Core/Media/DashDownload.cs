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
using static BBDown.Core.Util.RetryUtil;
using static BBDown.Core.Util.Utils;

namespace BBDown.Core.Media;

public static class DashDownload
{
    internal static async Task<PageOutcome> RunAsync(ParsedResult parsedResult, DownloadSession session, TrackSelection selection, CancellationToken ct = default)
    {
        var (myOption, ctx, pageCtx, _, _, _) = session;
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
            (vIndex, aIndex) = await TrackSelect.PickTracksAsync(parsedResult, p.Dur, ct);
            selection = selection with { Selected = true, VIndex = vIndex, AIndex = aIndex };
        }

        var selectedVideo = parsedResult.VideoTracks.ElementAtOrDefault(vIndex);
        var selectedAudio = parsedResult.AudioTracks.ElementAtOrDefault(aIndex);
        var selectedBackgroundAudio = parsedResult.BackgroundAudioTracks.ElementAtOrDefault(aIndex);

        LogDebug("Format Before: " + ctx.SavePathFormat);
        var savePath = SavePath.Build(ctx, pageCtx, selectedVideo, selectedAudio);
        LogDebug("Format After: " + savePath);

        // 弹幕非必要项，独立重试，耗尽仅跳过（无音视频时后续会自然中止该 P）
        if (await PageAssets.TryDownloadDanmakuAsync(session, savePath, selection, ct) is { } danmakuAbort)
        {
            return danmakuAbort;
        }

        // 独立封面（c）非必要项，独立重试，耗尽仅跳过（不影响音视频）；纯封面任务落盘后即中止
        if (await PageAssets.TryDownloadCoverAsync(session, savePath, selection, ct) is { } coverAbort)
        {
            return coverAbort;
        }

        // 纯字幕 / 纯评论等无音视频内容：字幕已在 PrepareAsync 产出，此处统一中止
        if (myOption.Content.IsAssetOnly( ))
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
        var (audioMaterial, mux) = await DownloadTracksAsync(parsedResult, session, selection, pageCtx, videoPath, audioPath, ct);

        if (parsedResult.VideoTracks.Count == 0)
        {
            videoPath = "";
        }

        if (parsedResult.AudioTracks.Count == 0)
        {
            audioPath = "";
        }

        var inputs = new MuxFinish.MuxInputs(savePath, videoPath, audioPath, audioMaterial, mux, selectedVideo?.Codecs == "HEVC");
        // 混流是整 P 必要收尾，独立重试；耗尽则整 P 失败（不影响其他分 P）
        return await RetryAsync(
            async ( ) => await MuxFinish.RunAsync(session, inputs, selection, ct),
            myOption.MaxRetry, $"P{p.Index} 混流", ct, ex => PageDownload.ShouldRetry(ex, ct));
    }

    // 下载全部已选轨（视频 / 音频 / 背景配音 / 角色配音）并就地后处理；返回混流所需的配音素材与
    // （可能因杜比视界调整的）封装模式。主媒体下载窗口（进度条显隐）与“实际下载哪些轨”共用同一组布尔，统一在此收口
    private static async Task<(List<AudioMaterial> AudioMaterial, MuxMode Mux)> DownloadTracksAsync(
        ParsedResult parsedResult, DownloadSession session, TrackSelection selection,
        PageContext pageCtx, string videoPath, string audioPath, CancellationToken ct)
    {
        var (myOption, ctx, _, _, _, _) = session;
        var p = pageCtx.Page;
        var (_, vIndex, aIndex) = selection;
        var selectedVideo = parsedResult.VideoTracks.ElementAtOrDefault(vIndex);
        var selectedAudio = parsedResult.AudioTracks.ElementAtOrDefault(aIndex);
        var selectedBackgroundAudio = parsedResult.BackgroundAudioTracks.ElementAtOrDefault(aIndex);
        var mux = myOption.Mux;

        var hasVideo = selectedVideo != null;
        var hasAudio = selectedAudio != null;
        var hasBackgroundAudio = selectedBackgroundAudio != null;
        var hasRoleAudio = parsedResult.RoleAudioList.Count != 0;
        var backgroundPath = "";
        List<AudioMaterial> audioMaterial = [];
        // 成功下载的角色配音，供后续后处理使用；失败角色已清理临时文件、不加入
        List<AudioMaterialInfo> succeededRoles = [];
        // 主媒体下载窗口：只有音视频轨下载时进度条才显示（阶段内采样经 ProgressBus 上报）
        if (hasVideo || hasAudio || hasBackgroundAudio || hasRoleAudio)
        {
            using var stage = ProgressBus.BeginStage("下载");
            if (hasVideo)
            {
                // 杜比视界 (id=126), 若 FFmpeg 版本小于 5.0, 使用 mp4box 封装
                if (selectedVideo!.Id == Config.DolbyVisionQn && mux == MuxMode.Mpeg4 && !await ChapterMeta.CheckFFmpegDOVIAsync(ctx.Run.Tools, ct))
                {
                    LogWarn("您的 FFmpeg 版本小于 5.0，杜比视界将使用 MP4Box 混流...");
                    mux = MuxMode.Mp4box;
                }

                // 视频是必要轨，重试耗尽则整 P 失败
                Log($"开始下载 P{p.Index} 视频...");
                await TryDownloadTrackAsync(session, selectedVideo!.BaseUrl, videoPath, $"P{p.Index} 视频", true, ct);
            }

            if (hasAudio)
            {
                // 音频是必要轨，重试耗尽则整 P 失败
                Log($"开始下载 P{p.Index} 音频...");
                await TryDownloadTrackAsync(session, selectedAudio!.BaseUrl, audioPath, $"P{p.Index} 音频", true, ct);
            }

            // 背景配音非必要轨，失败仅跳过该轨
            if (hasBackgroundAudio)
            {
                backgroundPath = Path.Combine(pageCtx.TempDir, $"{p.Aid}.{p.Cid}.P{p.Index}.back_ground.m4a");
                Log($"开始下载 P{p.Index} 背景配音...");
                if (!await TryDownloadTrackAsync(session, selectedBackgroundAudio!.BaseUrl, backgroundPath, $"P{p.Index} 背景配音", false, ct))
                {
                    backgroundPath = "";
                    hasBackgroundAudio = false;
                }
            }

            if (hasBackgroundAudio)
            {
                audioMaterial.Add(new AudioMaterial { Title = "背景音频", PersonName = "", Path = backgroundPath });
            }

            // 角色配音非必要轨，逐角色独立下载，单个角色失败仅跳过该角色
            if (hasRoleAudio)
            {
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
                    if (!await TryDownloadTrackAsync(session, roleAudio.BaseUrl, role.Path, $"P{p.Index} 配音 [{role.Title}]", false, ct))
                    {
                        continue;
                    }

                    audioMaterial.Add(new AudioMaterial { Title = role.Title, PersonName = role.PersonName, Path = role.Path });
                    succeededRoles.Add(role);
                }
            }
        }

        Log($"P{p.Index} 下载完成");
        // 外部后处理（可选）：配置了 --post-process 时对每条轨调用已配置的处理进程，
        // 加密与否由处理方自行判断；成功产物覆盖原轨，未配置 / 失败 / 超时一律静默保留原文件
        if (hasVideo)
        {
            await TryPostProcessAsync(session, videoPath, "video", p.Aid, p.Cid, ct);
        }

        if (hasAudio)
        {
            await TryPostProcessAsync(session, audioPath, "audio", p.Aid, p.Cid, ct);
        }

        if (hasBackgroundAudio)
        {
            await TryPostProcessAsync(session, backgroundPath, "background", p.Aid, p.Cid, ct);
        }

        foreach (var role in succeededRoles)
        {
            await TryPostProcessAsync(session, role.Path, "role", p.Aid, p.Cid, ct);
        }

        return (audioMaterial, mux);
    }

    // 单轨下载收口：必要轨（required）重试耗尽异常上抛（整 P 失败）；可选轨失败仅告警返回 false。
    // 半截文件未进入 audioMaterial、混流清理不会删，就地清理避免残留
    private static async Task<bool> TryDownloadTrackAsync(DownloadSession session, string url, string path, string label, bool required, CancellationToken ct)
    {
        var (_, _, _, _, downloadConfig, _) = session;
        try
        {
            await RetryAsync(
                async ( ) => await DownloadAsync(url, path, downloadConfig, ct: ct),
                session.Options.MaxRetry, label, ct, ex => PageDownload.ShouldRetry(ex, ct));
            return true;
        }
        catch (Exception ex)
        {
            if (required)
            {
                throw;
            }

            LogWarn($"{label}失败，已跳过：{ex.Message}");
            SafeDelete(path);
            return false;
        }
    }

    // 对每条轨发起外部后处理（加密与否由处理方判断）；产物校验通过后覆盖原轨，其余情况静默
    private static async Task TryPostProcessAsync(DownloadSession session, string path, string kind, string aid, string cid, CancellationToken ct)
    {
        var destPath = path + ".out.mp4";
        if (await PostProcessClient.TryProcessAsync(session.Options.PostProcessPath, aid, cid, kind, path, destPath, session.Ctx.Run.Tools.Ffmpeg, ct)
            && File.Exists(destPath) && new FileInfo(destPath).Length > 0)
        {
            File.Move(destPath, path, true);
        }
    }
}
