using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core.Live;

namespace BBDown.Core.Tests;

public class LiveRecorderTests
{
    private const string Dest = "/tmp/rec/room";
    private const string Seg1 = Dest + ".001.bbdown.part";
    private const string Seg2 = Dest + ".002.bbdown.part";

    private static LivePlayInfo Info(params string[] hosts)
    {
        var candidates = hosts.Select(h => new LiveStreamCandidate($"https://{h}/live.flv?tk=1", h, "http_stream", "flv", "avc", 10000)).ToList( );
        return new LivePlayInfo(10000, 10000, [10000], candidates);
    }

    // avc 与 hevc 候选并存：首段成功后必须锁定编码，否则失败轮换会跳到 hevc，
    // 合并时套单一 bsf 会把 avc 段静默丢弃（数据丢失）
    private static LivePlayInfo MixedCodecInfo( )
    {
        var candidates = new List<LiveStreamCandidate>
        {
            new("https://cdn1/live.flv?tk=1", "cdn1", "http_stream", "flv", "avc", 10000),
            new("https://cdn2/live.flv?tk=1", "cdn2", "http_stream", "flv", "avc", 10000),
            new("https://cdn3/live.flv?tk=1", "cdn3", "http_stream", "flv", "hevc", 10000),
        };
        return new LivePlayInfo(10000, 10000, [10000], candidates);
    }

    // 服务端临时撤下 avc，只剩 hevc：用于验证锁定后的回退分支
    private static LivePlayInfo HevcOnlyInfo( )
    {
        var candidates = new List<LiveStreamCandidate>
        {
            new("https://cdn9/live.flv?tk=1", "cdn9", "http_stream", "flv", "hevc", 10000),
        };
        return new LivePlayInfo(10000, 10000, [10000], candidates);
    }

    /// <summary>把「第 N 次调用返回什么」写成脚本，越界后重复最后一项。</summary>
    private sealed class Script<T>(params T[] items)
    {
        private int calls;
        public int Calls => calls;
        public T Next( )
        {
            var i = Interlocked.Increment(ref calls) - 1;
            return items[Math.Min(i, items.Length - 1)];
        }
    }

    private sealed class Harness
    {
        public List<TimeSpan> Delays { get; } = [];
        public List<string> Deleted { get; } = [];
        public List<string> WrittenPaths { get; } = [];
        public List<string> UsedHosts { get; } = [];
        public Dictionary<string, long> Sizes { get; } = [];
        public List<int> SegmentStarts { get; } = [];

        public LiveRecorder Build(LiveRecorder.ResolveStream resolve, LiveRecorder.WriteSegment write) =>
            new(resolve,
                (candidate, path, ct) =>
                {
                    WrittenPaths.Add(path);
                    UsedHosts.Add(candidate.Host);
                    return write(candidate, path, ct);
                },
                delay: (d, _) => { Delays.Add(d); return Task.CompletedTask; },
                fileLength: p => Sizes.GetValueOrDefault(p, 0),
                deleteFile: Deleted.Add,
                onSegmentStart: SegmentStarts.Add);
    }

    private static (CancellationTokenSource Global, CancellationTokenSource Stop, CancellationTokenSource Record) Tokens( )
    {
        var global = new CancellationTokenSource( );
        var stop = new CancellationTokenSource( );
        return (global, stop, CancellationTokenSource.CreateLinkedTokenSource(global.Token, stop.Token));
    }

    // ---- 正常路径 ----

    [Fact]
    public async Task SingleSegment_UntilStreamEnds( )
    {
        var (global, stop, record) = Tokens( );
        using var _1 = global;
        using var _2 = stop;
        using var _3 = record;

        var plan = new Script<LivePlayInfo?>(Info("cdn1"), null);
        var h = new Harness( );
        var recorder = h.Build((_, _) => Task.FromResult(plan.Next( )), (_, _, _) => Task.FromResult(5000L));

        var result = await recorder.RunAsync(Dest, 10000, record.Token, global.Token);

        Assert.Equal([Seg1], result.Segments);
        Assert.Equal("avc", result.CodecName);
        Assert.Equal(LiveStopReason.StreamEnded, result.Reason);
        Assert.Empty(h.Delays);
        Assert.Equal([1], h.SegmentStarts);
    }

    // 断流后立即重连，不退避——干净的流结束不是故障
    [Fact]
    public async Task Reconnect_ProducesSecondSegment( )
    {
        var (global, stop, record) = Tokens( );
        using var _1 = global;
        using var _2 = stop;
        using var _3 = record;

        var plan = new Script<LivePlayInfo?>(Info("cdn1"), Info("cdn1"), null);
        var h = new Harness( );
        var recorder = h.Build((_, _) => Task.FromResult(plan.Next( )), (_, _, _) => Task.FromResult(5000L));

        var result = await recorder.RunAsync(Dest, 10000, record.Token, global.Token);

        Assert.Equal([Seg1, Seg2], result.Segments);
        Assert.Empty(h.Delays);
    }

    // ---- 空段与重试 ----

