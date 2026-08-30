using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core.Download;
using BBDown.Core.Entity;
using BBDown.Core.Util;

using static BBDown.Core.Util.Utils;

namespace BBDown.Core.Mux;

/// <summary>
/// 一次混流的不可变入参集合，由 <see cref="MuxFinish"/> 组装后交给 <see cref="Muxer.MuxAV"/>。
/// 调用方按名填字段，下游 <see cref="MuxArgs"/> 的 <c>Build*</c> 直接读 <c>req</c> 上的路径与元数据，不再数位置。
/// </summary>
public sealed record MuxRequest(
    MuxMode Mux,
    string Bvid,
    string VideoPath,
    string AudioPath,
    List<AudioMaterial> AudioMaterial,
    string OutPath,
    ToolPaths Tools,
    string Desc,
    string Title,
    string Author,
    string EpisodeId,
    string Pic,
    string Lang,
    List<Subtitle>? Subs,
    DownloadContent Content,
    List<ViewPoint>? Points,
    long PubTime,
    bool IsHevc,
    // 多P（总集数大于 1）时填当前分 P 序号与该视频总集数，写入 track / track_total 元数据
    int TrackNumber,
    int TotalTracks);

/// <summary>
/// 混流的执行侧：组装入参、起外部进程、失败时清理临时产物。参数构造在 <see cref="MuxArgs"/>。
/// </summary>
public static class Muxer
{
    public static async Task<int> MuxAV(MuxRequest req, CancellationToken ct = default)
    {
        var videoPath = req.VideoPath;
        var audioPath = req.AudioPath;
        if (!req.Content.Has(DownloadContent.Video) && audioPath.Length != 0)
        {
            videoPath = "";
        }

        if (!req.Content.Has(DownloadContent.Audio))
        {
            audioPath = "";
        }

        // 音视频独占修正与有效字幕回写进副本，下游 Build* 只读 req 上的路径
        var validSubs = req.Subs?.Where(s => File.Exists(s.Path) && File.ReadAllText(s.Path).Length != 0).ToList( ) ?? [];
        req = req with { VideoPath = videoPath, AudioPath = audioPath, Subs = validSubs };

        var outDir = Path.GetDirectoryName(req.OutPath);
        if (!string.IsNullOrEmpty(outDir))
        {
            Directory.CreateDirectory(outDir);
        }

        string? chapterFile = null;
        if (req.Points is { Count: > 0 } points)
        {
            chapterFile = Path.Combine(Path.GetDirectoryName(videoPath.Length == 0 ? audioPath : videoPath)!, "chapters");
            File.WriteAllText(chapterFile, req.Mux == MuxMode.Mp4box ? ChapterMeta.GetMp4boxMetaString(points) : ChapterMeta.GetFFmpegMetaString(points));
        }

        // MuxMode.None 由 MuxFinish 提前中止，不会走到这里；Mkv 走 FFmpeg（matroska 容器）
        // 混流失败时章节临时文件未进入 MuxFinish 的成功清理路径，此处于异常分支删除，避免残留
        try
        {
            return req.Mux == MuxMode.Mp4box
                ? await MuxByMp4boxAsync(req, chapterFile, ct)
                : await Utils.RunExe(req.Tools.Ffmpeg, MuxArgs.BuildFFmpegArgs(req, chapterFile, Config.DebugLog), ct);
        }
        catch
        {
            if (chapterFile != null)
            {
                SafeDelete(chapterFile);
            }

            throw;
        }
    }

    // 标签走临时文件而非命令行：见 BuildMp4boxTagFile 的说明
    private static async Task<int> MuxByMp4boxAsync(MuxRequest req, string? chapterFile, CancellationToken ct)
    {
        var tagFile = Path.Combine(Path.GetDirectoryName(req.OutPath) ?? ".", $"itags-{Guid.NewGuid( ):N}.txt");
        File.WriteAllText(tagFile, MuxArgs.BuildMp4boxTagFile(req), new UTF8Encoding(false));
        try
        {
            return await Utils.RunExe(req.Tools.Mp4box, MuxArgs.BuildMp4boxArgs(req, tagFile, chapterFile, Config.DebugLog), ct);
        }
        finally
        {
            SafeDelete(tagFile);
        }
    }

    public static async Task MergeFLV(string[] files, string outPath, ToolPaths tools, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(files);
        if (files.Length == 0)
        {
            return;
        }

        if (files.Length == 1)
        {
            File.Move(files[0], outPath, true);
            return;
        }

        // 只合并本次转出的分段：扫目录取 .ts 会混入并发任务或上次残留的文件，且顺序不受控（P1-22）
        List<string> tsFiles = new(files.Length);
        try
        {
            foreach (var file in files)
            {
                var tmpFile = Path.Combine(Path.GetDirectoryName(file)!, Path.GetFileNameWithoutExtension(file) + ".ts");
                var code = await Utils.RunExe(tools.Ffmpeg, ["-loglevel", "warning", "-y", "-i", file, "-map", "0", "-c", "copy", "-f", "mpegts", "-bsf:v", "h264_mp4toannexb", tmpFile], ct);
                if (code != 0)
                {
                    throw new InvalidOperationException($"FLV 分段 {file} 转封装失败（退出码 {code}）");
                }

                tsFiles.Add(tmpFile);
                SafeDelete(file);
            }

            CombineMultipleFilesIntoSingleFile([.. tsFiles], outPath);
        }
        finally
        {
            tsFiles.ForEach(SafeDelete);
        }
    }
}
