using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;

using BBDown.Core.Download;

using static BBDown.Core.Util.SubUtil;

namespace BBDown.Core.Mux;

/// <summary>
/// 混流参数构造（纯函数）。所有 Build* 都只读 <see cref="MuxRequest"/> 上的字段，
/// 不触碰文件系统也不启动进程，便于单测；执行侧在 <see cref="Muxer"/>。
/// </summary>
public static class MuxArgs
{
    internal static string BuildMp4boxTagFile(MuxRequest req)
    {
        List<string> lines = ["tool="];
        if (req.Pic.Length != 0)
        {
            lines.Add($"cover={req.Pic}");
        }

        if (req.EpisodeId.Length != 0)
        {
            lines.Add($"album={req.Title}");
            lines.Add($"title={req.EpisodeId}");
        }
        else
        {
            lines.Add($"title={req.Title}");
        }

        lines.Add($"sdesc={req.Desc}");
        lines.Add($"comment={BiliApi.VideoPage}/{req.Bvid}/");
        lines.Add($"artist={req.Author}");
        if (req.TotalTracks > 1)
        {
            lines.Add($"tracknum={req.TrackNumber}/{req.TotalTracks}");
        }

        // 值里的换行天然成为续行，无需任何转义
        return string.Join('\n', lines.Select(line => line.Replace("\r\n", "\n"))) + "\n";
    }

    internal static List<string> BuildMp4boxArgs(MuxRequest req, string tagFile, string? chapterFile, bool debugLog)
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
            args.AddRange(["-add", $"{req.VideoPath}#trackID=1:name="]);
            trackId++;
        }

        if (req.AudioPath.Length != 0)
        {
            args.AddRange(["-add", $"{req.AudioPath}:lang={(req.Lang.Length == 0 ? "und" : req.Lang)}"]);
            trackId++;
        }

        // 配音/背景音轨与 ffmpeg 分支同序编入：track 编号在视频、主音频之后，字幕之前
        foreach (var audio in req.AudioMaterial)
        {
            trackId++;
            args.AddRange(["-add", $"{audio.Path}:lang=und"]);
            var name = string.IsNullOrWhiteSpace(audio.Title) ? audio.PersonName : audio.Title;
            // -udta 的 value 位于 '=' 之后的末段，冒号不再承载结构，无需转义
            if (!string.IsNullOrWhiteSpace(name))
            {
                args.AddRange(["-udta", $"{trackId}:type=name:str={name}"]);
            }

            if (!string.IsNullOrWhiteSpace(audio.PersonName) && audio.PersonName != name)
            {
                args.AddRange(["-udta", $"{trackId}:type=artist:str={audio.PersonName}"]);
            }
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

        args.AddRange(["-itags", tagFile]);

        args.AddRange(["-new", "--", req.OutPath]);
        return args;
    }

    internal static List<string> BuildFFmpegArgs(MuxRequest req, string? chapterFile, bool debugLog)
    {
        var subs = req.Subs ?? [];
        var mkv = req.Mux == MuxMode.Mkv;
        // hvc1 是 mp4 的 codec tag，matroska 用 codec id 表达，只有 mp4 容器需要
        var tagHvc1 = req.IsHevc && RuntimeInformation.IsOSPlatform(OSPlatform.OSX) && !mkv;
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
                // 该串由 ffmpeg 的 av_parse_time 按 ISO-8601 解析，':' 必须是字面字符，
                // 而插值格式化里的 ':' 在部分区域设置下会被替换成当地时间分隔符
                var creationTime = DateTimeOffset.FromUnixTimeSeconds(req.PubTime).ToString("yyyy-MM-ddTHH:mm:ss.ffffffZ", CultureInfo.InvariantCulture);
                args.AddRange(["-metadata", $"creation_time={creationTime}"]);
            }
        }

        args.AddRange(["-c:v", "copy", "-c:a", "copy"]);
        if (!req.Content.Has(DownloadContent.Video) && req.AudioPath.Length == 0)
        {
            args.Add("-vn");
        }

        if (subs.Count != 0)
        {
            // mp4 无原生文本字幕流需转 mov_text；matroska 原生支持 srt/ass 文本字幕，直接复制
            args.AddRange(["-c:s", mkv ? "copy" : "mov_text"]);
        }
        // fix macOS hev1, see https://discussions.apple.com/thread/253081863?sortBy=rank
        if (tagHvc1)
        {
            args.AddRange(["-tag:v:0", "hvc1"]);
        }
        // -movflags faststart / -strict -2 是 mp4 专属；mkv 模式用 matroska 封装
        args.AddRange(mkv
            ? ["-f", "matroska", "--", req.OutPath]
            : ["-movflags", "faststart", "-strict", "-2", "-f", "mp4", "--", req.OutPath]);
        return args;
    }
}
