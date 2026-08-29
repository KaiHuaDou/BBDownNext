using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core.Download;
using BBDown.Core.Entity;

using static BBDown.Core.Logger;
using static BBDown.Core.Util.Utils;

namespace BBDown.Core.Mux;

public static class MuxFinish
{
    internal readonly record struct MuxInputs(
        string SavePath,
        string VideoPath,
        string AudioPath,
        List<AudioMaterial> AudioMaterial,
        MuxMode Mux,
        bool IsHevc);

    // 输出产物已存在且非空即可跳过：存在性与尺寸的纯判定，与 ToOutputPath 的扩展名映射解耦以便单测
    internal static bool ShouldSkip(bool exists, long size)
    {
        return exists && size > 0;
    }

    /// <summary>
    /// 目标文件已存在且非空时登记路径、清掉临时产物并返回中止结果；需要下载则返回 null。
    /// 产物扩展名随混流方式修正（mkv 视频 .mkv / 纯音频 .mka，其余 .mp4 / .m4a），
    /// 跳过检测须用同一扩展名，否则重跑会重复下载。
    /// </summary>
    internal static PageOutcome? TrySkipExisting(DownloadSession session, string savePath, TrackSelection selection)
    {
        savePath = ToOutputPath(savePath, session.Options.Mux, session.Options.Content.Has(DownloadContent.Video));

        var exists = File.Exists(savePath);
        if (!exists || !ShouldSkip(exists, new FileInfo(savePath).Length))
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
    /// 混流并清理临时文件，DASH 与 FLV 共用。--mux none 时直接中止，保留已下载的裸轨。
    /// </summary>
    internal static async Task<PageOutcome> RunAsync(DownloadSession session, MuxInputs inputs, TrackSelection selection, CancellationToken ct = default)
    {
        var (myOption, ctx, pageCtx, subtitleInfo, _, _) = session;
        if (myOption.Mux == MuxMode.None)
        {
            return PageOutcome.Abort(selection);
        }

        var p = pageCtx.Page;
        var savePath = ToOutputPath(inputs.SavePath, inputs.Mux, myOption.Content.Has(DownloadContent.Video));
        var streams = string.IsNullOrEmpty(inputs.AudioPath) ? "视频" : "音视频";
        Log($"开始混流{streams}{(subtitleInfo.Count != 0 ? "和字幕" : "")}...");
        var req = new MuxRequest(
            Mux: inputs.Mux,
            Bvid: p.Bvid,
            VideoPath: inputs.VideoPath,
            AudioPath: inputs.AudioPath,
            AudioMaterial: inputs.AudioMaterial,
            OutPath: savePath,
            Tools: ctx.Run.Tools,
            Desc: pageCtx.Desc,
            Title: pageCtx.Title,
            Author: p.OwnerName ?? "",
            EpisodeId: pageCtx.EpisodeTitle,
            Pic: File.Exists(pageCtx.CoverPath) ? pageCtx.CoverPath : "",
            Lang: ctx.Run.Lang,
            Subs: subtitleInfo,
            Content: myOption.Content,
            Points: p.Points,
            PubTime: p.PubTime,
            IsHevc: inputs.IsHevc,
            TrackNumber: p.Index,
            TotalTracks: pageCtx.PagesCount);
        var code = await Muxer.MuxAV(req, ct);
        if (code != 0 || !File.Exists(savePath) || new FileInfo(savePath).Length == 0)
        {
            LogError("混流失败");
            // 失败产物可能非空但损坏，留着会被下次 TrySkipExisting 误判为已完成；删除以保证重跑重新混流
            SafeDelete(savePath);
            return PageOutcome.Abort(selection);
        }

        Cleanup(pageCtx, inputs.VideoPath, inputs.AudioPath, inputs.AudioMaterial);
        return PageOutcome.Done(savePath, selection);
    }

    /// <summary>
    /// 按混流方式修正产物扩展名：mkv 模式视频 .mkv / 纯音频 .mka，其余 .mp4 / .m4a。
    /// SavePath.Format 恒产出 .mp4 基底，纯音频与 mkv 容器在此统一换后缀。
    /// </summary>
    internal static string ToOutputPath(string savePath, MuxMode mux, bool hasVideo)
    {
        var ext = mux == MuxMode.Mkv ? (hasVideo ? ".mkv" : ".mka") : (hasVideo ? ".mp4" : ".m4a");
        return Path.ChangeExtension(savePath, ext);
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
        // 下载层临时文件随 track 一起清理：只在混流成功时走到这里，
        // 失败/Ctrl+C 时 DownloadAsync 保留 .download，重跑即可续上
        DownloadUtil.Discard(videoPath);
        DownloadUtil.Discard(audioPath);
        var trackPath = string.IsNullOrEmpty(videoPath) ? audioPath : videoPath;
        if (pageCtx.Page.Points.Count != 0 && !string.IsNullOrEmpty(trackPath))
        {
            SafeDelete(Path.Combine(Path.GetDirectoryName(trackPath) ?? "", "chapters"));
        }

        foreach (var a in audioMaterial)
        {
            SafeDelete(a.Path);
            DownloadUtil.Discard(a.Path);
        }

        if (pageCtx.DeleteCoverAfterMux)
        {
            SafeDelete(pageCtx.CoverPath);
        }

        TryDeleteEmptyDir(pageCtx.TempDir);
    }
}
