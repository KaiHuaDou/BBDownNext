using System.Collections.Generic;
using System.Globalization;

namespace BBDown.Core.Live;

/// <summary>
/// 直播间基础信息。<see cref="RoomId"/> 恒为真实房间号（短号已换算）。
/// </summary>
public sealed record LiveRoomInfo(
    string RoomId,
    string ShortId,
    string Uid,
    string Uname,
    string Title,
    int LiveStatus,
    bool Encrypted,
    bool PwdVerified,
    string Cover)
{
    /// <summary>直播状态 2 是轮播（录播循环），不是真开播，不予录制。</summary>
    public bool IsLiving => LiveStatus == 1;
}

/// <summary>
/// 一条可直接请求的直播流地址。同一清晰度会有多个 CDN <see cref="Host"/>，用于 failover。
/// </summary>
public sealed record LiveStreamCandidate(
    string Url,
    string Host,
    string ProtocolName,
    string FormatName,
    string CodecName,
    int CurrentQn);

/// <summary>
/// 一次 getRoomPlayInfo 的解析结果。<see cref="Candidates"/> 已按「编码优先级 → CDN 顺序」排好。
/// </summary>
public sealed record LivePlayInfo(
    int RequestedQn,
    int ActualQn,
    IReadOnlyList<int> AcceptQn,
    IReadOnlyList<LiveStreamCandidate> Candidates)
{
    /// <summary>
    /// 实际清晰度低于请求值。未登录时 B 站恒返回 250 且 accept_qn 仍列出 10000，
    /// 故降级判定只能比对 current_qn，不能信 accept_qn。
    /// </summary>
    public bool Degraded => ActualQn != RequestedQn;
}

public static class LiveQuality
{
    /// <summary>原画。B 站直播的最高可用档，非会员场景下常被降级到 250。</summary>
    public const int Original = 10000;

    private static readonly Dictionary<int, string> Descriptions = new( )
    {
        [30000] = "杜比",
        [20000] = "4K",
        [15000] = "2K",
        [10000] = "原画",
        [400] = "蓝光",
        [250] = "超清",
        [150] = "高清",
        [80] = "流畅",
    };

    public static string Describe(int qn)
    {
        return Descriptions.TryGetValue(qn, out var desc) ? desc : qn.ToString(CultureInfo.InvariantCulture);
    }
}
