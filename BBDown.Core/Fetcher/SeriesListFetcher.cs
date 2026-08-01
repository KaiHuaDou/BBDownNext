using System.Threading;
using System.Threading.Tasks;

using BBDown.Core.Entity;

namespace BBDown.Core.Fetcher;

/// <summary>
/// 列表解析
/// https://space.bilibili.com/23630128/channel/seriesdetail?sid=340933
/// </summary>
public static class SeriesListFetcher
{
    public static Task<VInfo> FetchAsync(string id, AppConfig cfg, CancellationToken ct = default)
    {
        return FetchByBizIdAsync(id[IdPrefix.SeriesBizId.Length..], cfg, ct);
    }

    internal static Task<VInfo> FetchByBizIdAsync(string bizId, AppConfig cfg, CancellationToken ct = default)
    {
        return MediaListFetcher.FetchListAsync(bizId, 5, true, "系列", cfg, ct);
    }
}
