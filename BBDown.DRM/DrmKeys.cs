using System;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace BBDown.DRM;

// 取钥统一入口：按通道分发到对应取钥实现，成功返回 key，失败返回原因。
// widevine 经 CDM 向 license 服务器取钥；bili_drm 先走 clearkey 自动取钥（无需任何配置），
// 失败时回退用户密钥表；两条通道的取钥差异收敛于此，调用方不再关心通道类型。
// 失败原因只在 key 为 null 时有意义。
internal static class DrmKeys
{
    /// <summary>bili_drm 的 KID 取自 bilidrm_uri 最后一个 // 之后（32 位 hex）。</summary>
    public static string? KidFromUri(string? uri)
    {
        if (uri == null)
        {
            return null;
        }

        var i = uri.LastIndexOf("//", StringComparison.Ordinal);
        return i >= 0 ? uri[(i + 2)..] : uri;
    }

    public static async Task<(string? Key, DrmResult? Failure)> ResolveAsync(
        string drmType, string? biliDrmUri, string? psshBase64, DrmKeySource keys, string? wvdPath, CancellationToken ct)
    {
        if (drmType == "widevine")
        {
            return await FetchWidevineAsync(psshBase64, wvdPath, ct);
        }

        return await FetchBiliDrmAsync(biliDrmUri, keys, ct);
    }

    // widevine 交互失败（wvd 缺失/损坏、license 服务器拒绝、响应校验不过）统一归 FetchFailed
    private static async Task<(string? Key, DrmResult? Failure)> FetchWidevineAsync(string? psshBase64, string? wvdPath, CancellationToken ct)
    {
        if (psshBase64 is null || wvdPath is null)
        {
            return (null, DrmResult.DeviceMissing);
        }

        try
        {
            var keys = await WidevineLicense.FetchAsync(psshBase64, wvdPath, ct);
            return keys is { Length: > 0 } ? (keys[0].Key, null) : (null, DrmResult.FetchFailed);
        }
        catch (Exception)
        {
            return (null, DrmResult.FetchFailed);
        }
    }

    // clearkey 自动取钥失败（KID 无效、接口拒绝等）回退密钥表；两者皆不可用时按密钥缺失处理
    private static async Task<(string? Key, DrmResult? Failure)> FetchBiliDrmAsync(string? biliDrmUri, DrmKeySource keys, CancellationToken ct)
    {
        var kid = KidFromUri(biliDrmUri);
        if (kid is { Length: 32 })
        {
            try
            {
                var autoKey = await BiliDrmLicense.GetKeyAsync(kid, ct);
                if (autoKey is not null)
                {
                    return (autoKey, null);
                }
            }
            catch (Exception)
            {
                // 自动取钥失败（网络/接口异常）时继续走密钥表
            }
        }

        var key = keys.TryGetKey(kid);
        return key is null ? (null, DrmResult.KeyMissing) : (key, null);
    }
}
