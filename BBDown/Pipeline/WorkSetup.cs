using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

using BBDown.Core;
using BBDown.Core.Util;
using BBDown.Download;
using BBDown.Mux;

using static BBDown.Core.Logger;
using static BBDown.Util.Utils;

namespace BBDown.Pipeline;

internal static class WorkSetup
{
    public static WorkContext Build(DownloadOptions myOption)
    {
        Config.SetDebugLog(myOption.Debug);

        // 处理冲突选项
        HandleConflictingOptions(myOption);

        // 寻找并设置所需的二进制文件路径
        FindBinaries(myOption);

        // 确定本次任务的工作目录（不修改进程全局 CurrentDirectory，serve 模式下多任务会互相踩踏）
        var workDir = ResolveWorkDir(myOption);

        // 解析优先级
        var (encodingPriority, firstEncoding) = ParseEncodingPriority(myOption);
        var dfnPriority = ParseDfnPriority(myOption);

        // 优先使用用户设置的 UA
        if (!string.IsNullOrEmpty(myOption.UserAgent))
        {
            HTTPUtil.SetUserAgent(myOption.UserAgent);
        }

        var downloadDanmaku = myOption.DownloadDanmaku || myOption.DanmakuOnly;
        var downloadDanmakuFormats = ParseDownloadDanmakuFormats(myOption);

        var commentCount = Math.Max(0, myOption.CommentCount);
        var commentSortHot = !string.Equals(myOption.CommentSort, "time", StringComparison.OrdinalIgnoreCase);
        var commentFormats = ParseCommentFormats(myOption);

        var input = myOption.Url;
        var lang = myOption.Lang;
        var delay = int.TryParse(myOption.DelayPerPage, out var delayValue) ? delayValue : 0;

        LogDebug("AppDirectory: {0}", AppEnv.AppDir);
        LogDebug("运行参数：{0}", JsonSerializer.Serialize(myOption.WithSecretsRedacted( ), DownloadOptionsJsonContext.Default.DownloadOptions));
        return new WorkContext(
            EncodingPriority: encodingPriority,
            DfnPriority: dfnPriority,
            FirstEncoding: firstEncoding,
            EncodingFirst: myOption.EncodingFirst,
            DownloadDanmaku: downloadDanmaku,
            DownloadDanmakuFormats: downloadDanmakuFormats,
            CommentCount: commentCount,
            CommentSortHot: commentSortHot,
            CommentFormats: commentFormats,
            FullComment: myOption.FullComment,
            Input: input,
            SavePathFormat: "",
            Lang: lang,
            Delay: delay,
            FetchedAid: "",
            VInfo: null,
            ApiType: "",
            Cfg: AppConfig.Empty,
            WorkDir: workDir);
    }

