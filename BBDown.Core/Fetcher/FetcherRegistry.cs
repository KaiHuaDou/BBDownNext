using System.Threading.Tasks;

using BBDown.Core.Entity;

namespace BBDown.Core.Fetcher;

public static class FetcherRegistry
{
    public static Task<VInfo> FetchAsync(string id, AppConfig cfg, bool useIntlApi = false)
    {
        return id switch
        {
            _ when id.StartsWith("cheese") => CheeseInfoFetcher.FetchAsync(id, cfg),
            _ when id.StartsWith("ep") => useIntlApi
                ? IntlBangumiInfoFetcher.FetchAsync(id, cfg)
                : BangumiInfoFetcher.FetchAsync(id, cfg),
            _ when id.StartsWith("mid") => SpaceVideoFetcher.FetchAsync(id, cfg),
            _ when id.StartsWith("listBizId") => MediaListFetcher.FetchAsync(id, cfg),
            _ when id.StartsWith("seriesBizId") => SeriesListFetcher.FetchAsync(id, cfg),
            _ when id.StartsWith("favId") => FavListFetcher.FetchAsync(id, cfg),
            _ => NormalInfoFetcher.FetchAsync(id, cfg),
        };
    }
}
