using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core.Download;
using BBDown.Core.Entity;
using BBDown.Core.Workflow;

using static BBDown.Core.Logger;
using static BBDown.Core.Util.Utils;

namespace BBDown.Core.Media;

public static partial class TrackSelect
{
    internal static void SortDashTracks(ParsedResult parsedResult, WorkContext ctx, DownloadRequest myOption)
    {
        parsedResult.VideoTracks = SortTracks(parsedResult.VideoTracks, ctx.Run.DfnPriority, ctx.Run.EncodingPriority, myOption.VideoAscending, ctx.Run.EncodingFirst);
        parsedResult.AudioTracks = SortTracks(parsedResult.AudioTracks, ctx.Run.EncodingPriority, myOption.AudioAscending, ctx.Run.AudioDfnPriority);
        parsedResult.BackgroundAudioTracks = SortTracks(parsedResult.BackgroundAudioTracks, ctx.Run.EncodingPriority, myOption.AudioAscending, ctx.Run.AudioDfnPriority);
        foreach (var role in parsedResult.RoleAudioList)
        {
            role.Audio = SortTracks(role.Audio, ctx.Run.EncodingPriority, myOption.AudioAscending, ctx.Run.AudioDfnPriority);
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

    internal static List<Audio> SortTracks(List<Audio> audioTracks, Dictionary<string, byte> encodingPriority, bool audioAscending, Dictionary<string, int>? audioDfnPriority = null)
    {
        if (audioDfnPriority is { Count: > 0 })
        {
            // 按音质名（或 id）优先级排序：先查 Dfn，再查 Id，均未命中则排末尾。
            // 键与轨道名都转大写，使 "Hi-Res 无损" 等含小写字母的音质名与 --audio-quality 输入大小写无关
            return [.. audioTracks
                .OrderBy(a => audioDfnPriority.GetValueOrDefault(a.Dfn.ToUpper( ), audioDfnPriority.GetValueOrDefault(a.Id.ToUpper( ), 100)))
                .ThenBy(a => audioAscending ? a.Bandwidth : -a.Bandwidth)];
        }

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
                LogColor($"{index++}. {DescribeVideo(v, pageDur)}", false);
                if (onlyShowInfo)
                {
                    Log(v.BaseUrl);
                }
            }
        }

        if (parsedResult.AudioTracks.Count != 0)
        {
            Log($"共计 {parsedResult.AudioTracks.Count} 条音频流。");
            var index = 0;
            foreach (var a in parsedResult.AudioTracks)
            {
                LogColor($"{index++}. {DescribeAudio(a, pageDur)}", false);
                if (onlyShowInfo)
                {
                    Log(a.BaseUrl);
                }
            }
        }
    }

    // 音视频流描述（Dfn / 分辨率 / 编码 / 帧率 / 码率 / 估算体积），日志列表与交互选项共用同一格式化来源
    private static string DescribeVideo(Video v, int pageDur)
    {
        var pDur = pageDur == 0 ? v.Dur : pageDur;
        var size = v.Size > 0 ? v.Size : pDur * v.Bandwidth * 1024 / 8;
        return $"[{v.Dfn}] [{v.Res}] [{v.Codecs}] [{v.Fps}] [{v.Bandwidth} kbps] [~{FormatFileSize(size)}]".Replace("[] ", "");
    }

    private static string DescribeAudio(Audio a, int pageDur)
    {
        var pDur = pageDur == 0 ? a.Dur : pageDur;
        return $"[{a.Dfn}] [{a.Codecs}] [{a.Bandwidth} kbps] [~{FormatFileSize(pDur * a.Bandwidth * 1024 / 8)}]";
    }

    internal static void PrintFlvTracksInfo(ParsedResult parsedResult, List<string> clips, bool onlyShowInfo)
    {
        Log($"共计 {parsedResult.VideoTracks.Count} 条流（共有 {clips.Count} 个分段）。");
        var index = 0;
        foreach (var v in parsedResult.VideoTracks)
        {
            // Dur 为 0（接口未给时长）时跳过码率折算，否则除零得 Infinity 显示异常
            var kbps = v.Dur > 0 ? $"[~{v.Size / 1024 / v.Dur * 8:00} kbps] " : "";
            LogColor($"{index++}. [{v.Dfn}] [{v.Res}] [{v.Codecs}] [{v.Fps}] {kbps}[{FormatFileSize(v.Size)}]".Replace("[] ", ""), false);
            if (onlyShowInfo)
            {
                clips.ForEach(c => Log(c));
            }
        }
    }

    internal static void PrintSelectedTrackInfo(Video? selectedVideo, Audio? selectedAudio, int pageDur)
    {
        if (selectedVideo != null)
        {
            LogColor($"[视频] {DescribeVideo(selectedVideo, pageDur)}", false);
        }

        if (selectedAudio != null)
        {
            LogColor($"[音频] {DescribeAudio(selectedAudio, pageDur)}", false);
        }
    }

    /// <summary>
    /// 引导用户进行手动选择轨道；无应答（不交互）时回落默认序号 0。
    /// </summary>
    internal static async Task<(int VIndex, int AIndex)> PickTracksAsync(ParsedResult parsedResult, int pageDur, CancellationToken token)
    {
        var vIndex = 0;
        var aIndex = 0;
        if (parsedResult.VideoTracks.Count != 0)
        {
            var options = parsedResult.VideoTracks
                .Select((v, i) => new AskOption(i.ToString( ), $"{i}. {DescribeVideo(v, pageDur)}")).ToArray( );
            vIndex = await PickIndexAsync("请选择一条视频流（输入序号）：", options, token);
        }

        if (parsedResult.AudioTracks.Count != 0)
        {
            var options = parsedResult.AudioTracks
                .Select((a, i) => new AskOption(i.ToString( ), $"{i}. {DescribeAudio(a, pageDur)}")).ToArray( );
            aIndex = await PickIndexAsync("请选择一条音频流（输入序号）：", options, token);
        }

        return (vIndex, aIndex);
    }

    internal static async Task<int> PickDfnAsync(List<string> dfns, CancellationToken token)
    {
        var i = 0;
        dfns.ForEach(key => LogColor($"{i++}.{Config.GetQualityName(key)}"));
        var options = dfns
            .Select((key, n) => new AskOption(n.ToString( ), $"{n}. {Config.GetQualityName(key)}")).ToArray( );
        return await PickIndexAsync("请选择清晰度（输入序号）：", options, token);
    }

    // 序号选择：选项 Id 即序号字符串；无应答（不交互）回落 0，同现状非法输入回落 0
    private static async Task<int> PickIndexAsync(string prompt, AskOption[] options, CancellationToken token)
    {
        var answer = await AskBus.Ask(prompt, options, "0", token);
        return int.TryParse(answer?.OptionId, out var index) && index >= 0 && index < options.Length ? index : 0;
    }
}
