using BBDown.Core.Entity;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;


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
