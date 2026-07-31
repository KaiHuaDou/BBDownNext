using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

using BBDown.Core;

using static BBDown.Core.Entity.Entity;
using static BBDown.Core.Logger;
using static BBDown.Core.Util.SubUtil;
using static BBDown.Utils;

namespace BBDown;

internal static partial class BBDownMuxer
{
    public static string FFMPEG = "ffmpeg";
    public static string MP4BOX = "mp4box";

    private static int RunExe(string app, string parms)
    {
        using Process p = new( );
        p.StartInfo.FileName = app;
        p.StartInfo.Arguments = parms;
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
        p.WaitForExit( );
        return p.ExitCode;
    }

    private static string EscapeString(string str)
    {
        return string.IsNullOrEmpty(str) ? str : str.Replace("\"", "'").Replace("\\", "\\\\");
    }

    private static int MuxByMp4box(string url, string videoPath, string audioPath, string outPath, string desc, string title, string author, string episodeId, string pic, string lang, List<Subtitle>? subs, bool audioOnly, List<ViewPoint>? points)
    {
        StringBuilder inputArg = new( );
        StringBuilder metaArg = new( );
        var nowId = 0;
        inputArg.Append(" -inter 500 -noprog ");
        if (!string.IsNullOrEmpty(videoPath))
        {
            inputArg.Append($" -add \"{videoPath}#trackID={(audioOnly && audioPath.Length == 0 ? "2" : "1")}:name=\" ");
            nowId++;
        }

        if (!string.IsNullOrEmpty(audioPath))
        {
            inputArg.Append($" -add \"{audioPath}:lang={(lang.Length == 0 ? "und" : lang)}\" ");
            nowId++;
        }

        if (points != null && points.Count != 0)
        {
            var meta = GetMp4boxMetaString(points);
            var metaFile = Path.Combine(Path.GetDirectoryName(string.IsNullOrEmpty(videoPath) ? audioPath : videoPath)!, "chapters");
            File.WriteAllText(metaFile, meta);
            inputArg.Append($" -chap  \"{metaFile}\"  ");
        }

        if (!string.IsNullOrEmpty(pic))
        {
            metaArg.Append($":cover=\"{pic}\"");
        }

        if (!string.IsNullOrEmpty(episodeId))
        {
            metaArg.Append($":album=\"{title}\":title=\"{episodeId}\"");
        }
        else
        {
            metaArg.Append($":title=\"{title}\"");
        }

        metaArg.Append($":sdesc=\"{desc}\"");
        metaArg.Append($":comment=\"{url}\"");
        metaArg.Append($":artist=\"{author}\"");

        if (subs != null)
        {
            for (var i = 0; i < subs.Count; i++)
            {
                if (File.Exists(subs[i].path) && File.ReadAllText(subs[i].path!).Length != 0)
                {
                    nowId++;
                    inputArg.Append($" -add \"{subs[i].path}#trackID=1:name=:hdlr=sbtl:lang={GetSubtitleCode(subs[i].lan).Item1}\" ");
                    inputArg.Append($" -udta {nowId}:type=name:str=\"{GetSubtitleCode(subs[i].lan).Item2}\" ");
                }
            }
        }

        //----分析完毕
        var arguments = (Config.DebugLog ? " -v " : "") + inputArg + (metaArg.Length == 0 ? "" : " -itags tool=" + metaArg) + $" -new -- \"{outPath}\"";
        LogDebug("mp4box命令: {0}", arguments);
        return RunExe(MP4BOX, arguments);
    }

