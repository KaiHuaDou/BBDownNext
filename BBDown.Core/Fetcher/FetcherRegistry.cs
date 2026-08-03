using System;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core.Entity;

using static BBDown.Core.Logger;

namespace BBDown.Core.Fetcher;

public static class FetcherRegistry
{
    public static async Task<VInfo> FetchAsync(string id, AppConfig cfg, bool useIntlApi = false, CancellationToken ct = default)
    {
        if (id.StartsWith(IdPrefix.Cheese))
        {
            return await CheeseInfoFetcher.FetchAsync(id, cfg, ct);
        }

        if (id.StartsWith(IdPrefix.EpColon))
        {
            return await FetchEpisodeAsync(id, cfg, useIntlApi, ct);
        }

        if (id.StartsWith(IdPrefix.SeriesBizId))
        {
            return await MediaListFetcher.FetchListAsync(id[IdPrefix.SeriesBizId.Length..], 5, true, "系列", cfg, ct);
        }

        if (id.StartsWith(IdPrefix.FavId))
        {
            return await FavListFetcher.FetchAsync(id, cfg, ct);
        }

        // 合集与系列共用 medialist 接口; 合集解析失败（被删/私密/无权，或"系列"被误识别为合集）时回退按系列重试。
        // 候选链集中在此处，各 Fetcher 保持单向、无互相调用。
        if (id.StartsWith(IdPrefix.ListBizId))
        {
            try
            {
                return await MediaListFetcher.FetchAsync(id, cfg, ct);
            }
            catch (InvalidOperationException ex)
            {
                try
                {
                    return await MediaListFetcher.FetchListAsync(id[IdPrefix.ListBizId.Length..], 5, true, "系列", cfg, ct);
                }
                catch (Exception seriesEx)
                {
                    throw new InvalidOperationException($"{ex.Message}; 按系列解析同样失败: {seriesEx.Message}", ex);
                }
            }
        }

        return await NormalInfoFetcher.FetchAsync(id, cfg, ct);
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
