using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Download;

using static BBDown.Core.Entity.Entity;
using static BBDown.Core.Logger;
using static BBDown.Util.Utils;

namespace BBDown.Mux;

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
    /// 内容集无 v（仅音频）时产物为 .m4a，跳过检测须用同一扩展名，否则重跑会重复下载。
    /// </summary>
    internal static PageOutcome? TrySkipExisting(DownloadSession session, string savePath, TrackSelection selection)
    {
        if (!session.Options.Content.Has(DownloadContent.Video))
        {
            savePath = ToAudioOnlyPath(savePath);
        }

        if (!File.Exists(savePath) || new FileInfo(savePath).Length == 0)
        {
            return null;
        }

        Log($"{savePath} 已存在，跳过下载...");
        session.Sink.Saved?.Invoke(savePath);
        SafeDelete(session.PageCtx.CoverPath);
        TryDeleteEmptyDir(session.PageCtx.TempDir);
        return PageOutcome.Abort(selection);
    }

    /// <summary>
    /// 混流并清理临时文件，DASH 与 FLV 共用。--skip-mux 时直接中止，保留已下载的裸轨。
    /// </summary>
    internal static async Task<PageOutcome> RunAsync(DownloadSession session, MuxInputs inputs, TrackSelection selection, CancellationToken ct = default)
    {
        var (myOption, ctx, pageCtx, subtitleInfo, _, _) = session;
        if (myOption.SkipMux)
        {
            return PageOutcome.Abort(selection);
        }

        var p = pageCtx.Page;
        var savePath = !myOption.Content.Has(DownloadContent.Video) ? ToAudioOnlyPath(inputs.SavePath) : inputs.SavePath;
        var streams = string.IsNullOrEmpty(inputs.AudioPath) ? "视频" : "音视频";
        Log($"开始混流{streams}{(subtitleInfo.Count != 0 ? "和字幕" : "")}...");
        var req = new MuxRequest(
            UseMp4box: inputs.UseMp4box,
            Bvid: p.bvid,
            VideoPath: inputs.VideoPath,
            AudioPath: inputs.AudioPath,
            AudioMaterial: inputs.AudioMaterial,
            OutPath: savePath,
            Tools: ctx.Run.Tools,
            Desc: pageCtx.Desc,
            Title: pageCtx.Title,
            Author: p.ownerName ?? "",
            EpisodeId: pageCtx.EpisodeTitle,
            Pic: File.Exists(pageCtx.CoverPath) ? pageCtx.CoverPath : "",
            Lang: ctx.Run.Lang,
            Subs: subtitleInfo,
            Content: myOption.Content,
            Points: p.points,
            PubTime: p.pubTime,
            IsHevc: inputs.IsHevc);
        var code = await Muxer.MuxAV(req, ct);
        if (code != 0 || !File.Exists(savePath) || new FileInfo(savePath).Length == 0)
        {
            LogError("混流失败");
            return PageOutcome.Abort(selection);
        }

        Cleanup(pageCtx, inputs.VideoPath, inputs.AudioPath, inputs.AudioMaterial);
        return PageOutcome.Done(savePath, selection);
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

    internal static void Cleanup(PageContext pageCtx, string videoPath, string audioPath, List<AudioMaterial> audioMaterial)
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
