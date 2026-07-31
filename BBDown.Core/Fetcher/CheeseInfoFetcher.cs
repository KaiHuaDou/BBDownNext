using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

using BBDown.Core.Entity;

using static BBDown.Core.Entity.Entity;
using static BBDown.Core.Util.HTTPUtil;

namespace BBDown.Core.Fetcher;

public static class CheeseInfoFetcher
{
    public static async Task<VInfo> FetchAsync(string id, AppConfig cfg)
    {
        id = id[7..];
        var index = "";
        var api = $"https://api.bilibili.com/pugv/view/web/season?ep_id={id}";
        var json = await GetWebSourceAsync(api, cfg);
        using var infoJson = JsonDocument.Parse(json);
        var data = infoJson.RootElement.GetProperty("data");
        var cover = data.GetProperty("cover").ToString( );
        var title = data.GetProperty("title").ToString( );
        var desc = data.GetProperty("subtitle").ToString( );
        var ownerName = data.GetProperty("up_info").GetProperty("uname").ToString( );
        var ownerMid = data.GetProperty("up_info").GetProperty("mid").ToString( );
        var pages = data.GetProperty("episodes").EnumerateArray( );
        List<Page> pagesInfo = [];
        foreach (var page in pages)
        {
            Page p = new( )
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
            };
            if (p.epid == id)
            {
                index = p.index.ToString( );
            }

            pagesInfo.Add(p);
        }

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