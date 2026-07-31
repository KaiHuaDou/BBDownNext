using System.Threading.Tasks;

using BBDown.Core.Entity;

using static BBDown.Core.Logger;

namespace BBDown.Core.Fetcher;

public static class FetcherRegistry
{
    public static Task<VInfo> FetchAsync(string id, AppConfig cfg, bool useIntlApi = false)
    {
        return id switch
        {
            _ when id.StartsWith("cheese:") => CheeseInfoFetcher.FetchAsync(id, cfg),
            _ when id.StartsWith("ep:") => FetchEpisodeAsync(id, cfg, useIntlApi),
            _ when id.StartsWith("listBizId:") => MediaListFetcher.FetchAsync(id, cfg),
            _ when id.StartsWith("seriesBizId:") => SeriesListFetcher.FetchAsync(id, cfg),
            _ when id.StartsWith("favId:") => FavListFetcher.FetchAsync(id, cfg),
            _ => NormalInfoFetcher.FetchAsync(id, cfg),
        };
    }

    // 仅输入 EP/SS 时优先按番剧查找，找不到则回退到课程 (cheese) 查找。
    // 候选链集中在此处，调用方无需感知 cheese 的存在。
    private static async Task<VInfo> FetchEpisodeAsync(string id, AppConfig cfg, bool useIntlApi)
    {
        try
        {
            return useIntlApi
                ? await IntlBangumiInfoFetcher.FetchAsync(id, cfg)
                : await BangumiInfoFetcher.FetchAsync(id, cfg);
        }
        catch (BangumiNotFoundException)
        {
            LogWarn("未找到此 EP/SS 对应番剧信息，正在尝试按课程查找。");
            return await CheeseInfoFetcher.FetchAsync("cheese:" + id["ep:".Length..], cfg);
        }
    }
}
