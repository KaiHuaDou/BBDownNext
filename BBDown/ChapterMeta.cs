using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using BBDown.Core;

using static BBDown.Core.Entity.Entity;
using static BBDown.Core.Logger;
using static BBDown.Core.Util.HTTPUtil;
using static BBDown.Utils;

namespace BBDown;

/// <summary>
/// 视频章节信息：抓取分P章节点，并生成 ffmpeg / mp4box 混流用的 metadata 文本；
/// 另含 ffmpeg 杜比视界支持探测。
/// </summary>
internal static partial class ChapterMeta
{
    /// <summary>
    /// 获取章节信息
    /// </summary>
    public static async Task<List<ViewPoint>> FetchPointsAsync(string cid, string aid, Core.AppConfig cfg)
    {
        List<ViewPoint> points = [];
        try
        {
            var wts = DateTimeOffset.Now.ToUnixTimeSeconds( ).ToString(CultureInfo.InvariantCulture);
            var api = $"{BiliApi.PlayerWbiV2}?{Parser.WbiSign($"aid={aid}&cid={cid}&wts={wts}", cfg)}";
            var json = await GetWebSourceAsync(api, cfg);
            using var infoJson = JsonDocument.Parse(json);
            if (infoJson.RootElement.GetProperty("data").TryGetProperty("view_points", out var vPoint))
            {
                foreach (var point in vPoint.EnumerateArray( ))
                {
                    points.Add(new ViewPoint( )
                    {
                        title = point.GetProperty("content").GetString( )!,
                        start = int.Parse(point.GetProperty("from").ToString( )),
                        end = int.Parse(point.GetProperty("to").ToString( ))
                    });
                }
            }
        }
        catch (Exception ex)
        {
            LogDebug("解析章节信息失败: {0}", ex.Message);
        }

        return points;
    }

    /// <summary>
    /// 生成metadata文件, 用于ffmpeg混流章节信息
    /// </summary>
    public static string GetFFmpegMetaString(List<ViewPoint> points)
    {
        StringBuilder sb = new( );
        sb.AppendLine(";FFMETADATA");
        foreach (var p in points)
        {
            var time = 1000; //固定 1000
            sb.AppendLine("[CHAPTER]");
            sb.AppendLine($"TIMEBASE=1/{time}");
            sb.AppendLine($"START={p.start * time}");
            sb.AppendLine($"END={p.end * time}");
            sb.AppendLine($"title={p.title}");
            sb.AppendLine( );
        }

        return sb.ToString( );
    }

    /// <summary>
    /// 生成metadata文件, 用于mp4box混流章节信息
    /// </summary>
    public static string GetMp4boxMetaString(List<ViewPoint> points)
    {
        StringBuilder sb = new( );
        foreach (var p in points)
        {
            sb.AppendLine($"{FormatTime(p.start, true)} {p.title}");
        }

        return sb.ToString( );
    }

    /// <summary>
    /// 检测ffmpeg是否识别杜比视界
    /// </summary>
    public static bool CheckFFmpegDOVI( )
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = BBDownMuxer.FFMPEG,
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
            if (!match.Success) return false;
            if (Convert.ToInt32(match.Groups[1].Value) is (57 and >= 17) or > 57)
            {
                return true;
            }
        }
        catch (Exception ex)
        {
            LogDebug("探测 ffmpeg 杜比视界支持失败: {0}", ex.Message);
        }

        return false;
    }

    [GeneratedRegex("libavutil\\s+(\\d+)\\. +(\\d+)\\.")]
    private static partial Regex LibavutilRegex( );
}
