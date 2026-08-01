using System.Threading;
using System.Threading.Tasks;

using BBDown.Core.Entity;

using static BBDown.Core.Logger;

namespace BBDown.Core.Fetcher;

public static class FetcherRegistry
{
    public static Task<VInfo> FetchAsync(string id, AppConfig cfg, bool useIntlApi = false, CancellationToken ct = default)
    {
        return id switch
        {
            _ when id.StartsWith(IdPrefix.Cheese) => CheeseInfoFetcher.FetchAsync(id, cfg, ct),
            _ when id.StartsWith(IdPrefix.EpColon) => FetchEpisodeAsync(id, cfg, useIntlApi, ct),
            _ when id.StartsWith(IdPrefix.ListBizId) => MediaListFetcher.FetchAsync(id, cfg, ct),
            _ when id.StartsWith(IdPrefix.SeriesBizId) => SeriesListFetcher.FetchAsync(id, cfg, ct),
            _ when id.StartsWith(IdPrefix.FavId) => FavListFetcher.FetchAsync(id, cfg, ct),
            _ => NormalInfoFetcher.FetchAsync(id, cfg, ct),
        };
    }

    // 仅输入 EP/SS 时优先按番剧查找，找不到则回退到课程 (cheese) 查找。
    // 候选链集中在此处，调用方无需感知 cheese 的存在。
    private static async Task<VInfo> FetchEpisodeAsync(string id, AppConfig cfg, bool useIntlApi, CancellationToken ct = default)
    {
        try
        {
            return useIntlApi
                ? await IntlBangumiInfoFetcher.FetchAsync(id, cfg, ct)
                : await BangumiInfoFetcher.FetchAsync(id, cfg, ct);
        }
        catch (BangumiNotFoundException)
        {
            LogWarn("未找到此 EP/SS 对应番剧信息，正在尝试按课程查找。");
            return await CheeseInfoFetcher.FetchAsync(IdPrefix.Cheese + id[IdPrefix.EpColon.Length..], cfg, ct);
        }
    }
}
