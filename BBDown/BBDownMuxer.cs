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
using static BBDown.Utils;

namespace BBDown;

internal static class BBDownMuxer
{
    public static string FFMPEG = "ffmpeg";
    public static string MP4BOX = "mp4box";

    private static async Task<int> RunExe(string app, List<string> args, CancellationToken ct = default)
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
        p.ErrorDataReceived += delegate (object sendProcess, DataReceivedEventArgs output)
        {
            if (!string.IsNullOrWhiteSpace(output.Data))
            {
                Log(output.Data);
            }
        };
        p.Start( );
        p.BeginErrorReadLine( );
        // 取消时杀掉子进程, 避免 ffmpeg 在 WaitForExitAsync 已取消后仍挂起
        using var _ = ct.Register(() => { try { p.Kill( ); } catch { } });
        await p.WaitForExitAsync(ct);
        return p.ExitCode;
    }

    private static string FormatArgs(List<string> args)
        => string.Join(' ', args.Select(a => a.Length == 0 || a.Contains(' ') ? $"\"{a}\"" : a));

    internal static List<string> BuildMp4boxArgs(string url, string videoPath, string audioPath, string outPath, string desc, string title, string author, string episodeId, string pic, string lang, List<Subtitle> subs, bool audioOnly, string? chapterFile, bool debugLog)
    {
        List<string> args = [];
        if (debugLog) args.Add("-v");
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

        if (chapterFile != null) args.AddRange(["-chap", chapterFile]);

        foreach (var sub in subs)
        {
            trackId++;
            var (code, name) = GetSubtitleCode(sub.lan);
            args.AddRange(["-add", $"{sub.path}#trackID=1:name=:hdlr=sbtl:lang={code}"]);
            args.AddRange(["-udta", $"{trackId}:type=name:str={name}"]);
        }

        var tags = new StringBuilder("tool=");
        if (pic.Length != 0) tags.Append($":cover={pic}");
        if (episodeId.Length != 0) tags.Append($":album={title}:title={episodeId}");
        else tags.Append($":title={title}");
        tags.Append($":sdesc={desc}");
        tags.Append($":comment={url}");
        tags.Append($":artist={author}");
        args.AddRange(["-itags", tags.ToString( )]);

        args.AddRange(["-new", "--", outPath]);
        return args;
    }

    internal static List<string> BuildFFmpegArgs(string url, string videoPath, string audioPath, List<AudioMaterial> audioMaterial, string outPath, string desc, string title, string author, string episodeId, string pic, string lang, List<Subtitle> subs, bool audioOnly, string? chapterFile, long pubTime, bool noMetadata, bool tagHvc1, bool debugLog)
    {
        List<string> args = ["-loglevel", debugLog ? "verbose" : "warning", "-y"];
        List<string> meta = [];
        var inputCount = 0;

        foreach (var path in new[] { videoPath, audioPath })
        {
            if (path.Length == 0) continue;
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
                if (!string.IsNullOrWhiteSpace(audio.title)) meta.AddRange([$"-metadata:s:a:{audioIndex}", $"title={audio.title}"]);
                if (!string.IsNullOrWhiteSpace(audio.personName)) meta.AddRange([$"-metadata:s:a:{audioIndex}", $"artist={audio.personName}"]);
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

        if (pic.Length != 0) meta.AddRange([$"-disposition:v:{(audioOnly ? 0 : 1)}", "attached_pic"]);

        if (chapterFile != null) args.AddRange(["-i", chapterFile, "-map_chapters", inputCount.ToString( )]);

        for (var i = 0; i < inputCount; i++)
        {
            args.AddRange(["-map", i.ToString( )]);
        }

        args.AddRange(meta);

        if (!noMetadata)
        {
            args.AddRange(["-metadata", $"title={(episodeId.Length == 0 ? title : episodeId)}"]);
            args.AddRange(["-metadata", $"comment={url}"]);
            if (lang.Length != 0) args.AddRange(["-metadata:s:a:0", $"language={lang}"]);
            if (!string.IsNullOrWhiteSpace(desc)) args.AddRange(["-metadata", $"description={desc}"]);
            if (author.Length != 0) args.AddRange(["-metadata", $"artist={author}"]);
            if (episodeId.Length != 0) args.AddRange(["-metadata", $"album={title}"]);
            if (pubTime != 0) args.AddRange(["-metadata", $"creation_time={DateTimeOffset.FromUnixTimeSeconds(pubTime):yyyy-MM-ddTHH:mm:ss.ffffffZ}"]);
        }

        args.AddRange(["-c:v", "copy", "-c:a", "copy"]);
        if (audioOnly && audioPath.Length == 0) args.Add("-vn");
        if (subs.Count != 0) args.AddRange(["-c:s", "mov_text"]);
        // fix macOS hev1, see https://discussions.apple.com/thread/253081863?sortBy=rank
        if (tagHvc1) args.AddRange(["-tag:v:0", "hvc1"]);
        args.AddRange(["-movflags", "faststart", "-strict", "unofficial", "-strict", "-2", "-f", "mp4", "--", outPath]);
        return args;
    }

    public static async Task<int> MuxAV(bool useMp4box, string bvid, string videoPath, string audioPath, List<AudioMaterial> audioMaterial, string outPath, string desc = "", string title = "", string author = "", string episodeId = "", string pic = "", string lang = "", List<Subtitle>? subs = null, bool audioOnly = false, bool videoOnly = false, List<ViewPoint>? points = null, long pubTime = 0, bool noMetadata = false, bool isHevc = false, CancellationToken ct = default)
    {
        if (audioOnly && audioPath.Length != 0)
        {
            videoPath = "";
        }

        if (videoOnly)
        {
            audioPath = "";
        }

        var url = $"{BiliApi.VideoPage}/{bvid}/";
        var validSubs = subs?.Where(s => File.Exists(s.path) && File.ReadAllText(s.path).Length != 0).ToList( ) ?? [];

        var outDir = Path.GetDirectoryName(outPath);
        if (!string.IsNullOrEmpty(outDir)) Directory.CreateDirectory(outDir);

        string? chapterFile = null;
        if (points != null && points.Count != 0)
        {
            chapterFile = Path.Combine(Path.GetDirectoryName(videoPath.Length == 0 ? audioPath : videoPath)!, "chapters");
            File.WriteAllText(chapterFile, useMp4box ? GetMp4boxMetaString(points) : GetFFmpegMetaString(points));
        }

        return useMp4box
            ? await RunExe(MP4BOX, BuildMp4boxArgs(url, videoPath, audioPath, outPath, desc, title, author, episodeId, pic, lang, validSubs, audioOnly, chapterFile, Config.DebugLog), ct)
            : await RunExe(FFMPEG, BuildFFmpegArgs(url, videoPath, audioPath, audioMaterial, outPath, desc, title, author, episodeId, pic, lang, validSubs, audioOnly, chapterFile, pubTime, noMetadata, isHevc && RuntimeInformation.IsOSPlatform(OSPlatform.OSX), Config.DebugLog), ct);
    }

    public static async Task MergeFLV(string[] files, string outPath, CancellationToken ct = default)
    {
        if (files.Length == 1)
        {
            File.Move(files[0], outPath);
            return;
        }

        foreach (var file in files)
        {
            var tmpFile = Path.Combine(Path.GetDirectoryName(file)!, Path.GetFileNameWithoutExtension(file) + ".ts");
            await RunExe(FFMPEG, ["-loglevel", "warning", "-y", "-i", file, "-map", "0", "-c", "copy", "-f", "mpegts", "-bsf:v", "h264_mp4toannexb", tmpFile], ct);
            File.Delete(file);
        }

        var tsFiles = GetFiles(Path.GetDirectoryName(files[0])!, ".ts");
        CombineMultipleFilesIntoSingleFile(tsFiles, outPath);
        foreach (var s in tsFiles)
        {
            File.Delete(s);
        }
    }
}
