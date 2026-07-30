using System.Text.Json;

using BBDown.Core.Entity;

using static BBDown.Core.Entity.Entity;
using static BBDown.Core.Util.HTTPUtil;

namespace BBDown.Core.Fetcher;

public static class BangumiInfoFetcher
{
    public static async Task<VInfo> FetchAsync(string id, AppConfig cfg)
    {
        id = id[3..];
        var index = "";
        var api = $"https://{cfg.EpHost}/pgc/view/web/season?ep_id={id}";
        var json = await GetWebSourceAsync(api, cfg);
        using var infoJson = JsonDocument.Parse(json);
        JsonElement result = infoJson.RootElement.GetProperty("result");
        var cover = result.GetProperty("cover").ToString( );
        var title = result.GetProperty("title").ToString( );
        var desc = result.GetProperty("evaluate").ToString( );
        var pubTimeStr = result.GetProperty("publish").GetProperty("pub_time").ToString( );
        var pubTime = string.IsNullOrEmpty(pubTimeStr) ? 0 : DateTimeOffset.ParseExact(pubTimeStr, "yyyy-MM-dd HH:mm:ss", null).ToUnixTimeSeconds( );
        JsonElement.ArrayEnumerator pages = result.GetProperty("episodes").EnumerateArray( );
        List<Page> pagesInfo = [];
        var i = 1;

        //episodes为空; 或者未包含对应epid，番外/花絮什么的
        if (!(pages.Any( ) && result.GetProperty("episodes").ToString( ).Contains($"/ep{id}")))
        {
            if (result.TryGetProperty("section", out JsonElement sections))
            {
                foreach (JsonElement section in sections.EnumerateArray( ))
                {
                    if (section.ToString( ).Contains($"/ep{id}"))
                    {
                        title += "[" + section.GetProperty("title").ToString( ) + "]";
                        pages = section.GetProperty("episodes").EnumerateArray( );
                        break;
                    }
                }
            }
        }

        foreach (JsonElement page in pages)
        {
            //跳过预告
            if (page.TryGetProperty("badge", out JsonElement badge) && badge.ToString( ) == "预告") continue;
            var res = "";
            try
            {
                res = page.GetProperty("dimension").GetProperty("width").ToString( ) + "x" + page.GetProperty("dimension").GetProperty("height").ToString( );
            }
            catch (Exception) { }

            var _title = page.GetProperty("title").ToString( ) + " " + page.GetProperty("long_title").ToString( );
            _title = _title.Trim( );
            Page p = new( )
            {
                index = i++,
                aid = page.GetProperty("aid").ToString( ),
                cid = page.GetProperty("cid").ToString( ),
                epid = page.GetProperty("id").ToString( ),
                title = _title,
                dur = 0,
                res = res,
                pubTime = page.GetProperty("pub_time").GetInt64( ),
            };
            if (p.epid == id) index = p.index.ToString( );
            pagesInfo.Add(p);
        }

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