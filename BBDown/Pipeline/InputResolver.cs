using System;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using BBDown.Core;
using BBDown.Core.Util;

using static BBDown.Core.ResourceId;
using static BBDown.Core.Util.HTTPUtil;
using static BBDown.Util.Utils;

namespace BBDown.Pipeline;

/// <summary>
/// 把用户输入（URL / av / BV / ep / ss / 合集 / 系列 / 收藏 / 空间等）解析为内部统一的 <see cref="ResourceId"/>。
/// </summary>
internal static partial class InputResolver
{
    public static async Task<ResourceId> ResolveIdAsync(string input, Core.AppConfig cfg)
    {
        var id = input.StartsWith("http")
            ? await ResolveUrlAsync(input, cfg)
            : await ResolveShorthandAsync(input, cfg);
        return await FixAvidAsync(id);
    }

    private static async Task<ResourceId> ResolveUrlAsync(string input, Core.AppConfig cfg)
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
            return new Av(long.Parse(AvRegex( ).Match(input).Groups[1].Value));
        }

        if (input.Contains("video/bv", StringComparison.OrdinalIgnoreCase))
        {
            return new Av(BilibiliBvConverter.Decode(BVRegex( ).Match(input).Groups[1].Value));
        }

        // 稍后再看页：/watchlater/、/watchlater/#/list、/list/watchlater 等形态。
        // 分享链接携带 bvid/oid 参数指向单个视频时只下载该视频（bvid 优先，本地解码），否则按整个列表处理。
        if (input.Contains("/watchlater"))
        {
            var bvid = GetQueryString("bvid", input);
            if (bvid.Length > 0)
            {
                return new Av(BilibiliBvConverter.Decode(BVRegex( ).Match(bvid).Groups[1].Value));
            }

            var oid = GetQueryString("oid", input);
            return oid.Length > 0 ? new Av(long.Parse(oid)) : new WatchLater( );
        }

        if (input.Contains("/cheese/"))
        {
            return await ResolveCheeseAsync(input);
        }

        if (input.Contains("/ep"))
        {
            return new Ep(long.Parse(EpRegex( ).Match(input).Groups[1].Value));
        }

        if (input.Contains("/ss"))
        {
            return new Season(await GetSeasonIdBySSAsync(SsRegex( ).Match(input).Groups[1].Value, cfg));
        }

        if (input.Contains("/medialist/") && input.Contains("business_id=") && input.Contains("business=space_collection")) // 列表类型是合集
        {
            return new MediaList(long.Parse(GetQueryString("business_id", input)));
        }

        if (input.Contains("/medialist/") && input.Contains("business_id=") && input.Contains("business=space_series")) // 列表类型是系列
        {
            return new Series(long.Parse(GetQueryString("business_id", input)));
        }

        if (input.Contains("/channel/collectiondetail?sid="))
        {
            return new MediaList(long.Parse(GetQueryString("sid", input)));
        }

        if (input.Contains("/channel/seriesdetail?sid="))
        {
            return new Series(long.Parse(GetQueryString("sid", input)));
        }

        if (input.Contains("/space.bilibili.com/") && input.Contains("/lists/"))
        {
            return ResolveSpaceList(input);
        }

        if (input.Contains("/space.bilibili.com/") && input.Contains("/favlist"))
        {
            var fid = GetQueryString("fid", input);
            var uid = long.Parse(UidRegex( ).Match(input).Groups[1].Value);
            return new Fav(long.TryParse(fid, out var fidL) ? fidL : 0, uid);
        }

        if (input.Contains("/space.bilibili.com/"))
        {
            // 空间首页 / /upload/video / /video?tid=0 等子路径统一按「该 UP 全部投稿」处理
            return new Space(long.Parse(UidRegex( ).Match(input).Groups[1].Value));
        }

        if (input.Contains("ep_id="))
        {
            return new Ep(long.Parse(GetQueryString("ep_id", input)));
        }

        if (GlobalEpRegex( ).Match(input) is { Success: true } globalEp)
        {
            return new Ep(long.Parse(globalEp.Groups[1].Value));
        }

        if (BangumiMdRegex( ).Match(input) is { Success: true } md)
        {
            return new Season(await GetSeasonIdByMDAsync(md.Groups[1].Value, cfg));
        }

        return new Ep(await ScrapeFirstEpIdAsync(input, cfg));
    }

    private static async Task<ResourceId> ResolveShorthandAsync(string input, Core.AppConfig cfg)
    {
        if (input.Equals("watchlater", StringComparison.OrdinalIgnoreCase))
        {
            return new WatchLater( );
        }

        if (input.StartsWith("bv", StringComparison.OrdinalIgnoreCase))
        {
            return new Av(BilibiliBvConverter.Decode(input[IdPrefix.Bv.Length..]));
        }

        if (input.StartsWith(IdPrefix.Av, StringComparison.OrdinalIgnoreCase))
        {
            return new Av(long.Parse(input[IdPrefix.Av.Length..]));
        }

        if (input.StartsWith(IdPrefix.CheeseSlash)) // ^cheese/(ep|ss)\d+ 格式
        {
            return await ResolveCheeseAsync(input);
        }

        if (input.StartsWith(IdPrefix.Ep))
        {
            return new Ep(long.Parse(input[IdPrefix.Ep.Length..]));
        }

        if (input.StartsWith(IdPrefix.Ss))
        {
            return new Season(await GetSeasonIdBySSAsync(input[IdPrefix.Ss.Length..], cfg));
        }

        if (input.StartsWith(IdPrefix.Md))
        {
            return new Season(await GetSeasonIdByMDAsync(MdRegex( ).Match(input).Groups[1].Value, cfg));
        }

        // space402787936：显式空间简写（先判 space 再判裸数字，避免裸数字分支误吞）
        if (input.StartsWith("space", StringComparison.OrdinalIgnoreCase) && input["space".Length..] is { Length: > 0 } spaceRest && spaceRest.All(char.IsDigit))
        {
            return new Space(long.Parse(spaceRest));
        }

        // 裸数字按 av 号识别；若该 av 实际被重定向到番剧播放页，FixAvidAsync 会探测并转 Ep
        if (input.Length > 0 && input.All(char.IsDigit))
        {
            return new Av(long.Parse(input));
        }

        throw new ArgumentException("输入有误", nameof(input));
    }

    // 课程（cheese）解析：纯字符串，不触网。
    // ep 形式直接取 ep_id；ss 形式保留 season_id，交由 CheeseInfoFetcher 按 season_id 直接拉取整季，
    // 避免旧实现「先请求一次接口取首集 ep_id、再请求一次拉整季」的冗余往返（见 cheese-review 的 S1/C1）。
    private static Task<ResourceId> ResolveCheeseAsync(string input)
    {
        if (input.Contains("/ep"))
        {
            return Task.FromResult<ResourceId>(new CheeseEp(long.Parse(EpRegex( ).Match(input).Groups[1].Value)));
        }

        if (input.Contains("/ss"))
        {
            return Task.FromResult<ResourceId>(new CheeseSeason(long.Parse(SsRegex( ).Match(input).Groups[1].Value)));
        }

        return Task.FromResult<ResourceId>(new CheeseEp(long.Parse(EpRegex( ).Match(input).Groups[1].Value)));
    }

    // 新版个人空间合集/系列链接：
    //   合集：https://space.bilibili.com/392959666/lists/1560264?type=season
    //   系列：https://space.bilibili.com/392959666/lists/1560264?type=series
    private static ResourceId ResolveSpaceList(string input)
    {
        // path 最后一个 / 后到 ? 前即为 sid
        var path = input.Split('?', '#')[0];
        var sid = path[(path.LastIndexOf('/') + 1)..];
        var type = GetQueryString("type", input);
        // 未知类型按合集处理，至少不会识别失败
        return type.Equals("series", StringComparison.OrdinalIgnoreCase)
            ? new Series(long.Parse(sid))
            : new MediaList(long.Parse(sid));
    }

    private static async Task<long> ScrapeFirstEpIdAsync(string input, Core.AppConfig cfg)
    {
        var web = await GetWebSourceAsync(input, cfg);
        var json = StateRegex( ).Match(web).Groups[1].Value;
        using var jDoc = JsonDocument.Parse(json);
        return jDoc.RootElement.GetProperty("epList").EnumerateArray( ).First( ).GetProperty("id").GetInt64( );
    }

    // 纯数字 av 号可能实际指向番剧（稿件被重定向到番剧播放页），HEAD 探测后转 Ep，否则保持 Av
    private static async Task<ResourceId> FixAvidAsync(ResourceId id)
    {
        if (id is not Av av)
        {
            return id;
        }

        var api = $"{BiliApi.VideoPage}/av{av.Aid}/";
        var location = await GetWebLocationAsync(api);
        return location.Contains("/ep") ? new Ep(long.Parse(EpRegex( ).Match(location).Groups[1].Value)) : id;
    }

    // ss（番剧季号）直接解析为 season_id，与 md 路径完全对称：同样交由 BangumiInfoFetcher 按 season_id 拉取整季正片。
    private static async Task<long> GetSeasonIdBySSAsync(string ssId, Core.AppConfig cfg)
    {
        var api = $"https://{cfg.EpHost}{BiliApi.SeasonPgcPath}?season_id={ssId}";
        var json = await GetWebSourceAsync(api, cfg);
        using var jDoc = JsonDocument.Parse(json);
        var result = BBDown.Core.Util.JsonUtil.GetApiData(jDoc.RootElement, "番剧信息", "result");
        return result.GetProperty("season_id").GetInt64( );
    }

    // md（番剧详情页 id）本质是 media_id，需经 pgc/review/user 映射出 season_id，
    // 交由 BangumiInfoFetcher 按 season_id 拉取整季正片。旧实现取 new_ep.Id（最新一集）改为整季，用户可用 -p 选定具体集。
    private static async Task<long> GetSeasonIdByMDAsync(string mdId, Core.AppConfig cfg)
    {
        var api = $"{BiliApi.ReviewUser}?media_id={mdId}";
        var json = await GetWebSourceAsync(api, cfg);
        using var jDoc = JsonDocument.Parse(json);
        var media = BBDown.Core.Util.JsonUtil.GetApiData(jDoc.RootElement, "番剧信息", "result").GetProperty("media");
        return media.GetProperty("season_id").GetInt64( );
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
