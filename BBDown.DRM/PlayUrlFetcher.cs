using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core;
using BBDown.Core.Auth;
using BBDown.Core.Util;

namespace BBDown.DRM;

// 自行重新抓取 playurl 获取加密信息：主程序不传任何加密特征与凭据。
// web 通道需要 WBI 签名：从 nav 接口取 img_key / sub_key 生成 mixin key（bilibili 标准算法）。
// 普通请求拿不到加密特征时以 drm_tech_type=2 重试——该参数才会下发标准 Widevine 流（含 pssh）。
internal static class PlayUrlFetcher
{
    // WBI mixin key 的 64 位重排索引表（bilibili 标准实现）
    private static readonly int[] MixinKeyTable =
    [
        46, 47, 18, 2, 53, 8, 23, 32, 15, 50, 10, 31, 58, 3, 45, 35, 27, 43, 5, 49,
        33, 9, 42, 19, 29, 28, 14, 39, 12, 38, 41, 13, 37, 48, 7, 16, 24, 55, 40, 61,
        26, 17, 0, 1, 60, 51, 30, 4, 22, 25, 54, 21, 56, 59, 6, 63, 57, 62, 11, 36,
        20, 34, 44, 52
    ];

    /// <summary>
    /// 抓取 playurl 并返回首个加密轨的通道类型 / bili_drm uri / widevine pssh；无加密轨返回全空。
    /// KID 与轨类型无关（同一视频的加密轨共享密钥），取首个加密轨即够用。
    /// </summary>
    public static async Task<(string? DrmType, string? BiliDrmUri, string? PsshBase64)> FetchAsync(string aid, string cid, string kind, CancellationToken ct = default)
    {
        var cookie = CredentialStore.LoadWebCookie( );
        var cfg = new AppConfig(cookie, "", BiliApi.MainHost, BiliApi.MainHost, BiliApi.TvHost, "", "", "");
        var result = await FetchOnceAsync(aid, cid, kind, cfg, null, ct);
        if (result.DrmType is null)
        {
            // 标准 widevine 流只在 drm_tech_type=2 时下发
            result = await FetchOnceAsync(aid, cid, kind, cfg, 2, ct);
        }

        return result;
    }

    private static async Task<(string? DrmType, string? BiliDrmUri, string? PsshBase64)> FetchOnceAsync(string aid, string cid, string kind, AppConfig cfg, int? drmTechType, CancellationToken ct)
    {
        var url = await BuildPlayUrlAsync(aid, cid, cfg, drmTechType, ct);
        using var json = await HTTPUtil.GetJsonAsync(url, cfg, ct);
        var dash = json.RootElement.GetProperty("data").GetProperty("dash");
        foreach (var listName in kind == "video" ? new[] { "video" } : new[] { "audio" })
        {
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
        }

        return (null, null, null);
    }

    private static async Task<string> BuildPlayUrlAsync(string aid, string cid, AppConfig cfg, int? drmTechType, CancellationToken ct)
    {
        var (imgKey, subKey) = await FetchWbiKeysAsync(cfg, ct);
        var wts = DateTimeOffset.UtcNow.ToUnixTimeSeconds( );
        var drm = drmTechType is int type ? $"&drm_tech_type={type}" : "";
        var query = $"avid={aid}&cid={cid}&fnval={Config.Fnval}&fourk=1&qn=127&wts={wts}{drm}";
        return $"https://{BiliApi.MainHost}{BiliApi.PlayUrlWebPath}?{query}&w_rid={Wrid(query, MixinKey(imgKey, subKey))}";
    }

    // nav 接口返回的 img_url / sub_url 是图片地址，key 取文件名去扩展名
    private static async Task<(string ImgKey, string SubKey)> FetchWbiKeysAsync(AppConfig cfg, CancellationToken ct)
    {
        using var json = await HTTPUtil.GetJsonAsync($"https://{BiliApi.MainHost}/x/web-interface/nav", cfg, ct);
        var wbiImg = json.RootElement.GetProperty("data").GetProperty("wbi_img");
        var imgKey = KeyOf(wbiImg.GetProperty("img_url").GetString( ));
        var subKey = KeyOf(wbiImg.GetProperty("sub_url").GetString( ));
        return (imgKey, subKey);
    }

    private static string KeyOf(string? url)
    {
        if (url == null)
        {
            return "";
        }

        var fileName = url[(url.LastIndexOf('/') + 1)..];
        var dot = fileName.LastIndexOf('.');
        return dot > 0 ? fileName[..dot] : fileName;
    }

    // img_key + sub_key 按索引表重排取前 32 位，作为请求签名密钥
    private static string MixinKey(string imgKey, string subKey)
    {
        var mixed = new char[64];
        var source = imgKey + subKey;
        for (var i = 0; i < 64; i++)
        {
            mixed[i] = source[MixinKeyTable[i]];
        }

        return new string(mixed, 0, 32);
    }

    // MD5(按字典序拼接的 query + mixin key)，32 位小写 hex
    private static string Wrid(string query, string mixinKey)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes(query + mixinKey));
        return Convert.ToHexStringLower(bytes);
    }

    private static string? ReadString(JsonElement node, string name)
    {
        return node.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null ? value.ToString( ) : null;
    }
}
