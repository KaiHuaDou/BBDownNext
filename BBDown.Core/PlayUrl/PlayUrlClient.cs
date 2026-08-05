using System;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using static BBDown.Core.Logger;
using static BBDown.Core.Util.HTTPUtil;
using static BBDown.Core.Util.SignUtil;

namespace BBDown.Core.PlayUrl;

/// <summary>
/// playurl 请求：URL 构造与发送（WEB / TV / INTL / 网页源码兜底）。
/// 不含任何解析逻辑——拿到的是原始 JSON 字符串，由 <see cref="PlayUrlResponse"/> 与各个 TrackReader 负责导航。
/// </summary>
internal static partial class PlayUrlClient
{
    // appkey 与其配对的 salt 必须成对出现，混用会被服务端判为签名错误
    private const string TvAppKey = "4409e2ce8ffd12b8";
    private const string TvAppSecret = "59b43e04ad6965f34319062b478f83dd";
    private const string BiliPlusAppKey = "7d089525d3611b1c";
    private const string BiliPlusAppSecret = "acd495b248ec528c2eed1e862d393126";

    internal static async Task<string> FetchAsync(PlayUrlRequest req, string qn = "0", CancellationToken ct = default)
    {
        LogDebug("aid={0},cid={1},epId={2},api={3},qn={4}", req.Aid, req.Cid, req.EpId, req.Api, qn);

        if (req.Api == ApiType.Intl)
        {
            return await FetchIntlAsync(req, qn, "0", ct);
        }

        LogDebug("bangumi={0},cheese={1}", req.IsBangumi, req.IsCheese);

        var api = BuildPrefix(req.Api == ApiType.Tv, req.IsBangumi, req.IsCheese, req.Cfg.TvHost, req.Cfg.Host)
            + (req.Api == ApiType.Tv ? BuildTvQuery(req, qn) : BuildWebQuery(req, qn));

        var webJson = await GetWebSourceAsync(api, req.Cfg, null, ct);
        if (!PlayUrlResponse.IsVipRestricted(webJson))
        {
            return webJson;
        }

        //大会员专享限制时从网页源代码尝试解析
        Log("此视频需要大会员，您大概率需要登录一个有大会员的账号才可以下载，尝试从网页源码解析。");
        return await FetchFromWebPageAsync(req, ct);
    }

    // 大会员专享限制时, 改从网页源码抠 window.__playinfo__。
    // 与正常 API 路径解耦为独立方法, 并按 cheese / 番剧构造正确的播放页地址,
    // 匹配失败时抛明确异常(而非返回空串导致后续 JSON 解析报莫名其妙的错)。
    internal static async Task<string> FetchFromWebPageAsync(PlayUrlRequest req, CancellationToken ct = default)
    {
        var pageUrl = req.IsCheese
            ? $"{BiliApi.CheesePlayPage}/ep{req.EpId}"
            : $"{BiliApi.BangumiPlayPage}/ep{req.EpId}";
        var webSource = await GetWebSourceAsync(pageUrl, req.Cfg, null, ct);
        var match = PlayerJsonRegex( ).Match(webSource);
        if (!match.Success)
        {
            throw new InvalidOperationException("从网页源码解析播放信息失败");
        }

        return match.Groups[1].Value;
    }

    internal static string BuildPrefix(bool tvApi, bool bangumi, bool cheese, string tvHost, string host)
    {
        var prefix = (tvApi, bangumi) switch
        {
            (true, true) => tvHost + BiliApi.PlayUrlPgcTvPath,
            (true, false) => tvHost + BiliApi.PlayUrlTvPath,
            (false, true) => host + BiliApi.PlayUrlPgcPath,
            (false, false) => host + BiliApi.PlayUrlWebPath
        };
        // 课程（cheese）与番剧共用同一套 playurl 网关，仅域名路径中的 /pgc/ 需替换为 /pugv/。
        // 因此直接复用 PGC 的 v2 路径（含 DASH 支持），再整体换域名——并非文档里写的非 v2 端点，属有意设计。
        if (cheese)
        {
            prefix = prefix.Replace("/pgc/", "/pugv/");
        }

        return $"https://{prefix}?";
    }

