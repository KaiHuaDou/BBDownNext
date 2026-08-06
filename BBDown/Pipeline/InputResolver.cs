using System;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using BBDown.Core;

using static BBDown.Core.Util.HTTPUtil;
using static BBDown.Util.Utils;

namespace BBDown.Pipeline;

/// <summary>
/// 把用户输入（URL / av / BV / ep / ss / 合集 / 系列 / 收藏 / 空间等）解析为内部统一的 avid 字符串。
/// </summary>
internal static partial class InputResolver
{
    public static async Task<string> GetAvIdAsync(string input, Core.AppConfig cfg)
    {
        var avid = input.StartsWith("http")
            ? await ResolveUrlAsync(input, cfg)
            : await ResolveShorthandAsync(input, cfg);
        return await FixAvidAsync(avid);
    }

    private static async Task<string> ResolveUrlAsync(string input, Core.AppConfig cfg)
    {
        if (input.Contains("b23.tv"))
        {
            var tmp = await GetWebLocationAsync(input);
            if (tmp == input)
            {
                throw new InvalidOperationException("无限重定向");
            }

            input = tmp;
        }

        if (input.Contains("video/av"))
        {
            return AvRegex( ).Match(input).Groups[1].Value;
        }

        if (input.ToLower( ).Contains("video/bv"))
        {
            return GetAidByBV(BVRegex( ).Match(input).Groups[1].Value);
        }

        // 稍后再看页：/watchlater/、/watchlater/#/list、/list/watchlater 等形态。
        // 分享链接携带 bvid/oid 参数指向单个视频时只下载该视频（bvid 优先，本地解码），否则按整个列表处理。
        if (input.Contains("/watchlater"))
        {
            var bvid = GetQueryString("bvid", input);
            if (bvid.Length > 0)
            {
                return GetAidByBV(BVRegex( ).Match(bvid).Groups[1].Value);
            }

            var oid = GetQueryString("oid", input);
            return oid.Length > 0 ? oid : IdPrefix.WatchLater;
        }

        if (input.Contains("/cheese/"))
        {
            return await ResolveCheeseAsync(input);
        }

        if (input.Contains("/ep"))
        {
            return $"ep:{EpRegex( ).Match(input).Groups[1].Value}";
        }

        if (input.Contains("/ss"))
        {
            return $"ep:{await GetSeasonIdBySSAsync(SsRegex( ).Match(input).Groups[1].Value, cfg)}";
        }

        if (input.Contains("/medialist/") && input.Contains("business_id=") && input.Contains("business=space_collection")) // 列表类型是合集
        {
            return $"listBizId:{GetQueryString("business_id", input)}";
        }

        if (input.Contains("/medialist/") && input.Contains("business_id=") && input.Contains("business=space_series")) // 列表类型是系列
        {
            return $"seriesBizId:{GetQueryString("business_id", input)}";
        }

        if (input.Contains("/channel/collectiondetail?sid="))
        {
            return $"listBizId:{GetQueryString("sid", input)}";
        }

        if (input.Contains("/channel/seriesdetail?sid="))
        {
            return $"seriesBizId:{GetQueryString("sid", input)}";
        }

        if (input.Contains("/space.bilibili.com/") && input.Contains("/lists/"))
        {
            return ResolveSpaceList(input);
        }

        if (input.Contains("/space.bilibili.com/") && input.Contains("/favlist"))
        {
            return $"{IdPrefix.FavId}{GetQueryString("fid", input)}:{UidRegex( ).Match(input).Groups[1].Value}";
        }

        if (input.Contains("/space.bilibili.com/"))
        {
            // 空间首页 / /upload/video / /video?tid=0 等子路径统一按「该 UP 全部投稿」处理
            return $"{IdPrefix.SpaceMid}{UidRegex( ).Match(input).Groups[1].Value}";
        }

        if (input.Contains("ep_id="))
        {
            return $"{IdPrefix.EpColon}{GetQueryString("ep_id", input)}";
        }

        if (GlobalEpRegex( ).Match(input) is { Success: true } globalEp)
        {
            return $"{IdPrefix.EpColon}{globalEp.Groups[1].Value}";
        }

        if (BangumiMdRegex( ).Match(input) is { Success: true } md)
        {
            return $"{IdPrefix.EpColon}{await GetSeasonIdByMDAsync(md.Groups[1].Value, cfg)}";
        }

        return $"{IdPrefix.EpColon}{await ScrapeFirstEpIdAsync(input, cfg)}";
    }

