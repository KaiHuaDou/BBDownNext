using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core.Entity;

using static BBDown.Core.Entity.Entity;
using static BBDown.Core.Util.HTTPUtil;
using static BBDown.Core.Util.JsonUtil;

namespace BBDown.Core.Fetcher;

public static class BangumiInfoFetcher
{
    public static async Task<VInfo> FetchAsync(string id, AppConfig cfg, CancellationToken ct = default)
    {
        id = id[IdPrefix.EpColon.Length..];
        var api = $"https://{cfg.EpHost}{BiliApi.SeasonPgcPath}?ep_id={id}";
        var json = await GetWebSourceAsync(api, cfg, null, ct);
        using var infoJson = JsonDocument.Parse(json);
        if (!infoJson.RootElement.TryGetProperty("result", out var result))
        {
            throw new BangumiNotFoundException($"未找到 EP/SS 对应的番剧信息：ep_id={id}");
        }

        var cover = result.GetProperty("cover").ToString( );
        var title = result.GetProperty("title").ToString( );
        var desc = result.GetProperty("evaluate").ToString( );
        var pubTimeStr = result.GetProperty("publish").GetProperty("pub_time").ToString( );
        var pubTime = string.IsNullOrEmpty(pubTimeStr) ? 0 : DateTimeOffset.ParseExact(pubTimeStr, "yyyy-MM-dd HH:mm:ss", null).ToUnixTimeSeconds( );
        TryGetArray(result, "episodes", out var pages);

        //episodes为空; 或者未包含对应epid，番外/花絮什么的
        if (!ContainsEpisode(pages, id) && TryGetArray(result, "section", out var sections))
        {
            foreach (var section in sections.EnumerateArray( ))
            {
                if (TryGetArray(section, "episodes", out var sectionEpisodes) && ContainsEpisode(sectionEpisodes, id))
                {
                    title += "[" + section.GetProperty("title").ToString( ) + "]";
                    pages = sectionEpisodes;
                    break;
                }
            }
        }

        var pagesInfo = BuildEpisodePages(pages);
        var index = pagesInfo.Find(p => p.epid == id)?.index.ToString( ) ?? "";

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

    // 国内番剧与 INTL 番剧的 episodes 结构一致，共用同一段分集构造
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
                index = i++,
                aid = page.GetProperty("aid").ToString( ),
                cid = page.GetProperty("cid").ToString( ),
                epid = page.GetProperty("id").ToString( ),
                title = (page.GetProperty("title").ToString( ) + " " + page.GetProperty("long_title").ToString( )).Trim( ),
                dur = 0,
                res = ReadDimension(page),
                pubTime = page.TryGetProperty("pub_time", out var pubTime) ? pubTime.GetInt64( ) : 0,
            });
        }

        return pagesInfo;
    }
}
