using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core.Entity;

using static BBDown.Core.Entity.Entity;
using static BBDown.Core.Logger;
using static BBDown.Core.Util.HTTPUtil;
using static BBDown.Core.Util.JsonUtil;

namespace BBDown.Core.Fetcher;

public static class CheeseInfoFetcher
{
    public static async Task<VInfo> FetchAsync(string id, AppConfig cfg, CancellationToken ct = default)
    {
        // id 去掉 "cheese:" 前缀后有两种形态：
        //   - 纯数字 ep_id（如 "790"）：直接按 ep_id 拉整季；
        //   - 带 "ss" 前缀的 season_id（如 "ss61"）：按 season_id 拉整季（由 InputResolver 在 cheese/ss 输入时产出）。
        // 两者都返回整季分集，区别仅在于前者能直接定位「当前选择第几集」。
        var raw = id[IdPrefix.Cheese.Length..];
        var api = raw.StartsWith("ss")
            ? $"{BiliApi.SeasonPugv}?season_id={raw[2..]}"
            : $"{BiliApi.SeasonPugv}?ep_id={raw}";
        var json = await GetWebSourceAsync(api, cfg, null, ct);
        using var infoJson = JsonDocument.Parse(json);
        var data = GetApiData(infoJson.RootElement, "课程信息");
        var cover = data.GetProperty("cover").ToString( );
        var title = data.GetProperty("title").ToString( );
        var desc = data.GetProperty("subtitle").ToString( );
        var ownerName = data.GetProperty("up_info").GetProperty("uname").ToString( );
        var ownerMid = data.GetProperty("up_info").GetProperty("mid").ToString( );
        var pagesInfo = BuildPages(data.GetProperty("episodes"), ownerName, ownerMid);
        if (pagesInfo.Count == 0)
        {
            throw new InvalidOperationException("该课程没有可下载的分集（可能尚未购买，或分集均为试看锁定）。");
        }

        var index = raw.StartsWith("ss") ? "" : pagesInfo.Find(p => p.epid == raw)?.index.ToString( ) ?? "";
        var pubTime = pagesInfo.Count != 0 ? pagesInfo[0].pubTime : 0;

        var info = new VInfo
        {
            Title = title.Trim( ),
            Desc = desc.Trim( ),
            Pic = cover,
            PubTime = pubTime,
            PagesInfo = pagesInfo,
            IsBangumi = true,
            IsCheese = true,
            Index = index
        };

        return info;
    }

    // 课程分集 → Page。episodes[].status：1 可观看 / 2 不可观看（未购买或锁定），跳过不可观看分集。
    // 抽为纯函数便于单测（状态过滤 + 字段映射）。
    internal static List<Page> BuildPages(JsonElement episodes, string ownerName, string ownerMid)
    {
        List<Page> pagesInfo = [];
        foreach (var page in EnumerateArrayOrEmpty(episodes))
        {
            if (page.TryGetProperty("status", out var status) && status.GetInt32( ) == 2)
            {
                var lockedTitle = page.TryGetProperty("title", out var t) ? t.GetString( ) : "(未知)";
                LogWarn($"跳过不可观看的课程分集：{lockedTitle}");
                continue;
            }

            pagesInfo.Add(new Page
            {
                index = page.GetProperty("index").GetInt32( ),
                aid = page.GetProperty("aid").ToString( ),
                cid = page.GetProperty("cid").ToString( ),
                epid = page.GetProperty("id").ToString( ),
                title = page.GetProperty("title").ToString( ).Trim( ),
                dur = page.GetProperty("duration").GetInt32( ),
                res = "",
                pubTime = page.GetProperty("release_date").GetInt64( ),
                cover = "",
                desc = "",
                ownerName = ownerName,
                ownerMid = ownerMid,
            });
        }

        return pagesInfo;
    }
}
