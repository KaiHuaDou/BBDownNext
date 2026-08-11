using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core.Entity;

using static BBDown.Core.ResourceId;
using static BBDown.Core.Util.HTTPUtil;
using static BBDown.Core.Util.JsonUtil;

namespace BBDown.Core.Fetcher;

public static class BangumiInfoFetcher
{
    // ep 形态按 ep_id 拉单集并定位「当前选择第几集」；season 形态（md/ss/整季输入）按 season_id 拉整季正片
    public static Task<VInfo> FetchAsync(Ep ep, AppConfig cfg, CancellationToken ct = default)
    {
        return FetchCoreAsync($"https://{cfg.EpHost}{BiliApi.SeasonPgcPath}?ep_id={ep.EpId}", ep.EpId.ToString( ), locate: true, cfg, ct);
    }

    public static Task<VInfo> FetchAsync(Season season, AppConfig cfg, CancellationToken ct = default)
    {
        return FetchCoreAsync($"https://{cfg.EpHost}{BiliApi.SeasonPgcPath}?season_id={season.SeasonId}", "", locate: false, cfg, ct);
    }

    // locate=true 为单集形态：目标 ep 可能不在主 episodes 而在 section（番外/花絮），需扫描定位；
    // 单集形态缺 result 抛 BangumiNotFoundException 以触发课程回退；整季形态不回退，避免误命中 id 空间稠密、毫不相关的课程。
    private static async Task<VInfo> FetchCoreAsync(string api, string locateEpId, bool locate, AppConfig cfg, CancellationToken ct)
    {
        var json = await GetWebSourceAsync(api, cfg, null, ct);
        using var infoJson = JsonDocument.Parse(json);
        if (!infoJson.RootElement.TryGetProperty("result", out var result))
        {
            if (locate)
            {
                throw new BangumiNotFoundException($"未找到 EP/SS 对应的番剧信息：ep_id={locateEpId}");
            }

            var (code, message) = ReadApiError(infoJson.RootElement);
            throw new InvalidOperationException($"获取番剧信息失败(code={code})：{message}");

        }

        var cover = result.GetProperty("cover").ToString( );
        var title = result.GetProperty("title").ToString( );
        var desc = result.GetProperty("evaluate").ToString( );
        var pubTimeStr = result.GetProperty("publish").GetProperty("pub_time").ToString( );
        var pubTime = string.IsNullOrEmpty(pubTimeStr) ? 0 : DateTimeOffset.ParseExact(pubTimeStr, "yyyy-MM-dd HH:mm:ss", null).ToUnixTimeSeconds( );
        TryGetArray(result, "episodes", out var pages);

        // 整季形态无需定位，跳过 section 扫描
        if (locate && !ContainsEpisode(pages, locateEpId) && TryGetArray(result, "section", out var sections))
        {
            foreach (var section in sections.EnumerateArray( ))
            {
                if (TryGetArray(section, "episodes", out var sectionEpisodes) && ContainsEpisode(sectionEpisodes, locateEpId))
                {
                    title += "[" + section.GetProperty("title").ToString( ) + "]";
                    pages = sectionEpisodes;
                    break;
                }
            }
        }

        var pagesInfo = BuildEpisodePages(pages);
        if (pagesInfo.Count == 0)
        {
            throw new InvalidOperationException("该番剧没有可下载的正片分集。");
        }

        var index = locate ? pagesInfo.Find(p => p.EpId == locateEpId)?.Index.ToString( ) ?? "" : "";

        var info = new VInfo
        {
            Title = title.Trim( ),
            Desc = desc.Trim( ),
            Pic = cover,
            PubTime = pubTime,
            PagesInfo = pagesInfo,
            IsBangumi = true,
            // 番剧不是课程（cheese），原 IsCheese = true 为误设（P1-5）
            // 完结状态从 season 接口的 is_finish 读取，避免 IsBangumiEnd 永远为 false（P1-4）
            IsBangumiEnd = result.TryGetProperty("is_finish", out var f) && f.GetInt32( ) == 1,
            Index = index
        };

        return info;
    }

    // 国内番剧与 INTL 番剧的 episodes 结构一致，共用同一段分集构造；
    // 据此假定 bstar 的 duration 亦为毫秒（该接口在国内不可达，未实测）
    internal static List<Page> BuildEpisodePages(JsonElement episodes)
    {
        List<Page> pagesInfo = [];
        var i = 1;
        foreach (var page in EnumerateArrayOrEmpty(episodes))
        {
            //跳过预告
            if (page.TryGetProperty("badge", out var badge) && badge.ToString( ) == "预告")
            {
                continue;
            }

            pagesInfo.Add(new Page
            {
                Index = i++,
                Aid = page.GetProperty("aid").ToString( ),
                Cid = page.GetProperty("cid").ToString( ),
                EpId = page.GetProperty("id").ToString( ),
                Title = (page.GetProperty("title") + " " + page.GetProperty("long_title")).Trim( ),
                Dur = ReadDurationSeconds(page),
                Res = ReadDimension(page),
                PubTime = page.TryGetProperty("pub_time", out var pubTime) ? pubTime.GetInt64( ) : 0,
            });
        }

        return pagesInfo;
    }
}