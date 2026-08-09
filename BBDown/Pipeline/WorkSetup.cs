using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

using BBDown.Drm;

using static BBDown.Core.Logger;
using static BBDown.Util.Utils;

namespace BBDown.Pipeline;

/// <summary>
/// 一次下载任务在「启动即可确定」的运行参数快照（不可变）。由 <see cref="Build"/> 算清一次，
/// 不含任何「跑中才得到」的值（视频信息、aid、api 类型、保存路径模板）——那些由
/// <see cref="VideoInfo.FetchAsync"/> / <see cref="PageQueue.RunAsync"/> 作为返回值 / 局部变量回传，
/// 最终在 <see cref="PageQueue.RunAsync"/> 里一次性组装进 <see cref="WorkContext"/>，不再有空占位 + with 补全。
/// </summary>
internal sealed record RunConfig(
    Dictionary<string, byte> EncodingPriority,
    Dictionary<string, int> DfnPriority,
    string FirstEncoding,
    bool EncodingFirst,
    DownloadContent Content,
    MuxMode Mux,
    DanmakuFormat[] DownloadDanmakuFormats,
    int CommentCount,
    bool CommentSortHot,
    CommentFormat[] CommentFormats,
    string Input,
    string Lang,
    int Delay,
    ToolPaths Tools,
    // --drm-key 条目在任务启动时解析一次，全任务共享；解析告警只打印一遍
    DrmKeySource DrmKeys,
    string WorkDir);

internal static class WorkSetup
{
    public static RunConfig Build(DownloadRequest myOption)
    {
        // 解析外部工具路径（不可变快照，作为 ToolPaths 向下透传，不写进程级静态）
        var tools = ResolveToolPaths(myOption);

        // 确定本次任务的工作目录（不修改进程全局 CurrentDirectory，serve 模式下多任务会互相踩踏）
        var workDir = ResolveWorkDir(myOption);

        // 解析优先级
        var (encodingPriority, firstEncoding) = ParseEncodingPriority(myOption);
        var dfnPriority = ParseDfnPriority(myOption);

        var downloadDanmakuFormats = ParseDownloadDanmakuFormats(myOption);

        var commentCount = Math.Max(0, myOption.CommentCount);
        var commentSortHot = !string.Equals(myOption.CommentSort, "time", StringComparison.OrdinalIgnoreCase);
        var commentFormats = ParseCommentFormats(myOption);

        var lang = myOption.Lang;
        var delay = int.TryParse(myOption.DelayPerPage, out var delayValue) ? delayValue : 0;

        LogDebug("AppDirectory: {0}", AppEnv.AppDir);
        LogDebug("运行参数：{0}", JsonSerializer.Serialize(myOption.WithSecretsRedacted( ), DownloadRequestJsonContext.Default.DownloadRequest));
        return new RunConfig(
            EncodingPriority: encodingPriority,
            DfnPriority: dfnPriority,
            FirstEncoding: firstEncoding,
            EncodingFirst: myOption.EncodingFirst,
            Content: myOption.Content,
            Mux: myOption.Mux,
            DownloadDanmakuFormats: downloadDanmakuFormats,
            CommentCount: commentCount,
            CommentSortHot: commentSortHot,
            CommentFormats: commentFormats,
            Input: myOption.Url,
            Lang: lang,
            Delay: delay,
            Tools: tools,
            DrmKeys: new DrmKeySource(myOption.DrmKeys),
            WorkDir: workDir);
    }

    /// <summary>
    /// 解析用户指定的编码优先级，返回优先级表与首个编码
    /// </summary>
    internal static (Dictionary<string, byte> EncodingPriority, string FirstEncoding) ParseEncodingPriority(DownloadRequest myOption)
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

    internal static DanmakuFormat[] ParseDownloadDanmakuFormats(DownloadRequest myOption)
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

    internal static CommentFormat[] ParseCommentFormats(DownloadRequest myOption)
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
    internal static Dictionary<string, int> ParseDfnPriority(DownloadRequest myOption)
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
    /// 解析外部工具路径，返回不可变快照。原 FindBinaries 会把这些路径写进进程级可变静态字段
    /// （Muxer.ffmpeg/mp4box、BBDownAria2c.aria2c），在 serve 并发任务下互相踩踏；改为纯函数返回快照，
    /// 由调用方作为 ToolPaths 参数向下透传。
    /// </summary>
    internal static ToolPaths ResolveToolPaths(DownloadRequest myOption)
    {
        // 显式路径优先，否则在 PATH/AppDir 中探测，再不行回落到命令名（运行期由进程查找，仍可能命中 PATH）
        var ffmpeg = !string.IsNullOrEmpty(myOption.FFmpegPath) && File.Exists(myOption.FFmpegPath)
            ? myOption.FFmpegPath
            : FindExecutable("ffmpeg") ?? "ffmpeg";

        var mp4box = !string.IsNullOrEmpty(myOption.Mp4boxPath) && File.Exists(myOption.Mp4boxPath)
            ? myOption.Mp4boxPath
            : FindExecutable("mp4box", "MP4Box", "MP4box") ?? "mp4box";

        // 不混流时不强制要求 ffmpeg 存在
        if (myOption.Mux != MuxMode.None && (string.IsNullOrEmpty(ffmpeg) || !File.Exists(ffmpeg)))
        {
            throw new InvalidOperationException("找不到可执行的 ffmpeg 文件");
        }

        string? aria2c = null;
        if (myOption.UseAria2c)
        {
            aria2c = !string.IsNullOrEmpty(myOption.Aria2cPath) && File.Exists(myOption.Aria2cPath)
                ? myOption.Aria2cPath
                : FindExecutable("aria2c");
            if (string.IsNullOrEmpty(aria2c))
            {
                throw new InvalidOperationException("找不到可执行的 aria2c 文件");
            }
        }

        return new ToolPaths(ffmpeg, mp4box, aria2c);
    }

    /// <summary>
    /// 处理有冲突的选项。不原地改写入参（DownloadRequest 不可变），返回修正后的副本（C2）。
    /// 内容字符的冲突（AudioOnly / VideoOnly 互斥等）已由 <see cref="ContentSelector.Resolve"/> 在解析层消解。
    /// </summary>
    internal static DownloadRequest HandleConflictingOptions(DownloadRequest myOption)
    {
        return myOption with
        {
            // 手动选择时不能隐藏流
            HideStreams = !myOption.InteractiveQuality && myOption.HideStreams,
        };
    }

    /// <summary>
    /// 解析用户输入的自定义工作目录，返回绝对路径。未指定时回落到进程当前目录。
    /// </summary>
    internal static string ResolveWorkDir(DownloadRequest myOption)
    {
        if (string.IsNullOrEmpty(myOption.WorkDir))
        {
            return Environment.CurrentDirectory;
        }

        var dir = Path.GetFullPath(Environment.ExpandEnvironmentVariables(myOption.WorkDir));
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        LogDebug("本次任务工作目录：{0}", dir);
        return dir;
    }
}
