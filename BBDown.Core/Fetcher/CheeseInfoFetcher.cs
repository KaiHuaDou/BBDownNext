using System.Text.Json;

using BBDown.Core.Entity;

using static BBDown.Core.Entity.Entity;
using static BBDown.Core.Util.HTTPUtil;

namespace BBDown.Core.Fetcher;

public class CheeseInfoFetcher : IFetcher
{
    public async Task<VInfo> FetchAsync(string id)
    {
        id = id[7..];
        var index = "";
        var api = $"https://api.bilibili.com/pugv/view/web/season?ep_id={id}";
        var json = await GetWebSourceAsync(api);
        using var infoJson = JsonDocument.Parse(json);
        JsonElement data = infoJson.RootElement.GetProperty("data");
        var cover = data.GetProperty("cover").ToString( );
        var title = data.GetProperty("title").ToString( );
        var desc = data.GetProperty("subtitle").ToString( );
        var ownerName = data.GetProperty("up_info").GetProperty("uname").ToString( );
        var ownerMid = data.GetProperty("up_info").GetProperty("mid").ToString( );
        JsonElement.ArrayEnumerator pages = data.GetProperty("episodes").EnumerateArray( );
        List<Page> pagesInfo = [];
        foreach (JsonElement page in pages)
        {
            Page p = new(page.GetProperty("index").GetInt32( ),
                page.GetProperty("aid").ToString( ),
                page.GetProperty("cid").ToString( ),
                page.GetProperty("id").ToString( ),
                page.GetProperty("title").ToString( ).Trim( ),
                page.GetProperty("duration").GetInt32( ),
                "",
                page.GetProperty("release_date").GetInt64( ),
                "",
                "",
                ownerName,
                ownerMid);
            if (p.epid == id) index = p.index.ToString( );
            pagesInfo.Add(p);
        }

        var pubTime = pagesInfo.Any( ) ? pagesInfo[0].pubTime : 0;

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