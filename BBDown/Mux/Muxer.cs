using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core;

using static BBDown.Core.Entity.Entity;
using static BBDown.Core.Logger;
using static BBDown.Core.Util.SubUtil;
using static BBDown.Util.Utils;

namespace BBDown.Mux;

/// <summary>
/// 一次混流的不可变入参集合，由 <see cref="MuxFinish"/> 组装后交给 <see cref="Muxer.MuxAV"/>。
/// 把原先 20 余散参收敛为单一值对象（M2 item 3 深层收尾），调用方按名填字段、不再数位置。
/// </summary>
internal sealed record MuxRequest(
    bool UseMp4box,
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
    bool AudioOnly,
    bool VideoOnly,
    List<ViewPoint>? Points,
    long PubTime,
    bool NoMetadata,
    bool IsHevc);

internal static class Muxer
{
    internal static async Task<int> RunExe(string app, List<string> args, CancellationToken ct = default)
    {
        LogDebug("{0}命令: {1}", Path.GetFileNameWithoutExtension(app), FormatArgs(args));
        using Process p = new( );
        p.StartInfo.FileName = app;
        foreach (var arg in args)
        {
            p.StartInfo.ArgumentList.Add(arg);
        }

        p.StartInfo.UseShellExecute = false;
        p.StartInfo.RedirectStandardError = true;
        p.StartInfo.CreateNoWindow = true;
        p.StartInfo.StandardErrorEncoding = Encoding.UTF8;
        p.ErrorDataReceived += (sendProcess, output) =>
        {
            if (!string.IsNullOrWhiteSpace(output.Data))
            {
                Log(output.Data);
            }
        };
        p.Start( );
        p.BeginErrorReadLine( );
        // 取消时杀掉子进程, 避免 ffmpeg 在 WaitForExitAsync 已取消后仍挂起
        await using var _ = ct.Register(( ) => { try { p.Kill( ); } catch { } });
        await p.WaitForExitAsync(ct);
        return p.ExitCode;
    }

    private static string FormatArgs(List<string> args)
    {
        return string.Join(' ', args.Select(a => a.Length == 0 || a.Contains(' ') ? $"\"{a}\"" : a));
    }

    internal static List<string> BuildMp4boxArgs(MuxRequest req, List<Subtitle> subs, string? chapterFile, bool debugLog)
    {
        var url = $"{BiliApi.VideoPage}/{req.Bvid}/";
        var videoPath = req.VideoPath;
        var audioPath = req.AudioPath;
        var outPath = req.OutPath;
        var desc = req.Desc;
        var title = req.Title;
        var author = req.Author;
        var episodeId = req.EpisodeId;
        var pic = req.Pic;
        var lang = req.Lang;
        var audioOnly = req.AudioOnly;
        List<string> args = [];
        if (debugLog)
        {
            args.Add("-v");
        }

        args.AddRange(["-inter", "500", "-noprog"]);

        var trackId = 0;
        if (videoPath.Length != 0)
        {
            args.AddRange(["-add", $"{videoPath}#trackID={(audioOnly && audioPath.Length == 0 ? 2 : 1)}:name="]);
            trackId++;
        }

        if (audioPath.Length != 0)
        {
            args.AddRange(["-add", $"{audioPath}:lang={(lang.Length == 0 ? "und" : lang)}"]);
            trackId++;
        }

        if (chapterFile != null)
        {
            args.AddRange(["-chap", chapterFile]);
        }

        foreach (var sub in subs)
        {
            trackId++;
            var (code, name) = GetSubtitleCode(sub.lan);
            args.AddRange(["-add", $"{sub.path}#trackID=1:name=:hdlr=sbtl:lang={code}"]);
            args.AddRange(["-udta", $"{trackId}:type=name:str={name}"]);
        }

        var tags = new StringBuilder("tool=");
        if (pic.Length != 0)
        {
            tags.Append($":cover={pic}");
        }

        if (episodeId.Length != 0)
        {
            tags.Append($":album={title}:title={episodeId}");
        }
        else
        {
            tags.Append($":title={title}");
        }

        tags.Append($":sdesc={desc}");
        tags.Append($":comment={url}");
        tags.Append($":artist={author}");
        args.AddRange(["-itags", tags.ToString( )]);

        args.AddRange(["-new", "--", outPath]);
        return args;
    }