    [Fact]
    public async Task EmptySegment_IsDiscardedAndIndexNotAdvanced( )
    {
        var (global, stop, record) = Tokens( );
        using var _1 = global;
        using var _2 = stop;
        using var _3 = record;

        var plan = new Script<LivePlayInfo?>(Info("cdn1"), Info("cdn1"), null);
        var bytes = new Script<long>(0L, 5000L);
        var h = new Harness( );
        var recorder = h.Build((_, _) => Task.FromResult(plan.Next( )), (_, _, _) => Task.FromResult(bytes.Next( )));

        var result = await recorder.RunAsync(Dest, 10000, record.Token, global.Token);

        Assert.Equal([Seg1], result.Segments);
        Assert.Equal([Seg1], h.Deleted);
        // 两次都写在 .001 上：空段没占用序号
        Assert.Equal([Seg1, Seg1], h.WrittenPaths);
    }

    [Fact]
    public async Task Backoff_DoublesAndResetsAfterSuccess( )
    {
        var (global, stop, record) = Tokens( );
        using var _1 = global;
        using var _2 = stop;
        using var _3 = record;

        var plan = new Script<LivePlayInfo?>(Info("cdn1"), Info("cdn1"), Info("cdn1"), Info("cdn1"), Info("cdn1"), Info("cdn1"), null);
        var bytes = new Script<long>(0L, 0L, 0L, 5000L, 0L, 5000L);
        var h = new Harness( );
        var recorder = h.Build((_, _) => Task.FromResult(plan.Next( )), (_, _, _) => Task.FromResult(bytes.Next( )));

        await recorder.RunAsync(Dest, 10000, record.Token, global.Token);

        Assert.Equal([TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(1)], h.Delays);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 4)]
    [InlineData(5, 16)]
    [InlineData(9, 16)]
    public void Backoff_CapsAt16Seconds(int failures, int expectedSeconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), LiveRecorder.Backoff(failures));
    }

    // 录了几小时后网络彻底断掉，不能因为重试超限就把已录内容丢掉
    [Fact]
    public async Task MaxFailures_StopsWithoutThrowing_KeepsRecordedSegments( )
    {
        var (global, stop, record) = Tokens( );
        using var _1 = global;
        using var _2 = stop;
        using var _3 = record;

        var bytes = new Script<long>(5000L, 0L);
        var h = new Harness( );
        var recorder = h.Build((_, _) => Task.FromResult<LivePlayInfo?>(Info("cdn1")), (_, _, _) => Task.FromResult(bytes.Next( )));

        var result = await recorder.RunAsync(Dest, 10000, record.Token, global.Token);

        Assert.Equal([Seg1], result.Segments);
        Assert.Equal(LiveStopReason.TooManyFailures, result.Reason);
        // 第 10 次失败直接放弃，不再退避
        Assert.Equal(9, h.Delays.Count);
    }

    [Fact]
    public async Task ResolveThrows_IsRetried( )
    {
        var (global, stop, record) = Tokens( );
        using var _1 = global;
        using var _2 = stop;
        using var _3 = record;

        var attempts = 0;
        var h = new Harness( );
        var recorder = h.Build(
            (_, _) => ++attempts == 1 ? throw new HttpRequestException("boom") : Task.FromResult<LivePlayInfo?>(attempts == 2 ? Info("cdn1") : null),
            (_, _, _) => Task.FromResult(5000L));

        var result = await recorder.RunAsync(Dest, 10000, record.Token, global.Token);

        Assert.Equal([Seg1], result.Segments);
        Assert.Equal([TimeSpan.FromSeconds(1)], h.Delays);
    }

    // ---- CDN failover ----

    [Fact]
    public async Task CdnFailover_SwitchesHostAndProducesOneSegment( )
    {
        var (global, stop, record) = Tokens( );
        using var _1 = global;
        using var _2 = stop;
        using var _3 = record;

        var plan = new Script<LivePlayInfo?>(Info("cdn1", "cdn2"), Info("cdn1", "cdn2"), null);
        var h = new Harness( );
        var recorder = h.Build(
            (_, _) => Task.FromResult(plan.Next( )),
            (candidate, _, _) => candidate.Host == "cdn1"
                ? Task.FromException<long>(new HttpRequestException("403"))
                : Task.FromResult(5000L));

        var result = await recorder.RunAsync(Dest, 10000, record.Token, global.Token);

        Assert.Equal([Seg1], result.Segments);
        Assert.Equal(["cdn1", "cdn2"], h.UsedHosts);
    }

    // 首段锁定 avc 后，连续失败把 failures 推到 2（本应轮换到 hevc/cdn3），仍须只选 avc 候选，
    // 否则合并时套单一 bsf 会把 avc 段静默丢弃（数据丢失）
    [Fact]
    public async Task CodecPinned_AfterFirstSegment_StaysAvcUnderFailureRotation( )
    {
        var (global, stop, record) = Tokens( );
        using var _1 = global;
        using var _2 = stop;
        using var _3 = record;

        // 段1 成功(avc) → 段2/3 空(失败，failures 爬到 2) → 段4 成功(应锁定 avc，不选 hevc) → 流结束
        var plan = new Script<LivePlayInfo?>(MixedCodecInfo( ), MixedCodecInfo( ), MixedCodecInfo( ), MixedCodecInfo( ), null);
        var bytes = new Script<long>(5000L, 0L, 0L, 5000L);
        var h = new Harness( );
        var recorder = h.Build((_, _) => Task.FromResult(plan.Next( )), (_, _, _) => Task.FromResult(bytes.Next( )));

        var result = await recorder.RunAsync(Dest, 10000, record.Token, global.Token);

        Assert.Equal(2, result.Segments.Count);
        Assert.Equal("avc", result.CodecName);
        Assert.DoesNotContain("cdn3", h.UsedHosts);
    }

    // 锁定 avc 后服务端临时撤下 avc，须回退全集（用 hevc）而非无候选卡死（pool 空会除零崩溃）
    [Fact]
    public async Task CodecPinned_FallsBackToFullList_WhenPinnedCodecGone( )
    {
        var (global, stop, record) = Tokens( );
        using var _1 = global;
        using var _2 = stop;
        using var _3 = record;

        var plan = new Script<LivePlayInfo?>(MixedCodecInfo( ), HevcOnlyInfo( ), null);
        var h = new Harness( );
        var recorder = h.Build((_, _) => Task.FromResult(plan.Next( )), (_, _, _) => Task.FromResult(5000L));

        var result = await recorder.RunAsync(Dest, 10000, record.Token, global.Token);

        Assert.Equal(2, result.Segments.Count);
        Assert.Contains("cdn9", h.UsedHosts);
    }

    // ---- 异常终止 ----

    [Fact]
    public async Task NotLiving_NoSegments_Throws( )
    {
        var (global, stop, record) = Tokens( );
        using var _1 = global;
        using var _2 = stop;
        using var _3 = record;

        var h = new Harness( );
        var recorder = h.Build((_, _) => Task.FromResult<LivePlayInfo?>(null), (_, _, _) => Task.FromResult(5000L));

        var e = await Assert.ThrowsAsync<InvalidOperationException>(( ) => recorder.RunAsync(Dest, 10000, record.Token, global.Token));
        Assert.Contains("未录制到任何内容", e.Message, StringComparison.Ordinal);
    }

    // SIGINT：必须穿透，让 Program 返回 130 且不混流
    [Fact]
    public async Task GlobalCancel_Rethrows( )
    {
        var (global, stop, record) = Tokens( );
        using var _1 = global;
        using var _2 = stop;
        using var _3 = record;

        var h = new Harness( );
        var recorder = h.Build(
            (_, _) => Task.FromResult<LivePlayInfo?>(Info("cdn1")),
            (_, _, _) => { global.Cancel( ); return Task.FromResult(5000L); });

        await Assert.ThrowsAsync<OperationCanceledException>(( ) => recorder.RunAsync(Dest, 10000, record.Token, global.Token));
    }

    // SIGQUIT：正常返回，已写入的分段要参与混流
    [Fact]
    public async Task StopSignal_ReturnsRecordedSegments( )
    {
        var (global, stop, record) = Tokens( );
        using var _1 = global;
        using var _2 = stop;
        using var _3 = record;

        var h = new Harness( );
        var recorder = h.Build(
            (_, _) => Task.FromResult<LivePlayInfo?>(Info("cdn1")),
            (_, _, _) => { stop.Cancel( ); return Task.FromResult(5000L); });

        var result = await recorder.RunAsync(Dest, 10000, record.Token, global.Token);

        Assert.Equal([Seg1], result.Segments);
        Assert.Equal(LiveStopReason.UserStopped, result.Reason);
    }

    // 磁盘满不该进重试循环，但半截分段里可能有几小时内容，必须留下
    [Fact]
    public async Task DiskError_StopsImmediately_KeepsPartialSegment( )
    {
        var (global, stop, record) = Tokens( );
        using var _1 = global;
        using var _2 = stop;
        using var _3 = record;

        var h = new Harness( );
        h.Sizes[Seg1] = 999_999;
        var recorder = h.Build(
            (_, _) => Task.FromResult<LivePlayInfo?>(Info("cdn1")),
            (_, _, _) => Task.FromException<long>(new System.IO.IOException("磁盘空间不足")));

        var result = await recorder.RunAsync(Dest, 10000, record.Token, global.Token);

        Assert.Equal([Seg1], result.Segments);
        Assert.Equal(LiveStopReason.DiskError, result.Reason);
        Assert.Empty(h.Delays);
        Assert.Empty(h.Deleted);
    }

    [Fact]
    public async Task DiskError_TinyPartial_IsDiscardedAndThrows( )
    {
        var (global, stop, record) = Tokens( );
        using var _1 = global;
        using var _2 = stop;
        using var _3 = record;

        var h = new Harness( );
        var recorder = h.Build(
            (_, _) => Task.FromResult<LivePlayInfo?>(Info("cdn1")),
            (_, _, _) => Task.FromException<long>(new System.IO.IOException("磁盘空间不足")));

        await Assert.ThrowsAsync<InvalidOperationException>(( ) => recorder.RunAsync(Dest, 10000, record.Token, global.Token));
        Assert.Equal([Seg1], h.Deleted);
    }
}
