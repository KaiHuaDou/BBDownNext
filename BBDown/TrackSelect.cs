using System;
using System.Collections.Generic;
using System.Linq;

using BBDown.Core;
using BBDown.Core.Entity;

using static BBDown.Core.Entity.Entity;
using static BBDown.Core.Logger;
using static BBDown.Utils;

namespace BBDown;

internal static partial class TrackSelect
{
    internal static void SortDashTracks(ParsedResult parsedResult, WorkContext ctx, DownloadOptions myOption)
    {
        parsedResult.VideoTracks = SortTracks(parsedResult.VideoTracks, ctx.DfnPriority, ctx.EncodingPriority, myOption.VideoAscending, ctx.EncodingFirst);
        parsedResult.AudioTracks = SortTracks(parsedResult.AudioTracks, ctx.EncodingPriority, myOption.AudioAscending);
        parsedResult.BackgroundAudioTracks = SortTracks(parsedResult.BackgroundAudioTracks, ctx.EncodingPriority, myOption.AudioAscending);
        foreach (var role in parsedResult.RoleAudioList)
        {
            role.audio = SortTracks(role.audio, ctx.EncodingPriority, myOption.AudioAscending);
        }
    }

    internal static List<Video> SortTracks(List<Video> videoTracks, Dictionary<string, int> dfnPriority, Dictionary<string, byte> encodingPriority, bool videoAscending, bool encodingFirst)
    {
        //用户同时输入了自定义分辨率优先级和自定义编码优先级，则根据输入顺序依次进行排序
        return dfnPriority.Count != 0 && encodingPriority.Count != 0 && encodingFirst
            ? [.. videoTracks
                .OrderBy(v => encodingPriority.GetValueOrDefault(v.codecs, (byte)100))
                .ThenBy(v => dfnPriority.GetValueOrDefault(v.dfn, 100))
                .ThenByDescending(v => Convert.ToInt32(v.id))
                .ThenBy(v => videoAscending ? v.bandwidth : -v.bandwidth)]
            : [.. videoTracks
                .OrderBy(v => dfnPriority.GetValueOrDefault(v.dfn, 100))
                .ThenBy(v => encodingPriority.GetValueOrDefault(v.codecs, (byte)100))
                .ThenByDescending(v => Convert.ToInt32(v.id))
                .ThenBy(v => videoAscending ? v.bandwidth : -v.bandwidth)];
    }

    internal static List<Audio> SortTracks(List<Audio> audioTracks, Dictionary<string, byte> encodingPriority, bool audioAscending)
    {
        return [.. audioTracks
            .OrderBy(a => encodingPriority.GetValueOrDefault(a.shortCodecs, (byte)100))
            .ThenBy(a => audioAscending ? a.bandwidth : -a.bandwidth)];
    }

    internal static void PrintAllTracksInfo(ParsedResult parsedResult, int pageDur, bool onlyShowInfo)
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
                if (onlyShowInfo)
                {
                    Console.WriteLine(v.baseUrl);
                }
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
                if (onlyShowInfo)
                {
                    Console.WriteLine(a.baseUrl);
                }
            }
        }
    }

    internal static void PrintFlvTracksInfo(ParsedResult parsedResult, List<string> clips, bool onlyShowInfo)
    {
        Log($"共计 {parsedResult.VideoTracks.Count} 条流（共有 {clips.Count} 个分段）。");
        var index = 0;
        foreach (var v in parsedResult.VideoTracks)
        {
            LogColor($"{index++}. [{v.dfn}] [{v.res}] [{v.codecs}] [{v.fps}] [~{v.size / 1024 / v.dur * 8:00} kbps] [{FormatFileSize(v.size)}]".Replace("[] ", ""), false);
            if (onlyShowInfo)
            {
                clips.ForEach(Console.WriteLine);
            }
        }
    }

    internal static void PrintSelectedTrackInfo(Video? selectedVideo, Audio? selectedAudio, int pageDur)
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
    internal static void PickTracks(ParsedResult parsedResult, ref int vIndex, ref int aIndex)
    {
        if (parsedResult.VideoTracks.Count != 0)
        {
            Log("请选择一条视频流（输入序号）：", false);
            vIndex = ReadIndex(parsedResult.VideoTracks.Count);
        }

        if (parsedResult.AudioTracks.Count != 0)
        {
            Log("请选择一条音频流（输入序号）：", false);
            aIndex = ReadIndex(parsedResult.AudioTracks.Count);
        }
    }

    internal static int PickDfn(List<string> dfns)
    {
        var i = 0;
        dfns.ForEach(key => LogColor($"{i++}.{Config.GetQualityName(key)}"));
        Log("请选择清晰度（输入序号）：", false);
        return ReadIndex(dfns.Count);
    }

    private static int ReadIndex(int count)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        var input = Console.ReadLine( );
        Console.ResetColor( );
        return ParseIndex(input, count);
    }

    // 序号非法（空行、非数字、越界）时一律回落到 0，交互选轨不该因手滑输入而抛异常
    internal static int ParseIndex(string? input, int count)
    {
        return int.TryParse(input, out var index) && index >= 0 && index < count ? index : 0;
    }
}
