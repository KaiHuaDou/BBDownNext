using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core.Entity;

using static BBDown.Core.Entity.Entity;
using static BBDown.Core.Util.HTTPUtil;
using static BBDown.Core.Util.JsonUtil;

namespace BBDown.Core.Fetcher;

public static class CheeseInfoFetcher
{
    public static async Task<VInfo> FetchAsync(string id, AppConfig cfg, CancellationToken ct = default)
    {
        id = id[IdPrefix.Cheese.Length..];
        var api = $"{BiliApi.SeasonPugv}?ep_id={id}";
        var json = await GetWebSourceAsync(api, cfg, null, ct);
        using var infoJson = JsonDocument.Parse(json);
        var data = GetApiData(infoJson.RootElement, "课程信息");
        var cover = data.GetProperty("cover").ToString( );
        var title = data.GetProperty("title").ToString( );
        var desc = data.GetProperty("subtitle").ToString( );
        var ownerName = data.GetProperty("up_info").GetProperty("uname").ToString( );
        var ownerMid = data.GetProperty("up_info").GetProperty("mid").ToString( );
        List<Page> pagesInfo = [];
        foreach (var page in EnumerateArrayOrEmpty(data.GetProperty("episodes")))
        {
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

        var index = pagesInfo.Find(p => p.epid == id)?.index.ToString( ) ?? "";
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
}
