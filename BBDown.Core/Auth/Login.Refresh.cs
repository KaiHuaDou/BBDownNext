#pragma warning disable CA1308 // 刷新路径协议要求小写 hex

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core;
using BBDown.Core.Util;

using static BBDown.Core.Logger;

namespace BBDown.Core.Auth;

public static partial class Login
{
    private const string CookieInfoUrl = "https://passport.bilibili.com/x/passport-login/web/cookie/info";
    private const string RefreshUrl = "https://passport.bilibili.com/x/passport-login/web/cookie/refresh";
    private const string ConfirmUrl = "https://passport.bilibili.com/x/passport-login/web/confirm/refresh";

    // cookie_refresh 的 CorrespondPath 加密公钥（固定，来源 bilibili-API-collect/docs/login/cookie_refresh.md）
    private const string RefreshRsaPublicKey = """
        -----BEGIN PUBLIC KEY-----
        MIGfMA0GCSqGSIb3DQEBAQUAA4GNADCBiQKBgQDLgd2OAkcGVtoE3ThUREbio0Eg
        Uc/prcajMKXvkCKFCWhJYJcLkcM2DKKcSeFpD/j6Boy538YXnR6VhcuUJOhH2x71
        nzPjfdTcqMz7djHum0qSZA0AyCBDABUqCrfNgCiJ00Ra7GmRj+YCK1NJEuewlb40
        JNrRuoEUXpabUzGB8QIDAQAB
        -----END PUBLIC KEY-----
        """;

    /// <summary>
    /// 显式续期（<c>login refresh</c>）。与 <see cref="TryRefreshWebCookieIfStaleAsync"/> 的差异
    /// 那条路径是下载前的 best-effort 补充，失败静默沿用旧凭据；本方法面向用户主动操作
    /// 逐项汇报结果并以退出码表达成败。仅 WEB 通道有续期能力
    /// </summary>
    public static async Task<int> RefreshAsync(CancellationToken token = default)
    {
        var (cookie, refreshToken, _) = CredentialStore.LoadWebCredential( );
        if (string.IsNullOrEmpty(cookie))
        {
            LogError("未登录 WEB 账号，无可续期的 Cookie，请先运行 BBDown login");
            return 1;
        }

        if (string.IsNullOrEmpty(refreshToken))
        {
            LogError("本地没有 refresh_token，无法续期，请重新运行 BBDown login 扫码登录");
            return 1;
        }

        try
        {
            // force：用户主动要求续期，不看 /cookie/info 的「是否需要刷新」信号
            // 该信号用于避免无谓刷新，与显式续期的意图相反
            var (newCookie, newRefresh) = await RefreshWebCookieAsync(cookie, refreshToken, true, token);
            if (string.IsNullOrEmpty(newCookie))
            {
                LogError("Cookie 续期未返回新 Cookie");
                return 1;
            }

            await CredentialStore.SaveWebCookie(newCookie, refreshToken: newRefresh ?? refreshToken, issueTs: DateTimeOffset.UtcNow.ToUnixTimeSeconds( ));
            Log($"WEB Cookie 已续期：SESSDATA={MaskSecret(GetCookieValue("SESSDATA", newCookie))}");
            return 0;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            LogError($"Cookie 续期失败：{e.Message}");
            return 1;
        }
    }

    /// <summary>
    /// 主动续期 web cookie（best-effort）。仅当本地持有 refresh_token 时尝试；先问 /cookie/info 是否需要刷新
    /// 需要才走 RSA 签名 → 取 refresh_csrf → POST refresh → confirm 全流。任一步失败都回退到原 cookie，绝不阻断下载
    /// </summary>
    public static async Task<string> TryRefreshWebCookieIfStaleAsync(string? dir = null, CancellationToken token = default)
    {
        var (cookie, refreshToken, _) = CredentialStore.LoadWebCredential(dir);
        if (string.IsNullOrEmpty(cookie) || string.IsNullOrEmpty(refreshToken))
        {
            return cookie ?? "";
        }

        try
        {
            var (newCookie, newRefresh) = await RefreshWebCookieAsync(cookie, refreshToken, false, token);
            if (!string.IsNullOrEmpty(newCookie))
            {
                await CredentialStore.SaveWebCookie(newCookie, dir: dir, refreshToken: newRefresh ?? refreshToken, issueTs: DateTimeOffset.UtcNow.ToUnixTimeSeconds( ));
                Log("Web Cookie 已通过 refresh_token 主动续期。");
                return newCookie;
            }
        }
        catch (Exception e)
        {
            LogDebug("Cookie 主动续期失败，沿用现有凭据：{0}", e.Message);
        }

        return cookie;
    }

