using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core.Live;

using static BBDown.Core.Logger;
using static BBDown.Util.Utils;

namespace BBDown.Live;

internal enum LiveStopReason
{
    /// <summary>收到 SIGQUIT，用户主动停录。</summary>
    UserStopped,
    /// <summary>主播下播 / 直播间无可用流。</summary>
    StreamEnded,
    /// <summary>连续失败次数达上限，保住已录内容进混流。</summary>
    TooManyFailures,
    /// <summary>磁盘写入失败（多为磁盘满），继续重试只会一直失败。</summary>
    DiskError
}

internal sealed record LiveRecordResult(IReadOnlyList<string> Segments, string CodecName, LiveStopReason Reason);

/// <summary>
/// 录制状态机：解析流地址 → 写一个分段 → 断了就退避重连写下一段，直到停录 / 下播 / 失败超限。
/// 网络、文件、计时全部经委托注入，状态机本身可离线单测。
/// </summary>
internal sealed class LiveRecorder(
    LiveRecorder.ResolveStream resolve,
    LiveRecorder.WriteSegment write,
    Func<TimeSpan, CancellationToken, Task>? delay = null,
    Func<string, long>? fileLength = null,
    Action<string>? deleteFile = null,
    Action<int>? onSegmentStart = null)
{
    /// <summary>返回 null 表示已下播。</summary>
    internal delegate Task<LivePlayInfo?> ResolveStream(int qn, CancellationToken ct);

    /// <summary>契约同 <see cref="LiveSegmentWriter.WriteAsync"/>：取消时返回已写字节数而非抛出。</summary>
    internal delegate Task<long> WriteSegment(LiveStreamCandidate candidate, string partPath, CancellationToken ct);

    private const int MaxConsecutiveFailures = 10;
    // 服务端偶尔在断流瞬间回几百字节的残帧，这种段拿去混流只会让 ffmpeg 报错
    private const long MinSegmentBytes = 1024;
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(16);

    private readonly ResolveStream resolve = resolve;
    private readonly WriteSegment write = write;
    private readonly Func<TimeSpan, CancellationToken, Task> delay = delay ?? Task.Delay;
    private readonly Func<string, long> fileLength = fileLength ?? (p => File.Exists(p) ? new FileInfo(p).Length : 0);
    private readonly Action<string> deleteFile = deleteFile ?? SafeDelete;
    private readonly Action<int>? onSegmentStart = onSegmentStart;

    /// <summary>
    /// <paramref name="recordToken"/> 是 SIGINT 与 SIGQUIT 的联合取消源，<paramref name="globalToken"/> 仅 SIGINT。
    /// 二者用于区分「停录进混流」与「整个进程中断」——后者必须把 <see cref="OperationCanceledException"/>
    /// 抛出去，让调用方返回 130 且不混流。
    /// </summary>
    public async Task<LiveRecordResult> RunAsync(string destPathWithoutExtension, int qn, CancellationToken recordToken, CancellationToken globalToken)
    {
        List<string> segments = [];
        var codecName = "";
        // 首段成功后锁定编码：同一场直播的 avc/hevc 候选并存时，失败轮换会挑到另一种编码，
        // 而合并阶段只对全部分段套同一 bsf，编码不一的段会被 ffmpeg 静默丢弃（数据丢失）。锁死后全程同编码。
        var pinnedCodec = "";
        var failures = 0;
        var reason = LiveStopReason.UserStopped;

        while (!recordToken.IsCancellationRequested)
        {
            LivePlayInfo? info;
            try
            {
                info = await resolve(qn, recordToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception e)
            {
                var giveUp = await GiveUpAsync(e.Message, ++failures, recordToken);
                if (giveUp is not null)
                {
                    reason = giveUp.Value;
                    break;
                }

                continue;
            }

            if (info is null || info.Candidates.Count == 0)
            {
                reason = LiveStopReason.StreamEnded;
                break;
            }

            // 同一清晰度的多个 CDN 按失败次数轮换，避免死磕一个坏节点。
            // 锁定编码后只在该编码的候选里轮换；服务端临时撤下该编码时回退全集，避免无候选可用。
            var pool = pinnedCodec.Length == 0
                ? info.Candidates
                : [.. info.Candidates.Where(c => c.CodecName == pinnedCodec)];
            if (pool.Count == 0)
            {
                pool = info.Candidates;
            }

            var candidate = pool[failures % pool.Count];
            var partPath = LiveFileNaming.BuildSegmentPath(destPathWithoutExtension, segments.Count + 1);
            onSegmentStart?.Invoke(segments.Count + 1);

            long written;
            try
            {
                written = await write(candidate, partPath, recordToken);
            }
            catch (IOException e)
            {
                // 磁盘满 / 目标不可写：重试只会以同样的方式失败，直接保住已录内容
                LogError($"写入分段失败：{e.Message}");
                reason = LiveStopReason.DiskError;
                Keep(segments, partPath, candidate, ref codecName);
                pinnedCodec = codecName;
                break;
            }
            catch (Exception e)
            {
                Discard(partPath);
                var giveUp = await GiveUpAsync(e.Message, ++failures, recordToken);
                if (giveUp is not null)
                {
                    reason = giveUp.Value;
                    break;
                }

                continue;
            }

            if (written >= MinSegmentBytes)
            {
                segments.Add(partPath);
                codecName = candidate.CodecName;
                pinnedCodec = codecName;
                failures = 0;
            }
            else
            {
                Discard(partPath);
            }

            if (recordToken.IsCancellationRequested)
            {
                break;
            }

            if (written < MinSegmentBytes)
            {
                var giveUp = await GiveUpAsync("分段无有效数据", ++failures, recordToken);
                if (giveUp is not null)
                {
                    reason = giveUp.Value;
                    break;
                }

                continue;
            }

            LogWarn("直播流中断，正在重连...");
        }

        // SIGINT 必须穿透到 Program：保留 part、不混流、退出码 130
        globalToken.ThrowIfCancellationRequested( );

        if (segments.Count == 0)
        {
            throw new InvalidOperationException(reason == LiveStopReason.StreamEnded
                ? "直播已结束或无可用流，未录制到任何内容"
                : "录制失败，未产出任何分段");
        }

        return new LiveRecordResult(segments, codecName, reason);
    }

    private void Keep(List<string> segments, string partPath, LiveStreamCandidate candidate, ref string codecName)
    {
        if (fileLength(partPath) < MinSegmentBytes)
        {
            deleteFile(partPath);
            return;
        }

        segments.Add(partPath);
        codecName = candidate.CodecName;
    }

    private void Discard(string partPath)
    {
        if (fileLength(partPath) < MinSegmentBytes)
        {
            deleteFile(partPath);
        }
    }

    /// <summary>
    /// 返回非 null 表示应停止录制（达最大失败次数，或等待期间被取消），值即停止原因。
    /// </summary>
    private async Task<LiveStopReason?> GiveUpAsync(string message, int failures, CancellationToken recordToken)
    {
        if (failures >= MaxConsecutiveFailures)
        {
            LogError($"连续 {failures} 次失败（{message}），停止录制并合并已有分段");
            return LiveStopReason.TooManyFailures;
        }

        var wait = Backoff(failures);
        LogWarn($"录制中断（{message}），{wait.TotalSeconds:0} 秒后重连（第 {failures} 次）");
        try
        {
            await delay(wait, recordToken);
            return null;
        }
        catch (OperationCanceledException)
        {
            return LiveStopReason.UserStopped;
        }
    }

    internal static TimeSpan Backoff(int failures)
    {
        return TimeSpan.FromSeconds(Math.Min(Math.Pow(2, Math.Max(failures, 1) - 1), MaxBackoff.TotalSeconds));
    }
}
