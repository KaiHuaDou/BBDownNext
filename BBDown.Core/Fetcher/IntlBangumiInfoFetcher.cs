using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using BBDown.Core.Entity;

using static BBDown.Core.Entity.Entity;
using static BBDown.Core.Util.HTTPUtil;
using static BBDown.Core.Util.JsonUtil;

namespace BBDown.Core.Fetcher;

public static partial class IntlBangumiInfoFetcher
{
    public static async Task<VInfo> FetchAsync(string id, AppConfig cfg)
    {
        id = id[3..];
        var index = "";
        var host = cfg.Host == BiliApi.MainHost ? BiliApi.IntlAppHost : cfg.Host;
        var accessKey = cfg.Token.Length != 0 ? $"&access_key={cfg.Token}" : "";
        var api = $"https://{host}{BiliApi.IntlSeasonAppPath}?ep_id={id}&platform=android&s_locale=zh_SG&mobi_app=bstar_a{accessKey}";
        var json = (await GetWebSourceAsync(api, cfg)).Replace("\\/", "/");
        using var infoJson = JsonDocument.Parse(json);
        if (!infoJson.RootElement.TryGetProperty("result", out var result))
        {
            throw new BangumiNotFoundException($"未找到 EP/SS 对应的番剧信息：ep_id={id}");
        }
        var seasonId = result.GetProperty("season_id").ToString( );
        var cover = result.GetProperty("cover").ToString( );
        var title = result.GetProperty("title").ToString( );
        var desc = result.GetProperty("evaluate").ToString( );

        if (cover.Length == 0)
        {
            var animeUrl = $"{BiliApi.AnimePage}/{seasonId}";
            var web = await GetWebSourceAsync(animeUrl, cfg);
            if (web.Length != 0)
            {
                var regex = StateRegex( );
                var _json = regex.Match(web).Groups[1].Value;
                using var _tempJson = JsonDocument.Parse(_json);
                cover = _tempJson.RootElement.GetProperty("mediaInfo").GetProperty("cover").ToString( );
                title = _tempJson.RootElement.GetProperty("mediaInfo").GetProperty("title").ToString( );
                desc = _tempJson.RootElement.GetProperty("mediaInfo").GetProperty("evaluate").ToString( );
            }
        }

        var pubTimeStr = result.GetProperty("publish").GetProperty("pub_time").ToString( );
        var pubTime = string.IsNullOrEmpty(pubTimeStr) ? 0 : DateTimeOffset.ParseExact(pubTimeStr, "yyyy-MM-dd HH:mm:ss", null).ToUnixTimeSeconds( );
        TryGetArray(result, "episodes", out var pages);
        List<Page> pagesInfo = [];
        var i = 1;

        //目标 ep 可能不在主 episodes 里，而在某个 module 的分组下
        if (TryGetArray(result, "modules", out var modules))
        {
            foreach (var section in modules.EnumerateArray( ))
            {
                if (section.TryGetProperty("data", out var data)
                    && TryGetArray(data, "episodes", out var sectionEpisodes)
                    && ContainsEpisode(sectionEpisodes, id))
                {
                    pages = sectionEpisodes;
                    break;
                }
            }
        }

        foreach (var page in EnumerateArrayOrEmpty(pages))
        {
            //跳过预告
            if (page.TryGetProperty("badge", out var badge) && badge.ToString( ) == "预告")
            {
                continue;
            }

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
                pubTime = page.TryGetProperty("pub_time", out var pub_time) ? pub_time.GetInt64( ) : 0,
            };
            if (p.epid == id)
            {
                index = p.index.ToString( );
            }

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