    public static int MuxAV(bool useMp4box, string bvid, string videoPath, string audioPath, List<AudioMaterial> audioMaterial, string outPath, string desc = "", string title = "", string author = "", string episodeId = "", string pic = "", string lang = "", List<Subtitle>? subs = null, bool audioOnly = false, bool videoOnly = false, List<ViewPoint>? points = null, long pubTime = 0, bool simplyMux = false, bool isHevc = false)
    {
        if (audioOnly && audioPath.Length != 0)
        {
            videoPath = "";
        }

        if (videoOnly)
        {
            audioPath = "";
        }

        desc = EscapeString(desc);
        title = EscapeString(title);
        episodeId = EscapeString(episodeId);
        var url = $"https://www.bilibili.com/video/{bvid}/";

        if (useMp4box)
        {
            return MuxByMp4box(url, videoPath, audioPath, outPath, desc, title, author, episodeId, pic, lang, subs, audioOnly, points);
        }

        var outDir = Path.GetDirectoryName(outPath);
        if (!string.IsNullOrEmpty(outDir)) Directory.CreateDirectory(outDir);
        //----分析并生成-i参数
        StringBuilder inputArg = new( );
        StringBuilder metaArg = new( );
        byte inputCount = 0;
        foreach (var path in new[] { videoPath, audioPath })
        {
            if (!string.IsNullOrEmpty(path))
            {
                inputCount++;
                inputArg.Append($"-i \"{path}\" ");
            }
        }

        if (audioMaterial.Count != 0)
        {
            byte audioCount = 0;
            metaArg.Append("-metadata:s:a:0 title=\"原音频\" ");
            foreach (var audio in audioMaterial)
            {
                inputCount++;
                audioCount++;
                inputArg.Append($"-i \"{audio.path}\" ");
                if (!string.IsNullOrWhiteSpace(audio.title)) metaArg.Append($"-metadata:s:a:{audioCount} title=\"{audio.title}\" ");
                if (!string.IsNullOrWhiteSpace(audio.personName)) metaArg.Append($"-metadata:s:a:{audioCount} artist=\"{audio.personName}\" ");
            }
        }

        if (!string.IsNullOrEmpty(pic))
        {
            inputCount++;
            inputArg.Append($"-i \"{pic}\" ");
        }

        if (subs != null)
        {
            for (var i = 0; i < subs.Count; i++)
            {
                if (File.Exists(subs[i].path) && File.ReadAllText(subs[i].path!).Length != 0)
                {
                    inputCount++;
                    inputArg.Append($"-i \"{subs[i].path}\" ");
                    metaArg.Append($"-metadata:s:s:{i} title=\"{GetSubtitleCode(subs[i].lan).Item2}\" -metadata:s:s:{i} language={GetSubtitleCode(subs[i].lan).Item1} ");
                }
            }
        }

        if (!string.IsNullOrEmpty(pic))
        {
            metaArg.Append($"-disposition:v:{(audioOnly ? "0" : "1")} attached_pic ");
        }
        // var inputCount = InputRegex().Matches(inputArg.ToString()).Count;

        if (points != null && points.Count != 0)
        {
            var meta = GetFFmpegMetaString(points);
            var metaFile = Path.Combine(Path.GetDirectoryName(string.IsNullOrEmpty(videoPath) ? audioPath : videoPath)!, "chapters");
            File.WriteAllText(metaFile, meta);
            inputArg.Append($"-i \"{metaFile}\" -map_chapters {inputCount} ");
        }

        inputArg.Append(string.Concat(Enumerable.Range(0, inputCount).Select(i => $"-map {i} ")));

        //----分析完毕
        var argsBuilder = new StringBuilder( );
        argsBuilder.Append($"-loglevel {(Config.DebugLog ? "verbose" : "warning")} -y ");
        argsBuilder.Append(inputArg);
        argsBuilder.Append(metaArg);
        if (!simplyMux)
        {
            argsBuilder.Append($"-metadata title=\"{(episodeId.Length == 0 ? title : episodeId)}\" ");
            argsBuilder.Append($"-metadata comment=\"{url}\" ");
            if (lang.Length != 0) argsBuilder.Append($"-metadata:s:a:0 language={lang} ");
            if (!string.IsNullOrWhiteSpace(desc)) argsBuilder.Append($"-metadata description=\"{desc}\" ");
            if (!string.IsNullOrEmpty(author)) argsBuilder.Append($"-metadata artist=\"{author}\" ");
            if (episodeId.Length != 0) argsBuilder.Append($"-metadata album=\"{title}\" ");
            if (pubTime != 0) argsBuilder.Append($"-metadata creation_time=\"{DateTimeOffset.FromUnixTimeSeconds(pubTime).ToString("yyyy-MM-ddTHH:mm:ss.ffffffZ")}\" ");
        }

        argsBuilder.Append("-c:v copy -c:a copy ");
        if (audioOnly && audioPath.Length == 0) argsBuilder.Append("-vn ");
        if (subs != null) argsBuilder.Append("-c:s mov_text ");
        // fix macOS hev1, see https://discussions.apple.com/thread/253081863?sortBy=rank
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) && isHevc) argsBuilder.Append("-tag:v:0 hvc1 ");
        argsBuilder.Append($"-movflags faststart -strict unofficial -strict -2 -f mp4 -- \"{outPath}\"");

        var arguments = argsBuilder.ToString( );

        LogDebug("ffmpeg命令: {0}", arguments);
        return RunExe(FFMPEG, arguments);
    }

    public static void MergeFLV(string[] files, string outPath)
    {
        if (files.Length == 1)
        {
            File.Move(files[0], outPath);
        }
        else
        {
            foreach (var file in files)
            {
                var tmpFile = Path.Combine(Path.GetDirectoryName(file)!, Path.GetFileNameWithoutExtension(file) + ".ts");
                var arguments = $"-loglevel warning -y -i \"{file}\" -map 0 -c copy -f mpegts -bsf:v h264_mp4toannexb \"{tmpFile}\"";
                LogDebug("ffmpeg命令: {0}", arguments);
                RunExe(FFMPEG, arguments);
                File.Delete(file);
            }

            var f = GetFiles(Path.GetDirectoryName(files[0])!, ".ts");
            CombineMultipleFilesIntoSingleFile(f, outPath);
            foreach (var s in f)
            {
                File.Delete(s);
            }
        }
    }
}