    internal static string BuildTvQuery(PlayUrlRequest req, string qn)
    {
        StringBuilder query = new( );
        if (req.Cfg.Token.Length != 0)
        {
            query.Append($"access_key={req.Cfg.Token}&");
        }

        query.Append($"appkey={TvAppKey}&build=106500&cid={req.Cid}&device=android");
        if (req.IsBangumi)
        {
            query.Append($"&ep_id={req.EpId}&expire=0");
        }

        // TV 端点实测不提供 qn=100（智能修复），保持 4048 即可；强改 12240 无收益且可能触发风控
        query.Append("&fnval=4048&fnver=0&fourk=1&mid=0&mobi_app=android_tv_yst");
        query.Append($"&object_id={req.Aid}&platform=android&playurl_type=1&qn={qn}&ts={UnixTimestamp( )}");
        return $"{query}&sign={AppSign(query.ToString( ), TvAppSecret)}";
    }

    internal static string BuildWebQuery(PlayUrlRequest req, string qn)
    {
        StringBuilder query = new( );
        var fnval = req.IsBangumi ? Config.FnvalPgc : Config.Fnval;
        query.Append($"support_multi_audio=true&from_client=BROWSER&avid={req.Aid}&cid={req.Cid}&fnval={fnval}&fnver=0&fourk=1");
        if (req.Cfg.Area.Length != 0)
        {
            query.Append($"&access_key={req.Cfg.Token}&area={req.Cfg.Area}");
        }

        query.Append($"&otype=json&qn={qn}");
        if (req.IsBangumi)
        {
            // 课程（cheese）复用番剧 playurl 参数（module=bangumi&ep_id&session）；pugv 端点会忽略 module，ep_id 为必需。
            query.Append($"&module=bangumi&ep_id={req.EpId}&session=");
        }

        if (req.Cfg.Cookie.Length == 0)
        {
            query.Append("&try_look=1");
        }

        query.Append($"&wts={UnixTimestamp( )}");
        return req.IsBangumi ? query.ToString( ) : WbiSign(query.ToString( ), req.Cfg);
    }

    internal static async Task<string> FetchIntlAsync(PlayUrlRequest req, string qn, string code = "0", CancellationToken ct = default)
    {
        var cfg = req.Cfg;
        var isBiliPlus = cfg.Host != BiliApi.MainHost;
        var api = $"https://{(isBiliPlus ? cfg.Host : BiliApi.IntlWebHost)}{BiliApi.IntlPlayUrlPath}?";

        StringBuilder query = new( );
        if (cfg.Token.Length != 0)
        {
            query.Append($"access_key={cfg.Token}&");
        }

        query.Append($"aid={req.Aid}");
        if (isBiliPlus)
        {
            query.Append($"&appkey={BiliPlusAppKey}&area={(cfg.Area.Length == 0 ? "th" : cfg.Area)}");
        }

        query.Append($"&cid={req.Cid}&ep_id={req.EpId}&platform=android&prefer_code_type={code}&qn={qn}");
        if (isBiliPlus)
        {
            query.Append($"&ts={UnixTimestamp( )}");
        }

        query.Append("&s_locale=zh_SG");
        var param = query.ToString( );
        return await GetWebSourceAsync(api + (isBiliPlus ? $"{param}&sign={AppSign(param, BiliPlusAppSecret)}" : param), cfg, null, ct);
    }

    // 网页源码兜底时抠取 window.__playinfo__ 里的 JSON；宿主类为 partial 以承载源生成正则
    [GeneratedRegex("window.__playinfo__=([\\s\\S]*?)<\\/script>")]
    private static partial Regex PlayerJsonRegex( );
}
