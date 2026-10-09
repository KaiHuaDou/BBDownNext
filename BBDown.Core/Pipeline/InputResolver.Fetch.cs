using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core.Util;

using static BBDown.Core.ResourceId;
using static BBDown.Core.Util.HTTPUtil;
using static BBDown.Core.Util.JsonUtil;
using static BBDown.Core.Util.Utils;

namespace BBDown.Core.Pipeline;

/// <summary>
/// InputResolver 的触网部分：番剧季号换算、首集 ep 抓取与 av 重定向探测
/// 解析出 ResourceId 之后仍需外发请求的部分在此，与纯字符串解析分开
/// </summary>
public static partial class InputResolver
{
    // ss（番剧季号）直接解析为 season_id，与 md 路径完全对称：同样交由 BangumiInfoFetcher 按 season_id 拉取整季正片
    private static async Task<long> GetSeasonIdBySSAsync(string ssId, Core.AppConfig cfg, CancellationToken ct = default)
    {
        var api = $"https://{cfg.EpHost}{BiliApi.SeasonPgcPath}?season_id={ssId}";
        var json = await GetWebSourceAsync(api, cfg, ct: ct);
        using var jDoc = JsonDocument.Parse(json);
        var result = JsonUtil.GetApiData(jDoc.RootElement, "番剧信息", "result");
        // 字段缺失时给可读错误：接口变更 / 风控返回非预期结构，裸 GetProperty 只会抛晦涩 KeyNotFoundException
        if (result.ValueKind != JsonValueKind.Object || !result.TryGetProperty("season_id", out var seasonId) || seasonId.ValueKind != JsonValueKind.Number)
        {
            throw new InvalidOperationException($"番剧接口返回缺少 season_id 字段（ss={ssId}），接口返回结构可能已变更或被风控拦截");
        }

        return seasonId.GetInt64( );
    }

    // md（番剧详情页 id）本质是 media_id，需经 pgc/review/user 映射出 season_id
    // 交由 BangumiInfoFetcher 按 season_id 拉取整季正片，用户可用 -p 选定具体集
    private static async Task<long> GetSeasonIdByMDAsync(string mdId, Core.AppConfig cfg, CancellationToken ct = default)
    {
        var api = $"{BiliApi.ReviewUser}?media_id={mdId}";
        var json = await GetWebSourceAsync(api, cfg, ct: ct);
        using var jDoc = JsonDocument.Parse(json);
        var result = JsonUtil.GetApiData(jDoc.RootElement, "番剧信息", "result");
        if (result.ValueKind != JsonValueKind.Object || !result.TryGetProperty("media", out var media) || media.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException($"番剧接口返回缺少 media 字段（md={mdId}），接口返回结构可能已变更或被风控拦截");
        }

        if (!media.TryGetProperty("season_id", out var seasonId) || seasonId.ValueKind != JsonValueKind.Number)
        {
            throw new InvalidOperationException($"番剧接口返回缺少 season_id 字段（md={mdId}），接口返回结构可能已变更或被风控拦截");
        }

        return seasonId.GetInt64( );
    }

    private static async Task<long> ScrapeFirstEpIdAsync(string input, Core.AppConfig cfg, CancellationToken ct = default)
    {
        var web = await GetWebSourceAsync(input, cfg, ct: ct);
        // 解析失败时：匹配不到 __INITIAL_STATE__ 或页面不含 epList 时给可读错误，而不是 JsonDocument/GetProperty 抛晦涩异常
        if (InitialStateRegex( ).Match(web) is not { Success: true } stateMatch)
        {
            throw new InvalidOperationException("无法从页面源码解析出番剧播放信息（epList 缺失），请使用 ep/ss 链接直接下载");
        }

        using var jDoc = JsonDocument.Parse(stateMatch.Groups[1].Value);
        if (jDoc.RootElement.TryGetProperty("epList", out var epList) && epList.ValueKind == JsonValueKind.Array)
        {
            foreach (var ep in epList.EnumerateArray( ))
            {
                if (ep.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number)
                {
                    return id.GetInt64( );
                }
            }
        }

        throw new InvalidOperationException("无法从页面源码解析出番剧播放信息（epList 为空），请使用 ep/ss 链接直接下载");
    }

    // 纯数字 av 号可能实际指向番剧（稿件被重定向到番剧播放页），HEAD 探测后转 Ep，否则保持 Av
    private static async Task<ResourceId> FixAvidAsync(ResourceId id, CancellationToken ct = default)
    {
        if (id is not Av av)
        {
            return id;
        }

        var api = $"{BiliApi.VideoPage}/av{av.Aid}/";
        var location = await GetWebLocationAsync(api, ct);
        var epMatch = EpRegex( ).Match(location);
        return epMatch.Success && location.Contains("/ep") ? new Ep(long.Parse(epMatch.Groups[1].Value)) : id;
    }
}
