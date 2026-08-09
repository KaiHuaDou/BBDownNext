using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core;
using BBDown.Core.Entity;
using BBDown.Download;
using BBDown.Drm;
using BBDown.Mux;
using BBDown.Util;

using static BBDown.Core.Logger;
using static BBDown.Download.DownloadUtil;

namespace BBDown.Media;

internal static class DashDownload
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
        // DRM 轨解密：下载完成后统一处理，成功产物覆盖加密原件（后续混流与清理路径不变）；
        // 任一轨不可解/失败时保留文件、跳过混流
        var decryptOk = true;
        if (selectedVideo != null)
        {
            decryptOk &= await DecryptTrackIfNeededAsync(session, videoPath, selectedVideo.IsDrm, selectedVideo.DrmType, selectedVideo.BiliDrmUri, ct);
        }

        if (selectedAudio != null)
        {
            decryptOk &= await DecryptTrackIfNeededAsync(session, audioPath, selectedAudio.IsDrm, selectedAudio.DrmType, selectedAudio.BiliDrmUri, ct);
        }

        if (selectedBackgroundAudio != null)
        {
            decryptOk &= await DecryptTrackIfNeededAsync(session, backgroundPath, selectedBackgroundAudio.IsDrm, selectedBackgroundAudio.DrmType, selectedBackgroundAudio.BiliDrmUri, ct);
        }

        foreach (var role in parsedResult.RoleAudioList)
        {
            var roleAudio = role.Audio.ElementAtOrDefault(aIndex);
            if (roleAudio != null)
            {
                decryptOk &= await DecryptTrackIfNeededAsync(session, role.Path, roleAudio.IsDrm, roleAudio.DrmType, roleAudio.BiliDrmUri, ct);
            }
        }

        if (!decryptOk)
        {
            LogError($"P{p.Index} 存在无法解密的 DRM 轨道，已保留原始文件，跳过混流");
            return PageOutcome.Abort(selection);
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

    // 解密选中轨（含背景音/配音）：非 DRM 轨直接放行；解密成功产物覆盖加密原件；
    // 不可解/失败返回 false 并打印保留路径与补救方法
    private static async Task<bool> DecryptTrackIfNeededAsync(DownloadSession session, string path, bool isDrm, string drmType, string? biliDrmUri, CancellationToken ct)
    {
        if (!isDrm)
        {
            return true;
        }

        var destPath = path + ".dec.mp4";
        var result = await DrmDecryptor.DecryptAsync(drmType, biliDrmUri, path, destPath, session.Ctx.Run.DrmKeys, session.Ctx.Run.Tools.Ffmpeg, ct);
        switch (result)
        {
            case DrmResult.Decrypted:
                File.Move(destPath, path, true);
                return true;
            case DrmResult.KeyMissing:
                var kid = DrmDecryptor.KidFromUri(biliDrmUri);
                LogWarn($"该轨为 bili_drm 加密且未提供匹配 key（KID={kid}），已保留加密文件：{path}。用 --drm-key {kid}:<key> 传入密钥后重试");
                return false;
            case DrmResult.Unsupported:
                LogError($"该轨为 Widevine 加密，无法自动解密，已保留加密文件：{path}");
                return false;
            default:
                LogError($"该轨解密失败，已保留加密文件：{path}");
                return false;
        }
    }
}