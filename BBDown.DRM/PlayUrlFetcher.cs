using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core;
using BBDown.Core.Auth;
using BBDown.Core.Util;

namespace BBDown.DRM;

// 自行重新抓取 playurl 获取加密信息：主程序不传任何加密特征与凭据。
// web 通道需要 WBI 签名：签名与 mixin key 派生复用主仓库 SignUtil / Account（按键名升序的 canonical 由 SignUtil 保证）。
// 普通请求拿不到加密特征时以 drm_tech_type=2 重试——该参数才会下发标准 Widevine 流（含 pssh）。
internal static class PlayUrlFetcher
{
    /// <summary>
    /// 抓取 playurl 并返回首个加密轨的通道类型 / bili_drm uri / widevine pssh；无加密轨返回全空。
    /// KID 与轨类型无关（同一视频的加密轨共享密钥），取首个加密轨即够用。
    /// </summary>
    public static async Task<(string? DrmType, string? BiliDrmUri, string? PsshBase64)> FetchAsync(string aid, string cid, string kind, CancellationToken token = default)
    {
        var cookie = CredentialStore.LoadWebCookie( );
        var cfg = new AppConfig(cookie, "", BiliApi.MainHost, BiliApi.MainHost, BiliApi.TvHost, "", "", "");
        var result = await FetchOnceAsync(aid, cid, kind, cfg, null, token);
        if (result.DrmType is null)
        {
            // 标准 widevine 流只在 drm_tech_type=2 时下发
            result = await FetchOnceAsync(aid, cid, kind, cfg, 2, token);
        }

        return result;
    }

    private static async Task<(string? DrmType, string? BiliDrmUri, string? PsshBase64)> FetchOnceAsync(string aid, string cid, string kind, AppConfig cfg, int? drmTechType, CancellationToken token)
    {
        var url = await BuildPlayUrlAsync(aid, cid, cfg, drmTechType, token);
        using var json = await HTTPUtil.GetJsonAsync(url, cfg, token);
        var dash = json.RootElement.GetProperty("data").GetProperty("dash");
        // background / role 等伴音轨同在 audio 列表下发；KID 与轨类型无关，取首个加密轨即够用
        var listName = kind == "video" ? "video" : "audio";
        foreach (var track in dash.GetProperty(listName).EnumerateArray( ))
        {
            var uri = ReadString(track, "bilidrm_uri");
            var pssh = ReadString(track, "widevine_pssh");
            if (uri == null && pssh == null)
            {
                continue;
            }

            return (ReadString(track, "drm_type") ?? (pssh != null ? "widevine" : "bili_drm"), uri, pssh);
        }

        return (null, null, null);
    }

    private static async Task<string> BuildPlayUrlAsync(string aid, string cid, AppConfig cfg, int? drmTechType, CancellationToken token)
    {
        var (_, wbi) = await Account.ProbeAccountAsync(cfg, token);
        if (wbi.Length == 0)
        {
            throw new InvalidOperationException("获取 WBI 密钥失败");
        }

        var parameters = new List<KeyValuePair<string, string>>
        {
            new("avid", aid),
            new("cid", cid),
            new("fnval", Config.Fnval.ToString( )),
            new("fourk", "1"),
            new("qn", "127")
        };
        if (drmTechType is int type)
        {
            parameters.Add(new("drm_tech_type", type.ToString( )));
        }

        var query = SignUtil.WbiSignedQuery(parameters, cfg with { Wbi = wbi });
        return $"https://{BiliApi.MainHost}{BiliApi.PlayUrlWebPath}?{query}";
    }

    private static string? ReadString(JsonElement node, string name)
    {
        return node.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null ? value.ToString( ) : null;
    }
}
