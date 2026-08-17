using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core.Entity;

using static BBDown.Core.Logger;
using static BBDown.Core.ResourceId;
using static BBDown.Core.Util.HTTPUtil;
using static BBDown.Core.Util.JsonUtil;

namespace BBDown.Core.Fetcher;

public static class CheeseInfoFetcher
{
    public static Task<VInfo> FetchAsync(CheeseEp ep, AppConfig cfg, CancellationToken ct = default)
    {
        return FetchCoreAsync($"{BiliApi.SeasonPugv}?ep_id={ep.EpId}", ep.EpId.ToString( ), locate: true, cfg, ct);
    }

    public static Task<VInfo> FetchAsync(CheeseSeason season, AppConfig cfg, CancellationToken ct = default)
    {
        return FetchCoreAsync($"{BiliApi.SeasonPugv}?season_id={season.SeasonId}", "", locate: false, cfg, ct);
    }

    // ep 形态返回整季分集并按 ep_id 定位「当前选择第几集」；season 形态（cheese/ss 输入）按 season_id 拉整季，无需定位
    private static async Task<VInfo> FetchCoreAsync(string api, string locateEpId, bool locate, AppConfig cfg, CancellationToken ct)
    {
        var json = await GetWebSourceAsync(api, cfg, null, ct);
        using var infoJson = JsonDocument.Parse(json);
        var data = GetApiData(infoJson.RootElement, "课程信息");
        var cover = data.GetProperty("cover").ToString( );
        var title = data.GetProperty("title").ToString( );
        var desc = data.GetProperty("subtitle").ToString( );
        // up_info 偶发缺失（接口异常），缺失时退化为空 UP 主信息而非抛 KeyNotFoundException。
        // 缺失时 TryGetProperty 会把 out 置为默认 JsonElement，必须先用 ValueKind 短路，否则后续调用会抛异常。
        data.TryGetProperty("up_info", out var upInfo);
        var ownerName = upInfo.ValueKind == JsonValueKind.Object && upInfo.TryGetProperty("uname", out var uname)
            ? uname.GetString( ) ?? ""
            : "";
        var ownerMid = upInfo.ValueKind == JsonValueKind.Object && upInfo.TryGetProperty("mid", out var mid)
            ? mid.GetString( ) ?? ""
            : "";
        var pagesInfo = BuildPages(data.GetProperty("episodes"), ownerName, ownerMid);
        if (pagesInfo.Count == 0)
        {
            throw new InvalidOperationException("该课程没有可下载的分集（可能尚未购买，或分集均为试看锁定）。");
        }

        var index = locate ? pagesInfo.Find(p => p.EpId == locateEpId)?.Index.ToString( ) ?? "" : "";
        var pubTime = pagesInfo.Count != 0 ? pagesInfo[0].PubTime : 0;

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
                Index = page.GetProperty("index").GetInt32( ),
                Aid = page.GetProperty("aid").ToString( ),
                Cid = page.GetProperty("cid").ToString( ),
                EpId = page.GetProperty("id").ToString( ),
                Title = page.GetProperty("title").ToString( ).Trim( ),
                Dur = page.GetProperty("duration").GetInt32( ),
                Res = "",
                PubTime = page.GetProperty("release_date").GetInt64( ),
                Cover = "",
                Desc = "",
                OwnerName = ownerName,
                OwnerMid = ownerMid,
            });
        }

        return pagesInfo;
    }
}