    internal static List<string> BuildFFmpegArgs(MuxRequest req, List<Subtitle> subs, string? chapterFile, bool tagHvc1, bool debugLog)
    {
        var url = $"{BiliApi.VideoPage}/{req.Bvid}/";
        var videoPath = req.VideoPath;
        var audioPath = req.AudioPath;
        var audioMaterial = req.AudioMaterial;
        var outPath = req.OutPath;
        var desc = req.Desc;
        var title = req.Title;
        var author = req.Author;
        var episodeId = req.EpisodeId;
        var pic = req.Pic;
        var lang = req.Lang;
        var audioOnly = req.AudioOnly;
        var pubTime = req.PubTime;
        var noMetadata = req.NoMetadata;
        List<string> args = ["-loglevel", debugLog ? "verbose" : "warning", "-y"];
        List<string> meta = [];
        var inputCount = 0;

        foreach (var path in new[] { videoPath, audioPath })
        {
            if (path.Length == 0)
            {
                continue;
            }

            inputCount++;
            args.AddRange(["-i", path]);
        }

        if (audioMaterial.Count != 0)
        {
            var audioIndex = 0;
            meta.AddRange(["-metadata:s:a:0", "title=原音频"]);
            foreach (var audio in audioMaterial)
            {
                inputCount++;
                audioIndex++;
                args.AddRange(["-i", audio.path]);
                if (!string.IsNullOrWhiteSpace(audio.title))
                {
                    meta.AddRange([$"-metadata:s:a:{audioIndex}", $"title={audio.title}"]);
                }

                if (!string.IsNullOrWhiteSpace(audio.personName))
                {
                    meta.AddRange([$"-metadata:s:a:{audioIndex}", $"artist={audio.personName}"]);
                }
            }
        }

        if (pic.Length != 0)
        {
            inputCount++;
            args.AddRange(["-i", pic]);
        }

        for (var i = 0; i < subs.Count; i++)
        {
            inputCount++;
            var (code, name) = GetSubtitleCode(subs[i].lan);
            args.AddRange(["-i", subs[i].path]);
            meta.AddRange([$"-metadata:s:s:{i}", $"title={name}", $"-metadata:s:s:{i}", $"language={code}"]);
        }

        if (pic.Length != 0)
        {
            meta.AddRange([$"-disposition:v:{(audioOnly ? 0 : 1)}", "attached_pic"]);
        }

        if (chapterFile != null)
        {
            args.AddRange(["-i", chapterFile, "-map_chapters", inputCount.ToString( )]);
        }

        for (var i = 0; i < inputCount; i++)
        {
            args.AddRange(["-map", i.ToString( )]);
        }

        args.AddRange(meta);

        if (!noMetadata)
        {
            args.AddRange(["-metadata", $"title={(episodeId.Length == 0 ? title : episodeId)}"]);
            args.AddRange(["-metadata", $"comment={url}"]);
            if (lang.Length != 0)
            {
                args.AddRange(["-metadata:s:a:0", $"language={lang}"]);
            }

            if (!string.IsNullOrWhiteSpace(desc))
            {
                args.AddRange(["-metadata", $"description={desc}"]);
            }

            if (author.Length != 0)
            {
                args.AddRange(["-metadata", $"artist={author}"]);
            }

            if (episodeId.Length != 0)
            {
                args.AddRange(["-metadata", $"album={title}"]);
            }

            if (pubTime != 0)
            {
                args.AddRange(["-metadata", $"creation_time={DateTimeOffset.FromUnixTimeSeconds(pubTime):yyyy-MM-ddTHH:mm:ss.ffffffZ}"]);
            }
        }

        args.AddRange(["-c:v", "copy", "-c:a", "copy"]);
        if (audioOnly && audioPath.Length == 0)
        {
            args.Add("-vn");
        }

        if (subs.Count != 0)
        {
            args.AddRange(["-c:s", "mov_text"]);
        }
        // fix macOS hev1, see https://discussions.apple.com/thread/253081863?sortBy=rank
        if (tagHvc1)
        {
            args.AddRange(["-tag:v:0", "hvc1"]);
        }
        // -strict -2：允许实验性编码器/封装（如 mp4 容器内 hev1/hvc1 之外的实验性流）
        args.AddRange(["-movflags", "faststart", "-strict", "-2", "-f", "mp4", "--", outPath]);
        return args;
    }

    public static async Task<int> MuxAV(MuxRequest req, CancellationToken ct = default)
    {
        var videoPath = req.VideoPath;
        var audioPath = req.AudioPath;
        if (req.AudioOnly && audioPath.Length != 0)
        {
            videoPath = "";
        }

        if (req.VideoOnly)
        {
            audioPath = "";
        }

        // 把音视频独占修正回写进副本，下游 Build* 只读 req 上的路径
        req = req with { VideoPath = videoPath, AudioPath = audioPath };

        var url = $"{BiliApi.VideoPage}/{req.Bvid}/";
        var validSubs = req.Subs?.Where(s => File.Exists(s.path) && File.ReadAllText(s.path).Length != 0).ToList( ) ?? [];

        var outDir = Path.GetDirectoryName(req.OutPath);
        if (!string.IsNullOrEmpty(outDir))
        {
            Directory.CreateDirectory(outDir);
        }

        string? chapterFile = null;
        if (req.Points is { Count: > 0 } points)
        {
            chapterFile = Path.Combine(Path.GetDirectoryName(videoPath.Length == 0 ? audioPath : videoPath)!, "chapters");
            File.WriteAllText(chapterFile, req.UseMp4box ? ChapterMeta.GetMp4boxMetaString(points) : ChapterMeta.GetFFmpegMetaString(points));
        }

        return req.UseMp4box
            ? await RunExe(req.Tools.Mp4box, BuildMp4boxArgs(req, validSubs, chapterFile, Config.DebugLog), ct)
            : await RunExe(req.Tools.Ffmpeg, BuildFFmpegArgs(req, validSubs, chapterFile, req.IsHevc && RuntimeInformation.IsOSPlatform(OSPlatform.OSX), Config.DebugLog), ct);
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
                await RunExe(tools.Ffmpeg, ["-loglevel", "warning", "-y", "-i", file, "-map", "0", "-c", "copy", "-f", "mpegts", "-bsf:v", "h264_mp4toannexb", tmpFile], ct);
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