    /// <summary>
    /// 解析用户指定的编码优先级，返回优先级表与首个编码
    /// </summary>
    internal static (Dictionary<string, byte> EncodingPriority, string FirstEncoding) ParseEncodingPriority(DownloadOptions myOption)
    {
        var encodingPriority = new Dictionary<string, byte>( );
        var firstEncoding = "";
        if (myOption.EncodingPriority != null)
        {
            var encodingPriorityTemp = myOption.EncodingPriority
                .ToUpper( )
                .Replace('，', ',')
                .Replace("-", string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(s => !string.IsNullOrEmpty(s)).ToList( );
            byte index = 0;
            firstEncoding = encodingPriorityTemp.FirstOrDefault( ) ?? "";
            foreach (var encoding in encodingPriorityTemp)
            {
                if (encodingPriority.ContainsKey(encoding))
                {
                    continue;
                }

                encodingPriority[encoding] = index;
                index++;
            }
        }

        return (encodingPriority, firstEncoding);
    }

    internal static DanmakuFormat[] ParseDownloadDanmakuFormats(DownloadOptions myOption)
    {
        if (string.IsNullOrEmpty(myOption.DownloadDanmakuFormats))
        {
            return DanmakuFormatInfo.DefaultFormats;
        }

        var formats = myOption.DownloadDanmakuFormats.Replace("，", ",").ToLower( ).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (formats.Any(format => !DanmakuFormatInfo.AllFormatNames.Contains(format)))
        {
            LogError($"包含不支持的下载弹幕格式：{myOption.DownloadDanmakuFormats}。");
            return DanmakuFormatInfo.DefaultFormats;
        }

        return [.. formats.Select(DanmakuFormatInfo.FromFormatName)];
    }

    internal static CommentFormat[] ParseCommentFormats(DownloadOptions myOption)
    {
        if (string.IsNullOrEmpty(myOption.CommentFormats))
        {
            return CommentFormatInfo.DefaultFormats;
        }

        var formats = myOption.CommentFormats.Replace("，", ",").ToLower( ).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (formats.Any(format => !CommentFormatInfo.AllFormatNames.Contains(format)))
        {
            LogError($"包含不支持的评论导出格式：{myOption.CommentFormats}。");
            return CommentFormatInfo.DefaultFormats;
        }

        // 去重：同一格式写两遍只会让后一次覆盖前一次，白跑一趟 IO
        return [.. formats.Select(CommentFormatInfo.FromFormatName).Distinct( )];
    }

    /// <summary>
    /// 解析用户输入的清晰度规格优先级
    /// </summary>
    internal static Dictionary<string, int> ParseDfnPriority(DownloadOptions myOption)
    {
        var dfnPriority = new Dictionary<string, int>( );
        if (myOption.DfnPriority != null)
        {
            var dfnPriorityTemp = myOption.DfnPriority.Replace("，", ",").Split(',').Select(s => s.ToUpper( ).Trim( )).Where(s => !string.IsNullOrEmpty(s));
            var index = 0;
            foreach (var dfn in dfnPriorityTemp)
            {
                if (dfnPriority.ContainsKey(dfn)) { continue; }

                dfnPriority[dfn] = index;
                index++;
            }
        }

        return dfnPriority;
    }

    /// <summary>
    /// 寻找并设置所需的二进制文件
    /// </summary>
    internal static void FindBinaries(DownloadOptions myOption)
    {
        if (!string.IsNullOrEmpty(myOption.FFmpegPath) && File.Exists(myOption.FFmpegPath))
        {
            Muxer.ffmpeg = myOption.FFmpegPath;
        }

        if (!string.IsNullOrEmpty(myOption.Mp4boxPath) && File.Exists(myOption.Mp4boxPath))
        {
            Muxer.mp4box = myOption.Mp4boxPath;
        }

        if (!string.IsNullOrEmpty(myOption.Aria2cPath) && File.Exists(myOption.Aria2cPath))
        {
            BBDownAria2c.aria2c = myOption.Aria2cPath;
        }
        // 寻找 FFmpeg 或 mp4box
        if (!myOption.SkipMux)
        {
            // FFmpeg 与 mp4box 都探测，以便下载时按需选择 (杜比视界可能临时改用 mp4box)
            if (string.IsNullOrEmpty(Muxer.ffmpeg) || !File.Exists(Muxer.ffmpeg))
            {
                var binPath = FindExecutable("ffmpeg");
                if (!string.IsNullOrEmpty(binPath))
                {
                    Muxer.ffmpeg = binPath;
                }
            }

            if (string.IsNullOrEmpty(Muxer.mp4box) || !File.Exists(Muxer.mp4box))
            {
                var binPath = FindExecutable("mp4box", "MP4Box", "MP4box");
                if (!string.IsNullOrEmpty(binPath))
                {
                    Muxer.mp4box = binPath;
                }
            }

            if (string.IsNullOrEmpty(Muxer.ffmpeg) || !File.Exists(Muxer.ffmpeg))
            {
                throw new InvalidOperationException("找不到可执行的 ffmpeg 文件");
            }
        }

        // 寻找 aria2c
        if (myOption.UseAria2c)
        {
            if (string.IsNullOrEmpty(BBDownAria2c.aria2c) || !File.Exists(BBDownAria2c.aria2c))
            {
                var binPath = FindExecutable("aria2c");
                if (string.IsNullOrEmpty(binPath))
                {
                    throw new InvalidOperationException("找不到可执行的 aria2c 文件");
                }

                BBDownAria2c.aria2c = binPath;
            }
        }
    }

    /// <summary>
    /// 处理有冲突的选项
    /// </summary>
    internal static void HandleConflictingOptions(DownloadOptions myOption)
    {
        // 手动选择时不能隐藏流
        if (myOption.Interactive)
        {
            myOption.HideStreams = false;
        }
        // audioOnly 和 videoOnly 同时开启则全部忽视
        if (myOption.AudioOnly && myOption.VideoOnly)
        {
            myOption.AudioOnly = false;
            myOption.VideoOnly = false;
        }

        if (myOption.NoSub)
        {
            myOption.SubOnly = false;
        }
    }

    /// <summary>
    /// 解析用户输入的自定义工作目录，返回绝对路径。未指定时回落到进程当前目录。
    /// </summary>
    internal static string ResolveWorkDir(DownloadOptions myOption)
    {
        if (string.IsNullOrEmpty(myOption.WorkDir))
        {
            return Environment.CurrentDirectory;
        }

        myOption.WorkDir = Environment.ExpandEnvironmentVariables(myOption.WorkDir);
        var dir = Path.GetFullPath(myOption.WorkDir);
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        LogDebug("本次任务工作目录：{0}", dir);
        return dir;
    }
}
