using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Auth;
using BBDown.Core;
using BBDown.Core.Live;
using BBDown.Live;

using static BBDown.Core.Logger;

namespace BBDown.Pipeline;

/// <summary>
/// 直播录制编排。与音视频下载链路完全独立：直播没有分 P、没有可枚举的清晰度轨道、没有确定的总大小，
/// 走 WorkContext 那套只会处处不适配。分流点在 <see cref="Program.RunApp"/>。
/// </summary>
internal static class LiveDownload
{
    internal static async Task RunAsync(DownloadRequest myOption, LiveTarget target, CancellationToken ct = default)
    {
        // 录了几小时才发现没有 ffmpeg 是不可接受的，开录前就要探测
        var tools = WorkSetup.ResolveToolPaths(myOption);
        var workDir = WorkSetup.ResolveWorkDir(myOption);

        var (cookie, _) = CredentialStore.LoadAll(myOption.Cookie, myOption.AccessToken, ApiType.Web);
        var cfg = new AppConfig(cookie, "", myOption.Host, myOption.EpHost, myOption.TvHost, myOption.Area, "");

        Log("获取直播间信息...");
        var room = await LiveFetcher.FetchRoomAsync(target, cfg, ct);
        Log($"直播间：{room.RoomId}{(string.IsNullOrEmpty(room.ShortId) || room.ShortId == "0" ? "" : $"（短号 {room.ShortId}）")}");
        Log($"主播：{room.Uname}");
        Log($"标题：{room.Title}");

        if (room.Encrypted && !room.PwdVerified)
        {
            throw new InvalidOperationException("该直播间已加密，需要密码才能观看，无法录制");
        }

        if (!room.IsLiving)
        {
            throw new InvalidOperationException(room.LiveStatus == 2
                ? "该直播间正在轮播（非真实开播），不予录制"
                : "主播当前未开播");
        }

        var probe = await LiveFetcher.FetchPlayInfoAsync(room.RoomId, myOption.LiveQuality, cfg, ct)
            ?? throw new InvalidOperationException("未获取到可用的直播流地址");
        if (probe.Degraded)
        {
            LogWarn($"请求 {LiveQuality.Describe(myOption.LiveQuality)} 未获批准，服务端下发 {LiveQuality.Describe(probe.ActualQn)}（未登录或非大会员时常见）");
        }

        var codec = probe.Candidates[0].CodecName;
        var outPath = LiveFileNaming.EnsureUnique(Path.Combine(workDir, LiveFileNaming.BuildBaseName(room.Uname, room.Title, DateTime.Now) + ".mp4"));
        // 分段与成品同名，EnsureUnique 加的 -2 后缀也要跟着带上，否则并发录同一场会互相覆盖分段
        var destPathWithoutExtension = Path.Combine(Path.GetDirectoryName(outPath)!, Path.GetFileNameWithoutExtension(outPath));

        Log($"清晰度：{LiveQuality.Describe(probe.ActualQn)}（{codec}）");
        Log($"输出文件：{outPath}");
        LogColor("开始录制。按 Ctrl+Break 停止录制并合并；按 Ctrl+C 直接中断（保留分段，不合并）");

        using var stopCts = new CancellationTokenSource( );
        using var signalScope = LiveSignal.Register(stopCts);
        using var recordCts = CancellationTokenSource.CreateLinkedTokenSource(ct, stopCts.Token);

        LiveRecordResult result;
        using (var progress = new LiveProgress($"{LiveQuality.Describe(probe.ActualQn)}({codec})"))
        {
            var recorder = new LiveRecorder(
                (qn, token) => LiveFetcher.FetchPlayInfoAsync(room.RoomId, qn, cfg, token),
                (candidate, partPath, token) => LiveSegmentWriter.WriteAsync(candidate.Url, partPath, cookie, progress.Add, token),
                onSegmentStart: progress.StartSegment);

            result = await recorder.RunAsync(destPathWithoutExtension, myOption.LiveQuality, recordCts.Token, ct);
        }

        Log($"录制结束（{Describe(result.Reason)}），共 {result.Segments.Count} 个分段，正在合并...");
        // 合并只受 SIGINT 影响：SIGQUIT 的语义就是「停录并合并」，把 stopCts 传进来会让 ffmpeg 立刻被杀
        if (!await LiveMuxer.MergeSegmentsAsync(result.Segments, outPath, result.CodecName, tools, ct))
        {
            throw new InvalidOperationException("合并失败，分段文件已保留");
        }

        Log($"已保存：{outPath}");
    }

    private static string Describe(LiveStopReason reason)
    {
        return reason switch
        {
            LiveStopReason.UserStopped => "用户停止",
            LiveStopReason.StreamEnded => "主播已下播",
            LiveStopReason.TooManyFailures => "连续重连失败",
            LiveStopReason.DiskError => "磁盘写入失败",
            _ => reason.ToString( )
        };
    }
}