    private static async Task<string> ResolveShorthandAsync(string input, Core.AppConfig cfg)
    {
        if (input.ToLower( ).StartsWith("bv"))
        {
            return GetAidByBV(input[IdPrefix.Bv.Length..]);
        }

        if (input.ToLower( ).StartsWith(IdPrefix.Av))
        {
            return input.ToLower( )[IdPrefix.Av.Length..];
        }

        if (input.StartsWith(IdPrefix.CheeseSlash)) // ^cheese/(ep|ss)\d+ 格式
        {
            return await ResolveCheeseAsync(input);
        }

        if (input.StartsWith(IdPrefix.Ep))
        {
            return $"{IdPrefix.EpColon}{input[IdPrefix.Ep.Length..]}";
        }

        if (input.StartsWith(IdPrefix.Ss))
        {
            return $"{IdPrefix.EpColon}{await GetSeasonIdBySSAsync(input[IdPrefix.Ss.Length..], cfg)}";
        }

        if (input.StartsWith(IdPrefix.Md))
        {
            return $"{IdPrefix.EpColon}{await GetSeasonIdByMDAsync(MdRegex( ).Match(input).Groups[1].Value, cfg)}";
        }

        // space402787936：显式空间简写（先判 space 再判裸数字，避免裸数字分支误吞）
        if (input.ToLower( ).StartsWith("space") && input["space".Length..] is { Length: > 0 } spaceRest && spaceRest.All(char.IsDigit))
        {
            return $"{IdPrefix.SpaceMid}{spaceRest}";
        }

        // 裸数字：此前一律抛「输入有误」，改判为 ep 号（av 号必须带 av 前缀，无回归风险）
        if (input.Length > 0 && input.All(char.IsDigit))
        {
            return $"{IdPrefix.EpColon}{input}";
        }

        throw new ArgumentException("输入有误", nameof(input));
    }

    // 课程（cheese）解析：纯字符串，不触网。
    // ep 形式直接取 ep_id；ss 形式保留 season_id（以 "ss" 前缀标记），交由 CheeseInfoFetcher 按 season_id 直接拉取整季，
    // 避免旧实现「先请求一次接口取首集 ep_id、再请求一次拉整季」的冗余往返（见 cheese-review 的 S1/C1）。
    private static Task<string> ResolveCheeseAsync(string input)
    {
        if (input.Contains("/ep"))
        {
            return Task.FromResult($"{IdPrefix.Cheese}{EpRegex( ).Match(input).Groups[1].Value}");
        }

        if (input.Contains("/ss"))
        {
            return Task.FromResult($"{IdPrefix.Cheese}ss{SsRegex( ).Match(input).Groups[1].Value}");
        }

        return Task.FromResult($"{IdPrefix.Cheese}{EpRegex( ).Match(input).Groups[1].Value}");
    }

    // 新版个人空间合集/系列链接：
    //   合集：https://space.bilibili.com/392959666/lists/1560264?type=season
    //   系列：https://space.bilibili.com/392959666/lists/1560264?type=series
    private static string ResolveSpaceList(string input)
    {
        // path 最后一个 / 后到 ? 前即为 sid
        var path = input.Split('?', '#')[0];
        var sid = path[(path.LastIndexOf('/') + 1)..];
        var type = GetQueryString("type", input).ToLower( );
        // 未知类型按合集处理，至少不会识别失败
        return type == "series" ? $"seriesBizId:{sid}" : $"listBizId:{sid}";
    }

