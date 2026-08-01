using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core;
using BBDown.Core.Entity;

using static BBDown.BBDownDownloadUtil;
using static BBDown.Core.Entity.Entity;
using static BBDown.Core.Logger;
using static BBDown.Utils;

namespace BBDown;

internal sealed partial class Program
{

    /// <summary>
    /// 解析用户指定的编码优先级，返回优先级表与首个编码
    /// </summary>
    internal static (Dictionary<string, byte> EncodingPriority, string FirstEncoding) ParseEncodingPriority(MyOption myOption)
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
                    continue;
                encodingPriority[encoding] = index;
                index++;
            }
        }

        return (encodingPriority, firstEncoding);
    }

    internal static BBDownDanmakuFormat[] ParseDownloadDanmakuFormats(MyOption myOption)
    {
        if (string.IsNullOrEmpty(myOption.DownloadDanmakuFormats)) return BBDownDanmakuFormatInfo.DefaultFormats;

        var formats = myOption.DownloadDanmakuFormats.Replace("，", ",").ToLower( ).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (formats.Any(format => !BBDownDanmakuFormatInfo.AllFormatNames.Contains(format)))
        {
            LogError($"包含不支持的下载弹幕格式：{myOption.DownloadDanmakuFormats}。");
            return BBDownDanmakuFormatInfo.DefaultFormats;
        }

        return formats.Select(BBDownDanmakuFormatInfo.FromFormatName).ToArray( );
    }

    /// <summary>
    /// 解析用户输入的清晰度规格优先级
    /// </summary>
    internal static Dictionary<string, int> ParseDfnPriority(MyOption myOption)
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
    /// <param name="myOption"></param>
    /// <exception cref="Exception"></exception>
    private static void FindBinaries(MyOption myOption)
    {
        if (!string.IsNullOrEmpty(myOption.FFmpegPath) && File.Exists(myOption.FFmpegPath))
        {
            BBDownMuxer.FFMPEG = myOption.FFmpegPath;
        }

        if (!string.IsNullOrEmpty(myOption.Mp4boxPath) && File.Exists(myOption.Mp4boxPath))
        {
            BBDownMuxer.MP4BOX = myOption.Mp4boxPath;
        }

        if (!string.IsNullOrEmpty(myOption.Aria2cPath) && File.Exists(myOption.Aria2cPath))
        {
            BBDownAria2c.ARIA2C = myOption.Aria2cPath;
        }
        //寻找 ffmpeg 或 mp4box
        if (!myOption.SkipMux)
        {
            //ffmpeg 与 mp4box 都探测，以便下载时按需选择 (杜比视界可能临时改用 mp4box)
            if (string.IsNullOrEmpty(BBDownMuxer.FFMPEG) || !File.Exists(BBDownMuxer.FFMPEG))
            {
                var binPath = FindExecutable("ffmpeg");
                if (!string.IsNullOrEmpty(binPath)) BBDownMuxer.FFMPEG = binPath;
            }

            if (string.IsNullOrEmpty(BBDownMuxer.MP4BOX) || !File.Exists(BBDownMuxer.MP4BOX))
            {
                var binPath = FindExecutable("mp4box", "MP4Box", "MP4box");
                if (!string.IsNullOrEmpty(binPath)) BBDownMuxer.MP4BOX = binPath;
            }

            if (string.IsNullOrEmpty(BBDownMuxer.FFMPEG) || !File.Exists(BBDownMuxer.FFMPEG))
            {
                throw new InvalidOperationException("找不到可执行的 ffmpeg 文件");
            }
        }

        //寻找 aria2c
        if (myOption.UseAria2c)
        {
            if (string.IsNullOrEmpty(BBDownAria2c.ARIA2C) || !File.Exists(BBDownAria2c.ARIA2C))
            {
                var binPath = FindExecutable("aria2c");
                if (string.IsNullOrEmpty(binPath))
                    throw new InvalidOperationException("找不到可执行的 aria2c 文件");
                BBDownAria2c.ARIA2C = binPath;
            }
        }
    }

    /// <summary>
    /// 处理有冲突的选项
    /// </summary>
    internal static void HandleConflictingOptions(MyOption myOption)
    {
        //手动选择时不能隐藏流
        if (myOption.Interactive)
        {
            myOption.HideStreams = false;
        }
        //audioOnly 和 videoOnly 同时开启则全部忽视
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
    private static string ResolveWorkDir(MyOption myOption)
    {
        if (string.IsNullOrEmpty(myOption.WorkDir)) return Environment.CurrentDirectory;

        myOption.WorkDir = Environment.ExpandEnvironmentVariables(myOption.WorkDir);
        var dir = Path.GetFullPath(myOption.WorkDir);
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        LogDebug("本次任务工作目录：{0}", dir);
        return dir;
    }

    private static readonly object archiveLock = new( );
    private static Dictionary<(string Aid, string Cid), string>? _archiveCache;
    private static bool _archiveOldFormatWarned;

    // 仅在该分 P 完整成功（含混流）后写入；键为 (aid, cid)，同 aid 不同分 P 互不干扰
    public static void SaveArchive(string aid, string cid, string savePath)
    {
        lock (archiveLock)
        {
            _archiveCache ??= LoadArchives( );
            _archiveCache[(aid, cid)] = savePath;
            var filePath = Path.Combine(APP_DIR, "BBDown.archives");
            File.AppendAllText(filePath, $"{Environment.NewLine}{aid}\t{cid}\t{savePath}");
        }
    }

    public static bool CheckArchive(string aid, string cid)
    {
        lock (archiveLock)
        {
            _archiveCache ??= LoadArchives( );
            if (_archiveCache.TryGetValue((aid, cid), out var savePath))
            {
                // 文件被删/移走 → 视为未下载，重新下
                return string.IsNullOrEmpty(savePath) || File.Exists(savePath);
            }
            return false;
        }
    }

    // 进程内一次性载入；旧格式（aid| 拼接、无制表符）整体忽略
    private static Dictionary<(string Aid, string Cid), string> LoadArchives( )
    {
        var dict = new Dictionary<(string, string), string>( );
        var filePath = Path.Combine(APP_DIR, "BBDown.archives");
        if (!File.Exists(filePath)) return dict;
        foreach (var line in File.ReadAllLines(filePath))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var parts = line.Split('\t');
            if (parts.Length < 2)
            {
                if (!_archiveOldFormatWarned)
                {
                    _archiveOldFormatWarned = true;
                    LogWarn("检测到旧版 BBDown.archives（已失效），已忽略；新的归档记录将以 aid\\tcid\\t路径 格式写入。");
                }
                continue;
            }
            dict[(parts[0], parts[1])] = parts.Length > 2 ? parts[2] : "";
        }
        return dict;
    }

    /// <summary>
    /// 获取选中的分 P 列表。返回 null 表示不筛选（全量下载）；空列表表示用户显式指定但无任何合法分 P（一个都不下）。
    /// 语法：-p all｜1｜1,2,5｜3-5（闭区间，含两端）｜16-（开区间，到末集）｜-22（开区间，从首集）｜
    /// 1,2,3-3,4-5,6-10,15-latest（混合）｜latest/new=最后一集｜last/LAST=倒数第二集。
    /// 关键字大小写不敏感；表达式首尾、项内空白与尾逗号均忽略；越界数字夹紧到有效边界并提醒；倒序区间自动交换。
    /// </summary>
    internal static List<string>? GetSelectedPages(MyOption myOption, VInfo vInfo, string input)
    {
        if (string.IsNullOrWhiteSpace(myOption.SelectPage))
        {
            //如果用户没有选择分 P, 根据 epid 或 query param 来确定某一集
            if (!string.IsNullOrEmpty(vInfo.Index))
            {
                Log("程序已自动选择你输入的集数，如果要下载其他集数请自行指定分 P（如可使用 -p ALL 代表全部）。");
                return [vInfo.Index];
            }
            var urlPage = GetQueryString("p", input);
            if (!string.IsNullOrEmpty(urlPage))
            {
                Log("程序已自动选择你输入的集数，如果要下载其他集数请自行指定分 P（如可使用 -p ALL 代表全部）。");
                return [urlPage];
            }
            return null;
        }

        if (myOption.SelectPage.Trim().Equals("ALL", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var pagesInfo = vInfo.PagesInfo;
        var lastIndex = pagesInfo[^1].index;        // 列表末项，即最后一集（兼容非连续 index）
        var firstIndex = pagesInfo[0].index;
        var secondLastIndex = pagesInfo.Count >= 2 ? pagesInfo[^2].index : -1;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var anyValid = false;

        foreach (var rawToken in myOption.SelectPage.Split(','))
        {
            var token = rawToken.Trim();
            if (token.Length == 0) continue;

            if (token.Contains('-'))
            {
                var parts = token.Split('-', 2);
                var startStr = parts[0].Trim();
                var endStr = parts.Length > 1 ? parts[1].Trim() : "";

                var startValid = startStr.Length == 0;
                var start = startValid ? firstIndex : ResolveIndex(startStr, firstIndex, lastIndex, secondLastIndex, out startValid);
                var endValid = endStr.Length == 0;
                var end = endValid ? lastIndex : ResolveIndex(endStr, firstIndex, lastIndex, secondLastIndex, out endValid);

                if (!startValid || !endValid) continue;

                if (start > end) (start, end) = (end, start);   // 倒序区间归一化

                for (var i = start; i <= end; i++)
                {
                    if (seen.Add(i.ToString())) anyValid = true;
                }
            }
            else
            {
                var value = ResolveIndex(token, firstIndex, lastIndex, secondLastIndex, out var valid);
                if (!valid) continue;
                if (seen.Add(value.ToString())) anyValid = true;
            }
        }

        return anyValid ? [.. seen.OrderBy(x => int.Parse(x))] : [];
    }

    // 解析单个分 P 片段：latest/new → 最后一集；last/LAST → 倒数第二集；数字越界则夹紧到有效边界并提醒。
    // 无法解析（非数字非关键字）返回 (0, false)。
    private static int ResolveIndex(string part, int firstIndex, int lastIndex, int secondLastIndex, out bool valid)
    {
        valid = true;
        var upper = part.ToUpperInvariant();
        if (upper is "LATEST" or "NEW")
        {
            return lastIndex;
        }
        if (upper is "LAST")
        {
            if (secondLastIndex < 0)
            {
                LogError($"分 P 选择「{part}」需要至少 2 个分 P，已忽略。");
                valid = false;
                return 0;
            }
            return secondLastIndex;
        }
        if (int.TryParse(part, out var n))
        {
            if (n < firstIndex)
            {
                Log($"分 P 选择「{part}」小于最小分 P {firstIndex}，已夹紧到 {firstIndex}。");
                return firstIndex;
            }
            if (n > lastIndex)
            {
                Log($"分 P 选择「{part}」超出最大分 P {lastIndex}，已夹紧到 {lastIndex}。");
                return lastIndex;
            }
            return n;
        }
        LogError($"分 P 选择「{part}」不是合法的分 P 编号或关键字（可用：latest/new/last），已忽略。");
        valid = false;
        return 0;
    }

    /// <summary>
    /// 处理 CDN 域名
    /// </summary>
    /// <param name="myOption"></param>
    /// <param name="video"></param>
    /// <param name="audio"></param>
    private static void HandlePcdn(MyOption myOption, Video? selectedVideo, Audio? selectedAudio, AppConfig cfg)
    {
        if (myOption.UposHost is { Length: 0 })
        {
            //处理 PCDN
            if (!myOption.AllowPcdn)
            {
                var pcdnReg = PcdnRegex( );
                if (selectedVideo != null && pcdnReg.IsMatch(selectedVideo.baseUrl))
                {
                    LogWarn($"检测到视频流为 PCDN，尝试强制替换为 {BACKUP_HOST}...");
                    selectedVideo.baseUrl = pcdnReg.Replace(selectedVideo.baseUrl, $"://{BACKUP_HOST}/");
                }

                if (selectedAudio != null && pcdnReg.IsMatch(selectedAudio.baseUrl))
                {
                    LogWarn($"检测到音频流为 PCDN，尝试强制替换为 {BACKUP_HOST}...");
                    selectedAudio.baseUrl = pcdnReg.Replace(selectedAudio.baseUrl, $"://{BACKUP_HOST}/");
                }
            }

            var akamReg = AkamRegex( );
            if (selectedVideo != null && cfg.Area is not { Length: 0 } && selectedVideo.baseUrl.Contains("akamaized.net"))
            {
                LogWarn($"检测到视频流为外国源，尝试强制替换为{BACKUP_HOST}……");
                selectedVideo.baseUrl = akamReg.Replace(selectedVideo.baseUrl, $"://{BACKUP_HOST}/");
            }

            if (selectedAudio != null && cfg.Area is not { Length: 0 } && selectedAudio.baseUrl.Contains("akamaized.net"))
            {
                LogWarn($"检测到音频流为外国源，尝试强制替换为{BACKUP_HOST}……");
                selectedAudio.baseUrl = akamReg.Replace(selectedAudio.baseUrl, $"://{BACKUP_HOST}/");
            }
        }
        else
        {
            if (selectedVideo != null)
            {
                LogWarn($"尝试将视频流强制替换为{myOption.UposHost}……");
                selectedVideo.baseUrl = UposRegex( ).Replace(selectedVideo.baseUrl, $"://{myOption.UposHost}/");
            }

            if (selectedAudio != null)
            {
                LogWarn($"尝试将音频流强制替换为{myOption.UposHost}……");
                selectedAudio.baseUrl = UposRegex( ).Replace(selectedAudio.baseUrl, $"://{myOption.UposHost}/");
            }
        }
    }

    /// <summary>
    /// 打印解析到的各个轨道信息
    /// </summary>
    /// <param name="parsedResult"></param>
    /// <param name="pageDur"></param>
    private static void PrintAllTracksInfo(ParsedResult parsedResult, int pageDur, bool onlyShowInfo)
    {
        if (parsedResult.BackgroundAudioTracks.Count != 0 && parsedResult.RoleAudioList.Count != 0)
        {
            Log($"共计 {parsedResult.BackgroundAudioTracks.Count} 条背景音频流。");
            var index = 0;
            foreach (var a in parsedResult.BackgroundAudioTracks)
            {
                var pDur = pageDur == 0 ? a.dur : pageDur;
                LogColor($"{index++}. [{a.codecs}] [{a.bandwidth} kbps] [~{FormatFileSize(pDur * a.bandwidth * 1024 / 8)}]", false);
            }

            Log($"共计 {parsedResult.RoleAudioList.Count} 条配音，每条包含 {parsedResult.RoleAudioList[0].audio.Count} 条配音流。");
            index = 0;
            foreach (var a in parsedResult.RoleAudioList[0].audio)
            {
                var pDur = pageDur == 0 ? a.dur : pageDur;
                LogColor($"{index++}. [{a.codecs}] [{a.bandwidth} kbps] [~{FormatFileSize(pDur * a.bandwidth * 1024 / 8)}]", false);
            }
        }
        //展示所有的音视频流信息
        if (parsedResult.VideoTracks.Count != 0)
        {
            Log($"共计 {parsedResult.VideoTracks.Count} 条视频流。");
            var index = 0;
            foreach (var v in parsedResult.VideoTracks)
            {
                var pDur = pageDur == 0 ? v.dur : pageDur;
                var size = v.size > 0 ? v.size : pDur * v.bandwidth * 1024 / 8;
                LogColor($"{index++}. [{v.dfn}] [{v.res}] [{v.codecs}] [{v.fps}] [{v.bandwidth} kbps] [~{FormatFileSize(size)}]".Replace("[] ", ""), false);
                if (onlyShowInfo) Console.WriteLine(v.baseUrl);
            }
        }

        if (parsedResult.AudioTracks.Count != 0)
        {
            Log($"共计 {parsedResult.AudioTracks.Count} 条音频流。");
            var index = 0;
            foreach (var a in parsedResult.AudioTracks)
            {
                var pDur = pageDur == 0 ? a.dur : pageDur;
                LogColor($"{index++}. [{a.codecs}] [{a.bandwidth} kbps] [~{FormatFileSize(pDur * a.bandwidth * 1024 / 8)}]", false);
                if (onlyShowInfo) Console.WriteLine(a.baseUrl);
            }
        }
    }

    private static void PrintSelectedTrackInfo(Video? selectedVideo, Audio? selectedAudio, int pageDur)
    {
        if (selectedVideo != null)
        {
            var pDur = pageDur == 0 ? selectedVideo.dur : pageDur;
            var size = selectedVideo.size > 0 ? selectedVideo.size : pDur * selectedVideo.bandwidth * 1024 / 8;
            LogColor($"[视频] [{selectedVideo.dfn}] [{selectedVideo.res}] [{selectedVideo.codecs}] [{selectedVideo.fps}] [{selectedVideo.bandwidth} kbps] [~{FormatFileSize(size)}]".Replace("[] ", ""), false);
        }

        if (selectedAudio != null)
        {
            var pDur = pageDur == 0 ? selectedAudio.dur : pageDur;
            LogColor($"[音频] [{selectedAudio.codecs}] [{selectedAudio.bandwidth} kbps] [~{FormatFileSize(pDur * selectedAudio.bandwidth * 1024 / 8)}]", false);
        }
    }

    /// <summary>
    /// 引导用户进行手动选择轨道
    /// </summary>
    /// <param name="parsedResult"></param>
    /// <param name="vIndex"></param>
    /// <param name="aIndex"></param>
    private static void SelectTrackManually(ParsedResult parsedResult, ref int vIndex, ref int aIndex)
    {
        if (parsedResult.VideoTracks.Count != 0)
        {
            Log("请选择一条视频流（输入序号）：", false);
            Console.ForegroundColor = ConsoleColor.Cyan;
            vIndex = Convert.ToInt32(Console.ReadLine( ));
            if (vIndex > parsedResult.VideoTracks.Count || vIndex < 0) vIndex = 0;
            Console.ResetColor( );
        }

        if (parsedResult.AudioTracks.Count != 0)
        {
            Log("请选择一条音频流（输入序号）：", false);
            Console.ForegroundColor = ConsoleColor.Cyan;
            aIndex = Convert.ToInt32(Console.ReadLine( ));
            if (aIndex > parsedResult.AudioTracks.Count || aIndex < 0) aIndex = 0;
            Console.ResetColor( );
        }
    }

    [GeneratedRegex("://.*:\\d+/")]
    private static partial Regex PcdnRegex( );
    [GeneratedRegex("://.*akamaized\\.net/")]
    private static partial Regex AkamRegex( );
    [GeneratedRegex("://[^/]+/")]
    private static partial Regex UposRegex( );
}
