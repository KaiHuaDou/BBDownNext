using System;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core.Entity;

using static BBDown.Core.ResourceId;
using static BBDown.Core.Util.HTTPUtil;
using static BBDown.Core.Util.JsonUtil;

namespace BBDown.Core.Fetcher;

public static partial class IntlBangumiInfoFetcher
{
    public static async Task<VInfo> FetchAsync(Ep ep, AppConfig cfg, CancellationToken ct = default)
    {
        var id = ep.EpId.ToString( );
        var host = cfg.Host == BiliApi.MainHost ? BiliApi.IntlAppHost : cfg.Host;
        var accessKey = cfg.Token.Length != 0 ? $"&access_key={cfg.Token}" : "";
        var api = $"https://{host}{BiliApi.IntlSeasonAppPath}?ep_id={id}&platform=android&s_locale=zh_SG&mobi_app=bstar_a{accessKey}";
        var json = (await GetWebSourceAsync(api, cfg, null, ct)).Replace("\\/", "/");
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
            var web = await GetWebSourceAsync(animeUrl, cfg, null, ct);
            if (web.Length != 0)
            {
                var regex = StateRegex( );
                var m = regex.Match(web);
                if (m.Success && m.Groups.Count > 1)
                {
                    var _json = m.Groups[1].Value;
                    using var _tempJson = JsonDocument.Parse(_json);
                    cover = _tempJson.RootElement.GetProperty("mediaInfo").GetProperty("cover").ToString( );
                    title = _tempJson.RootElement.GetProperty("mediaInfo").GetProperty("title").ToString( );
                    desc = _tempJson.RootElement.GetProperty("mediaInfo").GetProperty("evaluate").ToString( );
                }
            }
        }

        var pubTimeStr = result.GetProperty("publish").GetProperty("pub_time").ToString( );
        // 同 BangumiInfoFetcher：pub_time 格式固定为公历，非公历 locale 下必须用 InvariantCulture 解析
        var pubTime = string.IsNullOrEmpty(pubTimeStr) ? 0 : DateTimeOffset.ParseExact(pubTimeStr, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture).ToUnixTimeSeconds( );
        TryGetArray(result, "episodes", out var pages);

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

        var pagesInfo = BangumiInfoFetcher.BuildEpisodePages(pages);
        var index = pagesInfo.Find(p => p.EpId == id)?.Index.ToString( ) ?? "";

        var info = new VInfo
        {
            Title = title.Trim( ),
            Desc = desc.Trim( ),
            Pic = cover,
            PubTime = pubTime,
            PagesInfo = pagesInfo,
            IsBangumi = true,
            // 国际版番剧同样不是课程（cheese），原 IsCheese = true 为误设（P1-5）
            IsBangumiEnd = result.TryGetProperty("is_finish", out var f) && f.GetInt32( ) == 1,
            Index = index
        };

        return info;
    }

    [GeneratedRegex("window.__INITIAL_STATE__=([\\s\\S].*?);\\(function\\(\\)")]
    private static partial Regex StateRegex( );
}
