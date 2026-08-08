using System;
using System.Collections.Generic;
using System.Linq;

using BBDown.Core;
using BBDown.Core.Entity;

using static BBDown.Core.Logger;
using static BBDown.Util.Utils;

namespace BBDown.Media;

internal static partial class TrackSelect
{
    internal static void SortDashTracks(ParsedResult parsedResult, WorkContext ctx, DownloadRequest myOption)
    {
        parsedResult.VideoTracks = SortTracks(parsedResult.VideoTracks, ctx.Run.DfnPriority, ctx.Run.EncodingPriority, myOption.VideoAscending, ctx.Run.EncodingFirst);
        parsedResult.AudioTracks = SortTracks(parsedResult.AudioTracks, ctx.Run.EncodingPriority, myOption.AudioAscending);
        parsedResult.BackgroundAudioTracks = SortTracks(parsedResult.BackgroundAudioTracks, ctx.Run.EncodingPriority, myOption.AudioAscending);
        foreach (var role in parsedResult.RoleAudioList)
        {
            role.Audio = SortTracks(role.Audio, ctx.Run.EncodingPriority, myOption.AudioAscending);
        }
    }

    internal static List<Video> SortTracks(List<Video> videoTracks, Dictionary<string, int> dfnPriority, Dictionary<string, byte> encodingPriority, bool videoAscending, bool encodingFirst)
    {
        //用户同时输入了自定义分辨率优先级和自定义编码优先级，则根据输入顺序依次进行排序
        return dfnPriority.Count != 0 && encodingPriority.Count != 0 && encodingFirst
            ? [.. videoTracks
                .OrderBy(v => encodingPriority.GetValueOrDefault(v.Codecs, (byte)100))
                .ThenBy(v => dfnPriority.GetValueOrDefault(v.Dfn, 100))
                .ThenBy(v => Config.QualityRank(v.Id))
                .ThenBy(v => videoAscending ? v.Bandwidth : -v.Bandwidth)]
            : [.. videoTracks
                .OrderBy(v => dfnPriority.GetValueOrDefault(v.Dfn, 100))
                .ThenBy(v => encodingPriority.GetValueOrDefault(v.Codecs, (byte)100))
                .ThenBy(v => Config.QualityRank(v.Id))
                .ThenBy(v => videoAscending ? v.Bandwidth : -v.Bandwidth)];
    }

    internal static List<Audio> SortTracks(List<Audio> audioTracks, Dictionary<string, byte> encodingPriority, bool audioAscending)
    {
        return [.. audioTracks
            .OrderBy(a => encodingPriority.GetValueOrDefault(a.ShortCodecs, (byte)100))
            .ThenBy(a => audioAscending ? a.Bandwidth : -a.Bandwidth)];
    }

    internal static void PrintAllTracksInfo(ParsedResult parsedResult, int pageDur, bool onlyShowInfo)
    {
        if (parsedResult.BackgroundAudioTracks.Count != 0 && parsedResult.RoleAudioList.Count != 0)
        {
            Log($"共计 {parsedResult.BackgroundAudioTracks.Count} 条背景音频流。");
            var index = 0;
            foreach (var a in parsedResult.BackgroundAudioTracks)
            {
                var pDur = pageDur == 0 ? a.Dur : pageDur;
                LogColor($"{index++}. [{a.Codecs}] [{a.Bandwidth} kbps] [~{FormatFileSize(pDur * a.Bandwidth * 1024 / 8)}]", false);
            }

            Log($"共计 {parsedResult.RoleAudioList.Count} 条配音，每条包含 {parsedResult.RoleAudioList[0].Audio.Count} 条配音流。");
            index = 0;
            foreach (var a in parsedResult.RoleAudioList[0].Audio)
            {
                var pDur = pageDur == 0 ? a.Dur : pageDur;
                LogColor($"{index++}. [{a.Codecs}] [{a.Bandwidth} kbps] [~{FormatFileSize(pDur * a.Bandwidth * 1024 / 8)}]", false);
            }
        }
        //展示所有的音视频流信息
        if (parsedResult.VideoTracks.Count != 0)
        {
            Log($"共计 {parsedResult.VideoTracks.Count} 条视频流。");
            var index = 0;
            foreach (var v in parsedResult.VideoTracks)
            {
                var pDur = pageDur == 0 ? v.Dur : pageDur;
                var size = v.Size > 0 ? v.Size : pDur * v.Bandwidth * 1024 / 8;
                LogColor($"{index++}. [{v.Dfn}] [{v.Res}] [{v.Codecs}] [{v.Fps}] [{v.Bandwidth} kbps] [~{FormatFileSize(size)}]".Replace("[] ", ""), false);
                if (onlyShowInfo)
                {
                    Console.WriteLine(v.BaseUrl);
                }
            }
        }

        if (parsedResult.AudioTracks.Count != 0)
        {
            Log($"共计 {parsedResult.AudioTracks.Count} 条音频流。");
            var index = 0;
            foreach (var a in parsedResult.AudioTracks)
            {
                var pDur = pageDur == 0 ? a.Dur : pageDur;
                LogColor($"{index++}. [{a.Codecs}] [{a.Bandwidth} kbps] [~{FormatFileSize(pDur * a.Bandwidth * 1024 / 8)}]", false);
                if (onlyShowInfo)
                {
                    Console.WriteLine(a.BaseUrl);
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
            LogColor($"{index++}. [{v.Dfn}] [{v.Res}] [{v.Codecs}] [{v.Fps}] [~{v.Size / 1024 / v.Dur * 8:00} kbps] [{FormatFileSize(v.Size)}]".Replace("[] ", ""), false);
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
            var pDur = pageDur == 0 ? selectedVideo.Dur : pageDur;
            var size = selectedVideo.Size > 0 ? selectedVideo.Size : pDur * selectedVideo.Bandwidth * 1024 / 8;
            LogColor($"[视频] [{selectedVideo.Dfn}] [{selectedVideo.Res}] [{selectedVideo.Codecs}] [{selectedVideo.Fps}] [{selectedVideo.Bandwidth} kbps] [~{FormatFileSize(size)}]".Replace("[] ", ""), false);
        }

        if (selectedAudio != null)
        {
            var pDur = pageDur == 0 ? selectedAudio.Dur : pageDur;
            LogColor($"[音频] [{selectedAudio.Codecs}] [{selectedAudio.Bandwidth} kbps] [~{FormatFileSize(pDur * selectedAudio.Bandwidth * 1024 / 8)}]", false);
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