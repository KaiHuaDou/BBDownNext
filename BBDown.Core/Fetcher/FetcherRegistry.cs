using System;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core.Entity;

using static BBDown.Core.Logger;
using static BBDown.Core.ResourceId;

namespace BBDown.Core.Fetcher;

public static class FetcherRegistry
{
    // switch 表达式按 ResourceId 子类型分发：缺分支编译报错。新增输入类型只需在 InputResolver 增加构造点 + 在此加一个 case。
    // useIntlApi 作为统一形参贯穿（番剧分支内部据此选 bangumi/intl），其余分支用 _ 丢弃。
    public static async Task<VInfo> FetchAsync(ResourceId id, AppConfig cfg, bool useIntlApi = false, CancellationToken ct = default)
    {
        return id switch
        {
            Av a => await NormalInfoFetcher.FetchAsync(a.Aid, cfg, ct),
            Ep e => await FetchEpisodeAsync(e, cfg, useIntlApi, ct),
            Season s when useIntlApi => throw new NotSupportedException(
                "国际版番剧接口(--intl-api)不支持 md/整季输入，请改用具体 ep 号，或去掉 --intl-api。"),
            Season s => await BangumiInfoFetcher.FetchAsync(s, cfg, ct),
            CheeseEp e => await CheeseInfoFetcher.FetchAsync(e, cfg, ct),
            CheeseSeason s => await CheeseInfoFetcher.FetchAsync(s, cfg, ct),
            Series s => await MediaListFetcher.FetchListAsync(s.BizId, 5, true, "系列", cfg, ct),
            Fav f => await FavListFetcher.FetchAsync(f, cfg, ct),
            WatchLater => await WatchLaterFetcher.FetchAsync(cfg, ct),
            MediaList m => await FetchMediaListWithSeriesFallback(m, cfg, ct),
            Space s => await SpaceListFetcher.FetchAsync(s.Mid, cfg, ct),
            _ => throw new ArgumentOutOfRangeException(nameof(id), id, $"未知的 id 类型：{id.GetType( ).Name}")
        };
    }

    // 仅输入 EP 时优先按番剧查找，找不到则回退到课程 (cheese) 查找。
    // 候选链集中在此处，调用方无需感知 cheese 的存在。
    private static async Task<VInfo> FetchEpisodeAsync(Ep ep, AppConfig cfg, bool useIntlApi, CancellationToken ct = default)
    {
        try
        {
            return useIntlApi
                ? await IntlBangumiInfoFetcher.FetchAsync(ep, cfg, ct)
                : await BangumiInfoFetcher.FetchAsync(ep, cfg, ct);
        }
        catch (BangumiNotFoundException)
        {
            // 只有 ep 形态会走到这里；整季形态（Season）不经过本方法，天然不回退。
            LogWarn("未找到此 EP/SS 对应番剧信息，正在尝试按课程查找。");
            return await CheeseInfoFetcher.FetchAsync(new CheeseEp(ep.EpId), cfg, ct);
        }
    }

    // 合集与系列共用 medialist 接口；合集解析失败（被删/私密/无权，或"系列"被误识别为合集）时回退按系列重试。
    // 候选链集中在此处，各 Fetcher 保持单向、无互相调用。
    private static async Task<VInfo> FetchMediaListWithSeriesFallback(MediaList list, AppConfig cfg, CancellationToken ct)
    {
        try
        {
            return await MediaListFetcher.FetchAsync(list, cfg, ct);
        }
        catch (InvalidOperationException ex)
        {
            try
            {
                return await MediaListFetcher.FetchListAsync(list.BizId, 5, true, "系列", cfg, ct);
            }
            catch (Exception seriesEx)
            {
                throw new InvalidOperationException($"{ex.Message}; 按系列解析同样失败: {seriesEx.Message}", ex);
            }
        }
    }
}
