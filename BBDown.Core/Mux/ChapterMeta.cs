using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core;
using BBDown.Core.Util;

using static BBDown.Core.Logger;
using static BBDown.Core.Util.HTTPUtil;
using static BBDown.Core.Util.Utils;
using BBDown.Core.Entity;
using BBDown.Core.Download;

namespace BBDown.Core.Mux;

/// <summary>
/// x/player/wbi/v2 的解析结果：章节点，以及充电专属稿件标记。
/// </summary>
public readonly record struct PlayerV2Info(List<ViewPoint> Points, bool UpowerExclusive, string UpowerTitle)
{
    public static PlayerV2Info Empty => new([], false, "");
}

/// <summary>
/// 视频章节信息：抓取分 P 章节点，并生成 FFmpeg / MP4Box 混流用的 metadata 文本；
/// 另含 FFmpeg 杜比视界支持探测。
/// </summary>
public static partial class ChapterMeta
{
    /// <summary>
    /// 获取播放器信息（章节点 + 充电专属标记）
    /// </summary>
    public static async Task<PlayerV2Info> FetchPlayerV2Async(string cid, string aid, Core.AppConfig cfg, CancellationToken ct = default)
    {
        try
        {
            var api = $"{BiliApi.PlayerWbiV2}?{SignUtil.WbiSignNow($"aid={aid}&cid={cid}", cfg)}";
            return ParsePlayerV2(await GetWebSourceAsync(api, cfg, ct: ct));
        }
        catch (Exception ex)
        {
            LogDebug("解析播放器信息失败：{0}", ex.Message);
            return PlayerV2Info.Empty;
        }
    }

    internal static PlayerV2Info ParsePlayerV2(string json)
    {
        using var infoJson = JsonDocument.Parse(json);
        if (!infoJson.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
        {
            return PlayerV2Info.Empty;
        }

        List<ViewPoint> points = [];
        if (data.TryGetProperty("view_points", out var vPoint) && vPoint.ValueKind == JsonValueKind.Array)
        {
            foreach (var point in vPoint.EnumerateArray( ))
            {
                points.Add(new ViewPoint( )
                {
                    Title = point.GetProperty("content").GetString( )!,
                    Start = int.Parse(point.GetProperty("from").ToString( )),
                    End = int.Parse(point.GetProperty("to").ToString( ))
                });
            }
        }

        var exclusive = data.TryGetProperty("is_upower_exclusive", out var ex) && ex.ValueKind == JsonValueKind.True;
        var title = "";
        if (data.TryGetProperty("elec_high_level", out var elec) && elec.ValueKind == JsonValueKind.Object
            && elec.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String)
        {
            title = t.GetString( ) ?? "";
        }

        return new PlayerV2Info(points, exclusive, title);
    }

    /// <summary>
    /// 生成 metadata 文件，用于 FFmpeg 混流章节信息
    /// </summary>
    public static string GetFFmpegMetaString(List<ViewPoint> points)
    {
        StringBuilder sb = new( );
        sb.AppendLine(";FFMETADATA");
        foreach (var p in points)
        {
            const int Time = 1000;
            sb.AppendLine("[CHAPTER]");
            sb.AppendLine($"TIMEBASE=1/{Time}");
            sb.AppendLine($"START={p.Start * Time}");
            sb.AppendLine($"END={p.End * Time}");
            sb.AppendLine($"title={p.Title}");
            sb.AppendLine( );
        }

        return sb.ToString( );
    }

    /// <summary>
    /// 生成 metadata 文件，用于 mp4box 混流章节信息
    /// </summary>
    public static string GetMp4boxMetaString(List<ViewPoint> points)
    {
        StringBuilder sb = new( );
        foreach (var p in points)
        {
            sb.AppendLine($"{FormatTime(p.Start, true)} {p.Title}");
        }

        return sb.ToString( );
    }

    /// <summary>
    /// 检测 FFmpeg 是否识别杜比视界
    /// </summary>
    public static bool CheckFFmpegDOVI(ToolPaths tools)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = tools.Ffmpeg,
                    Arguments = "-version",
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                }
            };
            process.Start( );
            var info = process.StandardOutput.ReadToEnd( ) + Environment.NewLine + process.StandardError.ReadToEnd( );
            process.WaitForExit( );
            var match = LibavutilRegex( ).Match(info);
            if (!match.Success)
            {
                return false;
            }

            if (Convert.ToInt32(match.Groups[1].Value) is (57 and >= 17) or > 57)
            {
                return true;
            }
        }
        catch (Exception ex)
        {
            LogDebug("探测 FFmpeg 杜比视界支持失败：{0}", ex.Message);
        }

        return false;
    }

    [GeneratedRegex("libavutil\\s+(\\d+)\\. +(\\d+)\\.")]
    private static partial Regex LibavutilRegex( );
}
