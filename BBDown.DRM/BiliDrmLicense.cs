using System;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core.Util;

namespace BBDown.DRM;

// biliDRM（clearkey）取钥：持公开 RSA 公钥即可向 bilidrm 接口换取内容密钥，
// 无需任何设备文件。流程：取公钥 → 生成会话密钥/IV → 构造 SPC → 提交 → 解析 CKC。
// 与 widevine 的差别只在取钥路径，密文同为 cbcs，解密执行由 DrmDecryptor 复用。
internal static class BiliDrmLicense
{
    private const string PublicKeyUrl = "https://bvc-drm.bilivideo.com/cer/bilidrm_pub.key";
    private const string LicenseUrl = "https://bvc-drm.bilivideo.com/bilidrm";

    private static string? cachedPublicKey;

    /// <summary>kid 为 32 字符 hex 字符串（SDK 按 ASCII 字节处理）；成功返回内容密钥（hex），失败返回 null。</summary>
    public static async Task<string?> GetKeyAsync(string kid, CancellationToken ct)
    {
        var publicKey = await GetPublicKeyAsync(ct);
        var sessionKey = new byte[16];
        RandomNumberGenerator.Fill(sessionKey);
        var iv = new byte[16];
        RandomNumberGenerator.Fill(iv);

        var spc = SpcBuilder.Build(Encoding.ASCII.GetBytes(kid), sessionKey, iv, publicKey);
        var ckc = await SendAsync(spc, ct);
        var key = CkcParser.ParseKey(ckc, sessionKey);
        return key is null ? null : Convert.ToHexString(key).ToLowerInvariant( );
    }

    private static async Task<string> GetPublicKeyAsync(CancellationToken ct)
    {
        if (cachedPublicKey is not null)
        {
            return cachedPublicKey;
        }

        using var response = await HTTPUtil.AppHttpClient.GetAsync(PublicKeyUrl, ct);
        response.EnsureSuccessStatusCode( );
        var pem = await response.Content.ReadAsStringAsync(ct);
        cachedPublicKey = pem;
        return pem;
    }

    private static async Task<byte[]> SendAsync(string spc, CancellationToken ct)
    {
        // 接口仅接受 JSON（实测 application/x-protobuf 返回 400 only support JSON format），
        // 请求体形如 {"spc": <base64>}，响应 {"ckc": <base64>}（content-type 按旧版 SDK 用表单类型）
        using var content = new StringContent($"{{\"spc\":\"{spc}\"}}", Encoding.UTF8, "application/x-www-form-urlencoded");
        using var response = await HTTPUtil.AppHttpClient.PostAsync(LicenseUrl, content, ct);
        response.EnsureSuccessStatusCode( );
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var ckc = json.RootElement.GetProperty("ckc").GetString( );
        return ckc is null ? [] : Convert.FromBase64String(ckc);
    }
}
