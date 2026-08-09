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

using static BBDown.Core.Logger;
using static BBDown.Core.Util.SubUtil;
using static BBDown.Util.Utils;
using BBDown.Core.Entity;

namespace BBDown.Mux;

/// <summary>
/// 一次混流的不可变入参集合，由 <see cref="MuxFinish"/> 组装后交给 <see cref="Muxer.MuxAV"/>。
/// 调用方按名填字段，下游 <c>Build*</c> 直接读 <c>req</c> 上的路径与元数据，不再数位置。
/// </summary>
internal sealed record MuxRequest(
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
    // 多P（总集数大于 1）时填当前分P序号与该视频总集数，写入 track / track_total 元数据
    int TrackNumber,
    int TotalTracks);

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

    internal static List<string> BuildMp4boxArgs(MuxRequest req, string? chapterFile, bool debugLog)
    {
        var subs = req.Subs ?? [];
        List<string> args = [];
        if (debugLog)
        {
            args.Add("-v");
        }

        args.AddRange(["-inter", "500", "-noprog"]);

        var trackId = 0;
        if (req.VideoPath.Length != 0)
        {
            args.AddRange(["-add", $"{req.VideoPath}#trackID={(!req.Content.Has(DownloadContent.Video) && req.AudioPath.Length == 0 ? 2 : 1)}:name="]);
            trackId++;
        }

        if (req.AudioPath.Length != 0)
        {
            args.AddRange(["-add", $"{req.AudioPath}:lang={(req.Lang.Length == 0 ? "und" : req.Lang)}"]);
            trackId++;
        }

        if (chapterFile != null)
        {
            args.AddRange(["-chap", chapterFile]);
        }

        foreach (var sub in subs)
        {
            trackId++;
            var (code, name) = GetSubtitleCode(sub.Lan);
            args.AddRange(["-add", $"{sub.Path}#trackID=1:name=:hdlr=sbtl:lang={code}"]);
            args.AddRange(["-udta", $"{trackId}:type=name:str={name}"]);
        }

        var tags = new StringBuilder("tool=");
        if (req.Pic.Length != 0)
        {
            tags.Append($":cover={req.Pic}");
        }

        if (req.EpisodeId.Length != 0)
        {
            tags.Append($":album={req.Title}:title={req.EpisodeId}");
        }
        else
        {
            tags.Append($":title={req.Title}");
        }

        tags.Append($":sdesc={req.Desc}");
        tags.Append($":comment={BiliApi.VideoPage}/{req.Bvid}/");
        tags.Append($":artist={req.Author}");
        if (req.TotalTracks > 1)
        {
            tags.Append($":tracknum={req.TrackNumber}/{req.TotalTracks}");
        }

        args.AddRange(["-itags", tags.ToString( )]);

        args.AddRange(["-new", "--", req.OutPath]);
        return args;
    }

    internal static List<string> BuildFFmpegArgs(MuxRequest req, string? chapterFile, bool debugLog)
    {
        var subs = req.Subs ?? [];
        var tagHvc1 = req.IsHevc && RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
        List<string> args = ["-loglevel", debugLog ? "verbose" : "warning", "-y"];
        List<string> meta = [];
        var inputCount = 0;

        foreach (var path in new[] { req.VideoPath, req.AudioPath })
        {
            if (path.Length == 0)
            {
                continue;
            }

            inputCount++;
            args.AddRange(["-i", path]);
        }

        if (req.AudioMaterial.Count != 0)
        {
            var audioIndex = 0;
            meta.AddRange(["-metadata:s:a:0", "title=原音频"]);
            foreach (var audio in req.AudioMaterial)
            {
                inputCount++;
                audioIndex++;
                args.AddRange(["-i", audio.Path]);
                if (!string.IsNullOrWhiteSpace(audio.Title))
                {
                    meta.AddRange([$"-metadata:s:a:{audioIndex}", $"title={audio.Title}"]);
                }

                if (!string.IsNullOrWhiteSpace(audio.PersonName))
                {
                    meta.AddRange([$"-metadata:s:a:{audioIndex}", $"artist={audio.PersonName}"]);
                }
            }
        }

        if (req.Pic.Length != 0)
        {
            inputCount++;
            args.AddRange(["-i", req.Pic]);
        }

        for (var i = 0; i < subs.Count; i++)
        {
            inputCount++;
            var (code, name) = GetSubtitleCode(subs[i].Lan);
            args.AddRange(["-i", subs[i].Path]);
            meta.AddRange([$"-metadata:s:s:{i}", $"title={name}", $"-metadata:s:s:{i}", $"language={code}"]);
        }

        if (req.Pic.Length != 0)
        {
            meta.AddRange([$"-disposition:v:{(!req.Content.Has(DownloadContent.Video) ? 0 : 1)}", "attached_pic"]);
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

        if (req.Content.Has(DownloadContent.MuxMetadata))
        {
            args.AddRange(["-metadata", $"title={(req.EpisodeId.Length == 0 ? req.Title : req.EpisodeId)}"]);
            args.AddRange(["-metadata", $"comment={BiliApi.VideoPage}/{req.Bvid}/"]);
            if (req.Lang.Length != 0)
            {
                args.AddRange(["-metadata:s:a:0", $"language={req.Lang}"]);
            }

            if (!string.IsNullOrWhiteSpace(req.Desc))
            {
                args.AddRange(["-metadata", $"synopsis={req.Desc}"]);
            }

            if (req.Author.Length != 0)
            {
                args.AddRange(["-metadata", $"artist={req.Author}"]);
            }

            if (req.EpisodeId.Length != 0)
            {
                args.AddRange(["-metadata", $"album={req.Title}"]);
            }

            if (req.TotalTracks > 1)
            {
                args.AddRange(["-metadata", $"track={req.TrackNumber}"]);
                args.AddRange(["-metadata", $"track_total={req.TotalTracks}"]);
            }

            if (req.PubTime != 0)
            {
                args.AddRange(["-metadata", $"creation_time={DateTimeOffset.FromUnixTimeSeconds(req.PubTime):yyyy-MM-ddTHH:mm:ss.ffffffZ}"]);
            }
        }

        args.AddRange(["-c:v", "copy", "-c:a", "copy"]);
        if (!req.Content.Has(DownloadContent.Video) && req.AudioPath.Length == 0)
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
        args.AddRange(["-movflags", "faststart", "-strict", "-2", "-f", "mp4", "--", req.OutPath]);
        return args;
    }

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

        // MuxMode.None 由 MuxFinish 提前中止，不会走到这里；Mkv 已在 WorkSetup 回退为 Mpeg4
        return req.Mux == MuxMode.Mp4box
            ? await RunExe(req.Tools.Mp4box, BuildMp4boxArgs(req, chapterFile, Config.DebugLog), ct)
            : await RunExe(req.Tools.Ffmpeg, BuildFFmpegArgs(req, chapterFile, Config.DebugLog), ct);
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