    private static async Task<string> ScrapeFirstEpIdAsync(string input, Core.AppConfig cfg)
    {
        var web = await GetWebSourceAsync(input, cfg);
        var json = StateRegex( ).Match(web).Groups[1].Value;
        using var jDoc = JsonDocument.Parse(json);
        return jDoc.RootElement.GetProperty("epList").EnumerateArray( ).First( ).GetProperty("id").ToString( );
    }

    private static async Task<string> FixAvidAsync(string avid)
    {
        if (!avid.All(char.IsDigit))
        {
            return avid;
        }

        var api = $"{BiliApi.VideoPage}/av{avid}/";
        var location = await GetWebLocationAsync(api);
        return location.Contains("/ep") ? $"ep:{EpRegex( ).Match(location).Groups[1].Value}" : avid;
    }

    private static string GetAidByBV(string bv)
    {
        // 能在本地就在本地
        return Core.Util.BilibiliBvConverter.Decode(bv).ToString( );
    }

    // ss（番剧季号）直接解析为 season_id 编码，产出 "ss{seasonId}" 形态，
    // 与 md 路径完全对称：同样交由 BangumiInfoFetcher 按 season_id 拉取整季正片（Index=""）。
    // 这样 ss / md 两种入口得到完全一致的内部 id（ep:ss{season_id}），无特判、零跨层改动。
    private static async Task<string> GetSeasonIdBySSAsync(string ssId, Core.AppConfig cfg)
    {
        var api = $"https://{cfg.EpHost}{BiliApi.SeasonPgcPath}?season_id={ssId}";
        var json = await GetWebSourceAsync(api, cfg);
        using var jDoc = JsonDocument.Parse(json);
        var result = BBDown.Core.Util.JsonUtil.GetApiData(jDoc.RootElement, "番剧信息", "result");
        return $"ss{result.GetProperty("season_id")}";
    }

    // md（番剧详情页 id）本质是 media_id，需经 pgc/review/user 映射出 season_id。
    // 返回 "ss{seasonId}" 形态，交由 BangumiInfoFetcher 按 season_id 拉取整季正片，
    // 与 cheese 的 ss 形态编码保持一致，从而无需新增内部 id 前缀、playurl 判定零改动。
    // 旧实现取 new_ep.id（最新一集）改为整季，用户可用 -p 选定具体集。
    private static async Task<string> GetSeasonIdByMDAsync(string mdId, Core.AppConfig cfg)
    {
        var api = $"{BiliApi.ReviewUser}?media_id={mdId}";
        var json = await GetWebSourceAsync(api, cfg);
        using var jDoc = JsonDocument.Parse(json);
        var media = BBDown.Core.Util.JsonUtil.GetApiData(jDoc.RootElement, "番剧信息", "result").GetProperty("media");
        return $"ss{media.GetProperty("season_id")}";
    }

    [GeneratedRegex("av(\\d+)")]
    private static partial Regex AvRegex( );
    [GeneratedRegex("[Bb][Vv]1(\\w+)")]
    private static partial Regex BVRegex( );
    [GeneratedRegex("/ep(\\d+)")]
    private static partial Regex EpRegex( );
    [GeneratedRegex("/ss(\\d+)")]
    private static partial Regex SsRegex( );
    [GeneratedRegex(@"space\.bilibili\.com/(\d+)")]
    private static partial Regex UidRegex( );
    [GeneratedRegex(@"\.bilibili\.tv\/\w+\/play\/\d+\/(\d+)")]
    private static partial Regex GlobalEpRegex( );
    [GeneratedRegex("bangumi/media/md(\\d+)")]
    private static partial Regex BangumiMdRegex( );
    [GeneratedRegex(@"window.__INITIAL_STATE__=([\s\S].*?);\(function\(\)")]
    private static partial Regex StateRegex( );
    [GeneratedRegex("md(\\d+)")]
    private static partial Regex MdRegex( );
}