    private static async Task<(string? cookie, string? refreshToken)> RefreshWebCookieAsync(string cookie, string refreshToken, bool force, CancellationToken token)
    {
        var cfg = new AppConfig(cookie, "", BiliApi.MainHost, BiliApi.MainHost, BiliApi.TvHost, "", "", "");

        // 1) 无论 force 与否都要读 cookie/info：needRefresh 决定是否走全流，timestamp 是 CorrespondPath 的输入
        var (needRefresh, timestamp) = await ReadCookieInfoAsync(cookie, token);
        if (!force && !needRefresh)
        {
            return (null, null);
        }

        // 2) CorrespondPath = RSA-OAEP(SHA-256)("refresh_{ts}") 小写 hex
        var correspondPath = MakeCorrespondPath(timestamp);

        // 3) 取实时刷新口令 refresh_csrf
        using var csrfResp = await HTTPUtil.GetRawResponseAsync($"https://www.bilibili.com/correspond/1/{correspondPath}", cfg, token);
        var html = await HttpTransfer.ReadBodyAsync(csrfResp.Content, token);
        var m = CorrespondRegex( ).Match(html);
        if (!m.Success)
        {
            throw new InvalidOperationException("无法从 correspond 页面解析 refresh_csrf");
        }

        var refreshCsrf = m.Groups[1].Value;

        // 4) 刷新 Cookie
        var form = new Dictionary<string, string>
        {
            ["csrf"] = GetCookieValue("bili_jct", cookie) ?? "",
            ["refresh_csrf"] = refreshCsrf,
            ["source"] = "main_web",
            ["refresh_token"] = refreshToken,
        };
        using var refreshResp = await HTTPUtil.PostFormRawAsync(RefreshUrl, form, cfg, token);
        using var refreshDoc = JsonDocument.Parse(await HttpTransfer.ReadBodyAsync(refreshResp.Content, token));
        if (refreshDoc.RootElement.GetProperty("code").GetInt32( ) != 0)
        {
            throw new InvalidOperationException($"Cookie 刷新失败：{refreshDoc.RootElement.GetProperty("message").GetString( )}");
        }

        var newRefresh = refreshDoc.RootElement.GetProperty("data").GetProperty("refresh_token").GetString( );
        var newCookie = ExtractCookiesFromSetCookie(refreshResp.Headers, cookie);

        // 5) 确认（best-effort，使旧 refresh_token 失效）
        try
        {
            var confirmForm = new Dictionary<string, string>
            {
                ["csrf"] = GetCookieValue("bili_jct", newCookie) ?? "",
                ["refresh_token"] = refreshToken,
            };
            await HTTPUtil.PostFormRawAsync(ConfirmUrl, confirmForm, new AppConfig(newCookie, "", BiliApi.MainHost, BiliApi.MainHost, BiliApi.TvHost, "", "", ""), token);
        }
        catch (Exception e)
        {
            LogDebug("confirm/refresh 失败（可忽略）：{0}", e.Message);
        }

        return (newCookie, newRefresh);
    }

    private static string MakeCorrespondPath(long timestamp)
    {
        using var rsa = RSA.Create( );
        rsa.ImportFromPem(RefreshRsaPublicKey);
        var encrypted = rsa.Encrypt(Encoding.UTF8.GetBytes($"refresh_{timestamp}"), RSAEncryptionPadding.OaepSHA256);
        return Convert.ToHexString(encrypted).ToLowerInvariant( );
    }

    /// <summary>
    /// 问 /cookie/info 是否需要刷新 Cookie，并取当前毫秒时间戳（后者用于生成 CorrespondPath）
    /// 该端点对无效 Cookie 返回 <c>code = -400</c>（与 nav 的 -101 不同），异常上抛由调用方处置
    /// </summary>
    internal static async Task<(bool needRefresh, long timestamp)> ReadCookieInfoAsync(string cookie, CancellationToken token)
    {
        var cfg = new AppConfig(cookie, "", BiliApi.MainHost, BiliApi.MainHost, BiliApi.TvHost, "", "", "");
        using var response = await HTTPUtil.GetRawResponseAsync(CookieInfoUrl, cfg, token);
        using var doc = JsonDocument.Parse(await HttpTransfer.ReadBodyAsync(response.Content, token));
        var data = doc.RootElement.GetProperty("data");
        return (data.GetProperty("refresh").GetBoolean( ), data.GetProperty("timestamp").GetInt64( ));
    }

    [GeneratedRegex("<div id=\"1-name\">(.*?)</div>")]
    private static partial Regex CorrespondRegex( );

    private static string ExtractCookiesFromSetCookie(HttpResponseHeaders headers, string fallback)
    {
        var values = headers.TryGetValues("Set-Cookie", out var setCookies)
            ? ParseSetCookies(setCookies)
            : [];

        return WebCookieNames.All(values.ContainsKey)
            ? string.Join(';', WebCookieNames.Select(n => $"{n}={EscapeCookieValue(values[n])}"))
            : fallback;
    }
}
