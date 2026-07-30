using System.Text.Json;
using System.Text.RegularExpressions;

using BBDown.Core.Entity;

using static BBDown.Core.Entity.Entity;
using static BBDown.Core.Util.HTTPUtil;

namespace BBDown.Core.Fetcher;

public partial class IntlBangumiInfoFetcher : IFetcher
{
    public async Task<VInfo> FetchAsync(string id, AppConfig cfg)
    {
        id = id[3..];
        var index = "";
        //string api = $"https://api.global.bilibili.com/intl/gateway/ogv/m/view?ep_id={id}";
        var api = "https://" + (cfg.Host == "api.bilibili.com" ? "api.bilibili.tv" : cfg.Host) +
                     $"/intl/gateway/v2/ogv/view/app/season?ep_id={id}&platform=android&s_locale=zh_SG&mobi_app=bstar_a" + (cfg.Token != "" ? $"&access_key={cfg.Token}" : "");
        var json = (await GetWebSourceAsync(api, cfg)).Replace("\\/", "/");
        using var infoJson = JsonDocument.Parse(json);
        JsonElement result = infoJson.RootElement.GetProperty("result");
        var seasonId = result.GetProperty("season_id").ToString( );
        var cover = result.GetProperty("cover").ToString( );
        var title = result.GetProperty("title").ToString( );
        var desc = result.GetProperty("evaluate").ToString( );

        if (cover == "")
        {
            var animeUrl = $"https://bangumi.bilibili.com/anime/{seasonId}";
            var web = await GetWebSourceAsync(animeUrl, cfg);
            if (web != "")
            {
                Regex regex = StateRegex( );
                var _json = regex.Match(web).Groups[1].Value;
                using var _tempJson = JsonDocument.Parse(_json);
                cover = _tempJson.RootElement.GetProperty("mediaInfo").GetProperty("cover").ToString( );
                title = _tempJson.RootElement.GetProperty("mediaInfo").GetProperty("title").ToString( );
                desc = _tempJson.RootElement.GetProperty("mediaInfo").GetProperty("evaluate").ToString( );
            }
        }

        var pubTimeStr = result.GetProperty("publish").GetProperty("pub_time").ToString( );
        var pubTime = string.IsNullOrEmpty(pubTimeStr) ? 0 : DateTimeOffset.ParseExact(pubTimeStr, "yyyy-MM-dd HH:mm:ss", null).ToUnixTimeSeconds( );
        var pages = new List<JsonElement>( );
        if (result.TryGetProperty("episodes", out JsonElement episodes))
        {
            pages = episodes.EnumerateArray( ).ToList( );
        }

        List<Page> pagesInfo = [];
        var i = 1;

        if (result.TryGetProperty("modules", out JsonElement modules))
        {
            foreach (JsonElement section in modules.EnumerateArray( ))
            {
                if (section.ToString( ).Contains($"/{id}"))
                {
                    pages = section.GetProperty("data").GetProperty("episodes").EnumerateArray( ).ToList( );
                    break;
                }
            }
        }

        /*if (pages.Count == 0)
        {
            if (web != "")
            {
                string epApi = $"https://api.bilibili.com/pgc/web/season/section?season_id={seasonId}";
                var _web = GetWebSource(epApi);
                pages = JArray.Parse(JObject.Parse(_web)["result"]["main_section"]["episodes"].ToString());
            }
            else if (infoJson["data"]["modules"] != null)
            {
                foreach (JObject section in JArray.Parse(infoJson["data"]["modules"].ToString()))
                {
                    if (section.ToString().Contains($"ep_id={id}"))
                    {
                        pages = JArray.Parse(section["data"]["episodes"].ToString());
                        break;
                    }
                }
            }
        }*/

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
            Page p = new(i++,
                page.GetProperty("aid").ToString( ),
                page.GetProperty("cid").ToString( ),
                page.GetProperty("id").ToString( ),
                _title,
                0, res,
                page.TryGetProperty("pub_time", out JsonElement pub_time) ? pub_time.GetInt64( ) : 0);
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

    [GeneratedRegex("window.__INITIAL_STATE__=([\\s\\S].*?);\\(function\\(\\)")]
    private static partial Regex StateRegex( );
}