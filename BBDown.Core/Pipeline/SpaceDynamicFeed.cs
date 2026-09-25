using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core.Auth;
using BBDown.Core.Download;
using BBDown.Core.Util;

using static BBDown.Core.Util.HTTPUtil;
using static BBDown.Core.Util.JsonUtil;

namespace BBDown.Core.Pipeline;

/// <summary>
/// 空间动态流（feed/space）共用拉取器：WBI 签名 + offset 游标翻页，返回克隆后的动态 entry。
/// 空间图文（<see cref="SpaceOpusDownload"/>，仅图文）与空间动态（<see cref="SpaceDynamicDownload"/>，全类型分发）共用。
/// </summary>
internal static class SpaceDynamicFeed
{
    private const int PageSize = 20;
    private const int MaxItems = 1000;

    // Web 端动态页固定携带的 features 清单，与 bilibili-API-collect docs/dynamic/space.md 默认值一致；
    // 缺 features / web_location / platform 时 feed/space 的风控层会直接 HTTP 412 拒绝
    private const string WebFeatures =
        "itemOpusStyle,listOnlyfans,opusBigCover,onlyfansVote,forwardListHidden,decorationCard,commentsNewVersion,onlyfansAssetsV2,ugcDelete,onlyfansQaCard";

    /// <summary>feed/space 需要 WBI 签名：nav 探测取密钥（未登录也能拿到，签名缺失会被服务端拒绝）。</summary>
    public static async Task<AppConfig> ResolveConfigAsync(DownloadRequest myOption, CancellationToken ct)
    {
        var cfg = WorkSetup.ResolveConfig(myOption, ApiType.Web);
        await Buvid.InitAsync(ct);
        var (_, wbi) = await Account.ProbeAccountAsync(cfg, ct);
        return cfg with { Wbi = wbi };
    }

    /// <summary>按 offset 游标翻页拉取全部动态 entry（Clone 脱离 JsonDocument 生命周期）；上限兜底防 has_more 异常导致死循环。</summary>
    public static async Task<List<JsonElement>> CollectEntriesAsync(long mid, AppConfig cfg, CancellationToken ct)
    {
        List<JsonElement> entries = [];
        string? offset = null;
        while (entries.Count < MaxItems)
        {
            var query = offset is null
                ? $"host_mid={mid}&page_size={PageSize}"
                : $"host_mid={mid}&page_size={PageSize}&offset={offset}";
            query += $"&timezone_offset=-480&platform=web&features={WebFeatures}&web_location=333.1387";
            var api = $"{BiliApi.SpaceDynamicFeed}?{SignUtil.WbiSignNow(query, cfg)}";
            using var doc = JsonDocument.Parse(await GetFeedAsync(api, cfg, ct));
            var data = GetApiData(doc.RootElement, "空间动态流");

            // 风控时接口可能只回 has_more 而无 items（未登录 + 无 buvid3 / 签名失效等）
            if (!TryGetArray(data, "items", out var feed))
            {
                throw new InvalidOperationException("获取空间动态流失败：接口未返回 items（可能被风控拦截，请登录后重试）");
            }

            var got = 0;
            string? lastId = null;
            foreach (var entry in feed.EnumerateArray( ))
            {
                got++;
                lastId = ReadStr(entry, "id_str");
                entries.Add(entry.Clone( ));
            }

            var hasMore = data.TryGetProperty("has_more", out var more) && more.ValueKind == JsonValueKind.True;
            if (got == 0 || !hasMore || lastId is null)
            {
                break;
            }

            offset = lastId;
        }

        return entries;
    }

    // HTTP 412 是风控层在业务校验前直接返回的反爬页（非 JSON），EnsureSuccessStatusCode 抛出的
    // 错误对用户不可操作，改写为可行动提示
    private static async Task<string> GetFeedAsync(string api, AppConfig cfg, CancellationToken ct)
    {
        try
        {
            return await GetWebSourceAsync(api, cfg, null, ct);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.PreconditionFailed)
        {
            throw new InvalidOperationException("获取空间动态流被风控拦截（HTTP 412），请先登录（携带有效 SESSDATA）或稍后重试", ex);
        }
    }

    private static string ReadStr(JsonElement obj, string name)
    {
        return obj.ValueKind == JsonValueKind.Object
            && obj.TryGetProperty(name, out var v)
            && v.ValueKind == JsonValueKind.String
            ? v.GetString( ) ?? ""
            : "";
    }
}
