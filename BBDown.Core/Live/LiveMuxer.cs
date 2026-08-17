using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core.Download;
using BBDown.Core.Mux;
using BBDown.Core.Util;

using static BBDown.Core.Logger;
using static BBDown.Core.Util.Utils;

namespace BBDown.Core.Live;

/// <summary>
/// 把录制产出的分段 FLV 合成单个 mp4。
/// 不复用 <see cref="Muxer.MergeFLV"/>：后者单段时直接改名（会产出 FLV 内容配 .mp4 后缀的坏文件），
/// 且在转换成功前就删源文件，直播场景下一旦失败等于丢录像。
/// </summary>
public static class LiveMuxer
{
    // 超大文件的 faststart 需要整体二次写盘，几十 GB 的录像上代价远大于收益
    private const long FaststartMaxBytes = 4L * 1024 * 1024 * 1024;

    // copy 模式容器开销（FLV 标签 / TS 188 字节对齐 / MP4 moov）通常 < 2%，
    // 留出余量以容忍长录像的容器差异；明显偏小说明有分段被静默丢弃
    private const double MinMergeRatio = 0.9;

    /// <summary>
    /// 合并成功返回 true，并删除源分段；失败时保留全部分段供手工抢救。
    /// </summary>
    public static async Task<bool> MergeSegmentsAsync(IReadOnlyList<string> segments, string outPath, string codecName, ToolPaths tools, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(segments);

        var inputs = segments.Where(File.Exists).ToList( );
        if (inputs.Count == 0)
        {
            LogError("没有可合并的分段文件");
            return false;
        }

        var outDir = Path.GetDirectoryName(outPath);
        if (!string.IsNullOrEmpty(outDir))
        {
            Directory.CreateDirectory(outDir);
        }

        return inputs.Count == 1
            ? await RemuxAsync(inputs[0], outPath, inputs, new FileInfo(inputs[0]).Length, tools, ct)
            : await ConcatAsync(inputs, outPath, codecName, tools, ct);
    }

    private static async Task<bool> RemuxAsync(string input, string outPath, List<string> sources, long expectedBytes, ToolPaths tools, CancellationToken ct)
    {
        var faststart = new FileInfo(input).Length <= FaststartMaxBytes;
        if (!faststart)
        {
            LogWarn("文件较大, 跳过 faststart 优化 (可正常播放, 边下边播时需先缓冲)");
        }

        var code = await Utils.RunExe(tools.Ffmpeg, BuildLiveRemuxArgs(input, outPath, faststart, Config.DebugLog), ct);
        if (code != 0)
        {
            LogError($"混流失败 (ffmpeg 退出码 {code}), 已保留分段文件");
            return false;
        }

        if (!AcceptMergeIntegrity(outPath, expectedBytes))
        {
            return false;
        }

        sources.ForEach(SafeDelete);
        return true;
    }

    // 合并产物应接近各分段字节数之和；明显偏小说明有分段被静默丢弃，保留分段交由用户手工抢救
    private static bool AcceptMergeIntegrity(string outPath, long expectedBytes)
    {
        if (!File.Exists(outPath) || expectedBytes <= 0)
        {
            LogError("合并产物缺失, 已保留分段文件");
            return false;
        }

        var actual = new FileInfo(outPath).Length;
        if (actual < expectedBytes * MinMergeRatio)
        {
            LogError($"合并产物大小 {actual} 远小于分段总和 {expectedBytes}, 疑似数据丢失, 已保留分段文件");
            return false;
        }

        return true;
    }

    private static async Task<bool> ConcatAsync(List<string> inputs, string outPath, string codecName, ToolPaths tools, CancellationToken ct)
    {
        List<string> tsFiles = new(inputs.Count);
        var concatPath = Path.ChangeExtension(outPath, ".concat.ts");
        try
        {
            foreach (var input in inputs)
            {
                var tsPath = Path.ChangeExtension(input, ".ts");
                var code = await Utils.RunExe(tools.Ffmpeg, BuildLiveToTsArgs(input, tsPath, codecName, Config.DebugLog), ct);
                if (code != 0)
                {
                    // 单段损坏不该拖垮整场录像，跳过后继续拼其余分段
                    LogWarn($"分段 {Path.GetFileName(input)} 转换失败 (退出码 {code}), 已跳过");
                    SafeDelete(tsPath);
                    continue;
                }

                tsFiles.Add(tsPath);
            }

            if (tsFiles.Count == 0)
            {
                LogError("全部分段转换失败, 已保留分段文件");
                return false;
            }

            CombineMultipleFilesIntoSingleFile([.. tsFiles], concatPath);
            // 已转换并入的 ts 字节数之和才是真实预期，被跳过的损坏分段不计入
            var expected = tsFiles.Sum(t => new FileInfo(t).Length);
            return await RemuxAsync(concatPath, outPath, inputs, expected, tools, ct);
        }
        finally
        {
            tsFiles.ForEach(SafeDelete);
            SafeDelete(concatPath);
        }
    }

    internal static List<string> BuildLiveToTsArgs(string input, string output, string codecName, bool debugLog)
    {
        // +discardcorrupt 丢弃被标记为损坏的包（停录时分会段尾常截在半个 FLV tag 上），
        // -err_detect ignore_err 让 ffmpeg 遇到解析错误继续而非中止，避免合并因尾包损坏整段失败。
        // 二者配合可消除 h264 "Invalid NAL unit size" / "corrupt input packet" 这类吓人的报错。
        List<string> args = ["-loglevel", debugLog ? "verbose" : "error", "-y",
            "-fflags", "+genpts+discardcorrupt", "-err_detect", "ignore_err",
            "-i", input, "-map", "0", "-c", "copy", "-f", "mpegts"];
        var bsf = SelectBitstreamFilter(codecName);
        if (bsf != null)
        {
            args.AddRange(["-bsf:v", bsf]);
        }

        args.AddRange(["--", output]);
        return args;
    }

    // 直播流时间戳常跳变/回绕, 不重建 PTS 会导致时长错误甚至无法 seek；
    // +discardcorrupt / -err_detect ignore_err 同 BuildLiveToTsArgs 注释，压制停录截断导致的噪声与损坏。
    internal static List<string> BuildLiveRemuxArgs(string input, string output, bool faststart, bool debugLog)
    {
        List<string> args = ["-loglevel", debugLog ? "verbose" : "error", "-y",
            "-fflags", "+genpts+discardcorrupt", "-err_detect", "ignore_err",
            "-i", input, "-map", "0", "-c", "copy"];
        if (faststart)
        {
            args.AddRange(["-movflags", "+faststart"]);
        }

        args.AddRange(["-f", "mp4", "--", output]);
        return args;
    }

    /// <summary>
    /// mpegts 要求 Annex B, 而 FLV 里是 AVCC/HVCC。用错 bsf 会让 ffmpeg 直接报错，故按编码分派。
    /// </summary>
    internal static string? SelectBitstreamFilter(string codecName)
    {
        return codecName switch
        {
            "avc" or "h264" => "h264_mp4toannexb",
            "hevc" or "h265" => "hevc_mp4toannexb",
            _ => null
        };
    }
}
