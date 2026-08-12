using System.Collections.Generic;
using System.Globalization;

namespace BBDown.Core.Download;

/// <summary>
/// 直播清晰度档位。作为下载请求的配置项归入下载模型，避免 DownloadRequest 反向依赖直播域。
/// </summary>
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
