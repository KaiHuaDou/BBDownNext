using System;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core.Entity;

using static BBDown.Core.Logger;

namespace BBDown.Core.Fetcher;

public static class FetcherRegistry
{
    // 声明式路由表：顺序即优先级。新增输入类型只需在此加一行，无需改动分发控制流。
    // 每个条目为 (前缀谓词, 抓取函数)；遍历命中第一个谓词即调用。
    // useIntlApi 作为统一形参贯穿（番剧分支内部据此选 bangumi/intl），其余分支用 _ 丢弃。
    // 无命中时由末尾的 NormalInfoFetcher 兜底，保持与旧 if/else 链完全一致的行为。
    private static readonly (Func<string, bool> Matches, FetchFn Fetch)[] Routes =
    [
        (s => s.StartsWith(IdPrefix.Cheese),     (s, c, _, t) => CheeseInfoFetcher.FetchAsync(s, c, t)),
        (s => s.StartsWith(IdPrefix.EpColon),    FetchEpisodeAsync),
        (s => s.StartsWith(IdPrefix.SeriesBizId),(s, c, _, t) => MediaListFetcher.FetchListAsync(s[IdPrefix.SeriesBizId.Length..], 5, true, "系列", c, t)),
        (s => s.StartsWith(IdPrefix.FavId),      (s, c, _, t) => FavListFetcher.FetchAsync(s, c, t)),
        (s => s.StartsWith(IdPrefix.ListBizId),  (s, c, _, t) => FetchMediaListWithSeriesFallback(s, c, t)),
        (s => s.StartsWith(IdPrefix.SpaceMid),   (s, c, _, t) => SpaceListFetcher.FetchAsync(s, c, t)),
    ];

    private delegate Task<VInfo> FetchFn(string id, AppConfig cfg, bool useIntlApi, CancellationToken ct);

    public static async Task<VInfo> FetchAsync(string id, AppConfig cfg, bool useIntlApi = false, CancellationToken ct = default)
    {
        foreach (var (Matches, Fetch) in Routes)
        {
            if (Matches(id))
            {
                return await Fetch(id, cfg, useIntlApi, ct);
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
        catch (BangumiNotFoundException ex)
        {
            var rawId = id[IdPrefix.EpColon.Length..];
            // 非纯数字 id（含 md/整季的 "ss{season_id}" 形态）不做课程回退：课程接口 id 空间稠密，
            // 静默命中无关课程风险极高；仅纯数字 ep_id 查不到番剧时才允许回退。
            if (!long.TryParse(rawId, out _))
            {
                throw new InvalidOperationException($"未找到番剧信息，且无安全的课程回退（id={rawId}）。", ex);
            }

            LogWarn("未找到此 EP/SS 对应番剧信息，正在尝试按课程查找。");
            return await CheeseInfoFetcher.FetchAsync(IdPrefix.Cheese + rawId, cfg, ct);
        }
    }

    // 合集与系列共用 medialist 接口；合集解析失败（被删/私密/无权，或"系列"被误识别为合集）时回退按系列重试。
    // 候选链集中在此处，各 Fetcher 保持单向、无互相调用。
    private static async Task<VInfo> FetchMediaListWithSeriesFallback(string id, AppConfig cfg, CancellationToken ct)
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
}
