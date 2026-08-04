using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core;
using BBDown.Core.Entity;

using static BBDown.DownloadUtil;
using static BBDown.Core.Entity.Entity;
using static BBDown.Core.Logger;
using static BBDown.Utils;
using PageOutcome = BBDown.PageDownload.PageOutcome;

namespace BBDown;

internal static class MuxFinish
{
    internal readonly record struct MuxInputs(
        string SavePath,
        string VideoPath,
        string AudioPath,
        List<AudioMaterial> AudioMaterial,
        bool UseMp4box,
        bool IsHevc);

    /// <summary>
    /// 目标文件已存在且非空时登记路径、清掉临时产物并返回中止结果；需要下载则返回 null。
    /// </summary>
    internal static PageOutcome? TrySkipExisting(DownloadSession session, string savePath, bool selected)
    {
        if (!File.Exists(savePath) || new FileInfo(savePath).Length == 0)
        {
            return null;
        }

        Log($"{savePath} 已存在，跳过下载...");
        session.RelatedTask?.SavePaths.Add(savePath);
        SafeDelete(session.PageCtx.CoverPath);
        TryDeleteEmptyDir(session.PageCtx.TempDir);
        return PageOutcome.Abort(selected);
    }

    /// <summary>
    /// 混流并清理临时文件，DASH 与 FLV 共用。--skip-mux 时直接中止，保留已下载的裸轨。
    /// </summary>
    internal static async Task<PageOutcome> RunAsync(DownloadSession session, MuxInputs inputs, bool selected, CancellationToken ct = default)
    {
        var (myOption, ctx, pageCtx, subtitleInfo, _, _) = session;
        if (myOption.SkipMux)
        {
            return PageOutcome.Abort(selected);
        }

        var p = pageCtx.Page;
        var savePath = myOption.AudioOnly ? ToAudioOnlyPath(inputs.SavePath) : inputs.SavePath;
        var streams = string.IsNullOrEmpty(inputs.AudioPath) ? "视频" : "音视频";
        Log($"开始混流{streams}{(subtitleInfo.Count != 0 ? "和字幕" : "")}...");
        var code = await Muxer.MuxAV(inputs.UseMp4box, p.bvid, inputs.VideoPath, inputs.AudioPath, inputs.AudioMaterial, savePath,
            pageCtx.Desc,
            pageCtx.Title,
            p.ownerName ?? "",
            pageCtx.EpisodeTitle,
            File.Exists(pageCtx.CoverPath) ? pageCtx.CoverPath : "",
            ctx.Lang,
            subtitleInfo, myOption.AudioOnly, myOption.VideoOnly, p.points, p.pubTime, myOption.NoMetadata, inputs.IsHevc, ct);
        if (code != 0 || !File.Exists(savePath) || new FileInfo(savePath).Length == 0)
        {
            LogError("混流失败");
            return PageOutcome.Abort(selected);
        }

        Cleanup(pageCtx, inputs.VideoPath, inputs.AudioPath, subtitleInfo, inputs.AudioMaterial);
        return PageOutcome.Done(savePath, selected);
    }

    internal static string ToAudioOnlyPath(string savePath)
    {
        return Path.ChangeExtension(savePath, ".m4a");
    }

    internal static void TryDeleteEmptyDir(string path)
    {
        if (Directory.Exists(path) && Directory.GetFiles(path).Length == 0)
        {
            Directory.Delete(path, true);
        }
    }

    internal static void Cleanup(PageContext pageCtx, string videoPath, string audioPath, List<Subtitle> subtitleInfo, List<AudioMaterial> audioMaterial)
    {
        Log("清理临时文件...");
        SafeDelete(videoPath);
        SafeDelete(audioPath);
        // 续传状态清单随 track 一起清理：只在混流成功时走到这里，
        // 失败/Ctrl+C 时 DownloadAsync 保留 .bbdown.part/.json，重跑即可续上
        PartFile.Discard(videoPath);
        PartFile.Discard(audioPath);
        var trackPath = string.IsNullOrEmpty(videoPath) ? audioPath : videoPath;
        if (pageCtx.Page.points.Count != 0 && !string.IsNullOrEmpty(trackPath))
        {
            SafeDelete(Path.Combine(Path.GetDirectoryName(trackPath) ?? "", "chapters"));
        }

        foreach (var s in subtitleInfo)
        {
            SafeDelete(s.path);
        }

        foreach (var a in audioMaterial)
        {
            SafeDelete(a.path);
            PartFile.Discard(a.path);
        }

        if (pageCtx.DeleteCoverAfterMux)
        {
            SafeDelete(pageCtx.CoverPath);
        }

        TryDeleteEmptyDir(pageCtx.TempDir);
    }
}
