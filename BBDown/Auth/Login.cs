using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

using BBDown.Core;
using BBDown.Core.Util;

using QRCoder;

using static BBDown.Core.Logger;
using static BBDown.Util.Utils;

namespace BBDown.Auth;

public static partial class Login
{
    public enum QrState { Expired, WaitingScan, WaitingConfirm, Success }

    private static readonly string[] WebCookieNames = ["DedeUserID", "DedeUserID__ckMd5", "SESSDATA", "bili_jct"];

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

    // code 字段在 Web 与 TV 两套接口间类型不一致（整数/字符串），ToString 对两种 ValueKind 都能取到原文
    private static int ReadCode(JsonElement element)
    {
        return int.Parse(element.ToString( ));
    }

    private static string ReadMessage(JsonElement element)
    {
        return element.TryGetProperty("message", out var m) ? (m.GetString( ) ?? "") : "";
    }

    // 日志中只展示凭据首尾，避免明文泄露（P0-3）
    private static string MaskSecret(string? s)
    {
        return string.IsNullOrEmpty(s) || s.Length <= 8 ? "***" : $"{s[..4]}****{s[^4..]}";
    }

    public static (QrState State, string? Data) InterpretWeb(JsonElement root)
    {
        var outer = ReadCode(root.GetProperty("code"));
        if (outer != 0)
        {
            throw new InvalidOperationException($"轮询失败：{outer} {ReadMessage(root)}");
        }

        var data = root.GetProperty("data");
        var state = ReadCode(data.GetProperty("code")) switch
        {
            0 => QrState.Success,
            86038 => QrState.Expired,
            86090 => QrState.WaitingConfirm,
            86101 => QrState.WaitingScan,
            var code => throw new InvalidOperationException($"未知的扫码状态：{code} {ReadMessage(data)}")
        };
        return (state, state == QrState.Success ? data.GetProperty("url").GetString( ) : null);
    }

    public static (QrState State, string? Data) InterpretTv(JsonElement root)
    {
        var state = ReadCode(root.GetProperty("code")) switch
        {
            0 => QrState.Success,
            86038 => QrState.Expired,
            86039 => QrState.WaitingScan,
            86090 => QrState.WaitingConfirm,
            var code => throw new InvalidOperationException($"扫码登录失败：{code} {ReadMessage(root)}")
        };
        return (state, state == QrState.Success ? root.GetProperty("data").GetProperty("access_token").GetString( ) : null);
    }

    /// <summary>
    /// crossDomain 回调 url 的 query 里混有 gourl / first_domain / Expires 等非 cookie 字段，只取真正的登录 cookie。
    /// </summary>
    public static string BuildWebCookie(string url)
    {
        var cookie = string.Join(';', url[(url.IndexOf('?') + 1)..]
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .Where(kv => kv.Length == 2 && WebCookieNames.Contains(kv[0]))
            // 英文逗号会被部分下游当作 cookie 分隔符，需转义
            .Select(kv => $"{kv[0]}={kv[1].Replace(",", "%2C")}"));
        return cookie.Length > 0 ? cookie : throw new InvalidOperationException("登录响应中缺少 Cookie");
    }

    /// <summary>
    /// 扫码登录的通用编排参数：生成二维码、轮询状态、解释状态、落盘凭据。
    /// Web 与 TV 仅这些环节不同，轮询循环本身完全一致。
    /// </summary>
    private record QrLoginPlan(
        Func<Task<(string Url, string Key)>> Generate,
        Func<string, Task<JsonElement>> Poll,
        Func<JsonElement, (QrState State, string? Data)> Interpret,
        Func<string, Task> Persist,
        string ExpiredText);

    private static async Task RunQrLoginAsync(QrLoginPlan plan, string qrPath)
    {
        var (url, key) = await plan.Generate( );
        await ShowQrCodeAsync(url, qrPath);
        var confirmed = false;
        while (true)
        {
            await Task.Delay(1000);
            var (state, data) = plan.Interpret(await plan.Poll(key));
            switch (state)
            {
                case QrState.Expired:
                    LogColor(plan.ExpiredText);
                    return;
                case QrState.WaitingScan:
                    break;
                case QrState.WaitingConfirm:
                    if (!confirmed)
                    {
                        Log("扫码成功，请确认...");
                        confirmed = true;
                    }

                    break;
                case QrState.Success:
                    if (data is not null)
                    {
                        await plan.Persist(data);
                    }

                    return;
            }
        }
    }

    private static async Task ShowQrCodeAsync(string url, string qrPath)
    {
        Log("生成二维码...");
        using QRCodeGenerator qrGenerator = new( );
        using var qrCodeData = qrGenerator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q);
        using PngByteQRCode pngByteCode = new(qrCodeData);
        await File.WriteAllBytesAsync(qrPath, pngByteCode.GetGraphic(7));
        Log("生成二维码成功，请打开并扫描，或扫描打印的二维码。");
        using var ascii = new AsciiQRCode(qrCodeData);
        Console.WriteLine(ascii.GetGraphic(1, "█", " ", false));
    }

    private static void DeleteQrCode(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    public static async Task<int> Web( )
    {
        var qrPath = Path.Combine(Path.GetTempPath( ), "BBDown_qrcode.png");
        var success = false;
        HttpResponseMessage? pollResp = null;
        string? refreshToken = null;
        try
        {
            // 设备指纹（buvid3/4）非登录必需，但 B 站风控可能核查；尽力初始化，失败不阻断
            try { await Buvid.InitAsync( ); } catch { }

            await RunQrLoginAsync(new QrLoginPlan(
                Generate: async ( ) =>
                {
                    Log("获取登录地址...");
                    var loginUrl = $"{BiliApi.QrCodeGenerate}?source=main-fe-header";
                    using var doc = JsonDocument.Parse(await HTTPUtil.GetWebSourceAsync(loginUrl, Core.AppConfig.Empty));
                    var url = doc.RootElement.GetProperty("data").GetProperty("url").GetString( )!;
                    var key = GetQueryString("qrcode_key", url);
                    return (url, key);
                },
                Poll: async key =>
                {
                    pollResp?.Dispose( );
                    var pollUrl = $"{BiliApi.QrCodePoll}?qrcode_key={key}&source=main-fe-header";
                    pollResp = await HTTPUtil.GetRawResponseAsync(pollUrl, Core.AppConfig.Empty);
                    using var doc = JsonDocument.Parse(await pollResp.Content.ReadAsStringAsync( ));
                    return doc.RootElement.Clone( );
                },
                Interpret: root =>
                {
                    var (state, url) = InterpretWeb(root);
                    if (state == QrState.Success && root.GetProperty("data").TryGetProperty("refresh_token", out var rt))
                    {
                        refreshToken = rt.ValueKind == JsonValueKind.String ? rt.GetString( ) : null;
                    }

                    return (state, url);
                },
                Persist: async url =>
                {
                    var cookie = await BuildWebCookieResilient(url, pollResp);
                    Log($"登录成功：SESSDATA={MaskSecret(GetCookieValue("SESSDATA", cookie))}");
                    await CredentialStore.SaveWebCookie(cookie, refreshToken: refreshToken, issueTs: DateTimeOffset.UtcNow.ToUnixTimeSeconds( ));
                    if (!string.IsNullOrEmpty(refreshToken))
                    {
                        Log("已保存 refresh_token，将用于后续 SESSDATA 主动续期。");
                    }

                    success = true;
                },
                ExpiredText: "二维码已过期，请重新执行登录指令。"), qrPath);
        }
        catch (Exception e) { LogError(e.Message); }
        finally
        {
            pollResp?.Dispose( );
            DeleteQrCode(qrPath);
        }

        return success ? 0 : 1;
    }

    /// <summary>
    /// 从多个来源合并出登录 cookie：优先 data.url 的 query（旧通道 / 兜底），其次 poll 响应自身的
    /// Set-Cookie 头，再次 crossDomain 端点 GET 后的 CookieContainer（B 站当前正规通道）。任一来源补齐即采用，
    /// 全部缺失才抛错。英文逗号会被部分下游当作 cookie 分隔符，需转义。
    /// </summary>
    private static async Task<string> BuildWebCookieResilient(string url, HttpResponseMessage? pollResp)
    {
        var values = new Dictionary<string, string>( );

        // 1) data.url query（旧通道 / 兜底）
        if (!string.IsNullOrEmpty(url) && url.Contains('?'))
        {
            foreach (var pair in url[(url.IndexOf('?') + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = pair.Split('=', 2);
                if (kv.Length == 2 && WebCookieNames.Contains(kv[0]) && !values.ContainsKey(kv[0]))
                {
                    values[kv[0]] = kv[1];
                }
            }
        }

        // 2) poll 响应自身的 Set-Cookie 头
        if (pollResp is not null && pollResp.Headers.TryGetValues("Set-Cookie", out var setCookies))
        {
            foreach (var sc in setCookies)
            {
                var parts = sc.Split('=', 2);
                if (parts.Length == 2 && WebCookieNames.Contains(parts[0].Trim( )) && !values.ContainsKey(parts[0].Trim( )))
                {
                    values[parts[0].Trim( )] = parts[1].Split(';')[0];
                }
            }
        }

        // 3) crossDomain 端点 GET 后的 CookieContainer（正规通道，仅当仍缺字段时再发请求）
        if (!WebCookieNames.All(values.ContainsKey) && !string.IsNullOrEmpty(url))
        {
            try
            {
                var jar = await HTTPUtil.GetCookieJarAsync(url, Core.AppConfig.Empty);
                foreach (Cookie cookie in jar.GetCookies(new Uri("https://bilibili.com")))
                {
                    if (WebCookieNames.Contains(cookie.Name) && !values.ContainsKey(cookie.Name))
                    {
                        values[cookie.Name] = cookie.Value;
                    }
                }
            }
            catch (Exception e)
            {
                LogDebug("crossDomain GET 失败，回退到其它来源：{0}", e.Message);
            }
        }

        var missing = WebCookieNames.Where(n => !values.ContainsKey(n)).ToArray( );
        if (missing.Length > 0)
        {
            throw new InvalidOperationException($"登录响应中缺少 Cookie：{string.Join(", ", missing)}");
        }

        return string.Join(';', WebCookieNames.Select(n => $"{n}={EscapeCookieValue(values[n])}"));
    }

    private static string EscapeCookieValue(string value)
    {
        return value.Replace(",", "%2C");
    }

    private static string? GetCookieValue(string name, string cookie)
    {
        return cookie.Split(';').Select(p => p.Trim( ).Split('=', 2))
                .FirstOrDefault(kv => kv.Length == 2 && kv[0] == name)?[1];
    }

    // ── Web Cookie 主动续期（cookie_refresh）───────────────────────────────────

    /// <summary>
    /// 主动续期 web cookie（best-effort）。仅当本地持有 refresh_token 时尝试；先问 /cookie/info 是否需要刷新，
    /// 需要才走 RSA 签名 → 取 refresh_csrf → POST refresh → confirm 全流。任一步失败都回退到原 cookie，绝不阻断下载。
    /// </summary>
    public static async Task<string> TryRefreshWebCookieIfStaleAsync(string? dir = null, CancellationToken ct = default)
    {
        var (cookie, refreshToken, _) = CredentialStore.LoadWebCredential(dir);
        if (string.IsNullOrEmpty(cookie) || string.IsNullOrEmpty(refreshToken))
        {
            return cookie ?? "";
        }

        try
        {
            var (newCookie, newRefresh) = await RefreshWebCookieAsync(cookie, refreshToken, ct);
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

    private static async Task<(string? cookie, string? refreshToken)> RefreshWebCookieAsync(string cookie, string refreshToken, CancellationToken ct)
    {
        var cfg = new Core.AppConfig(cookie, "", BiliApi.MainHost, BiliApi.MainHost, BiliApi.TvHost, "", "");

        // 1) /cookie/info 是否需要刷新（B 站官方信号，避免无谓刷新）
        using var infoResp = await HTTPUtil.GetRawResponseAsync(CookieInfoUrl, cfg, ct);
        using var infoDoc = JsonDocument.Parse(await infoResp.Content.ReadAsStringAsync(ct));
        var data = infoDoc.RootElement.GetProperty("data");
        if (!data.GetProperty("refresh").GetBoolean( ))
        {
            return (null, null);
        }

        var timestamp = data.GetProperty("timestamp").GetInt64( );

        // 2) CorrespondPath = RSA-OAEP(SHA-256)("refresh_{ts}") 小写 hex
        var correspondPath = MakeCorrespondPath(timestamp);

        // 3) 取实时刷新口令 refresh_csrf
        using var csrfResp = await HTTPUtil.GetRawResponseAsync($"https://www.bilibili.com/correspond/1/{correspondPath}", cfg, ct);
        var html = await csrfResp.Content.ReadAsStringAsync(ct);
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
        using var refreshResp = await HTTPUtil.PostFormRawAsync(RefreshUrl, form, cfg, ct);
        using var refreshDoc = JsonDocument.Parse(await refreshResp.Content.ReadAsStringAsync(ct));
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
            await HTTPUtil.PostFormRawAsync(ConfirmUrl, confirmForm, new Core.AppConfig(newCookie, "", BiliApi.MainHost, BiliApi.MainHost, BiliApi.TvHost, "", ""), ct);
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

    [GeneratedRegex("<div id=\"1-name\">(.*?)</div>")]
    private static partial Regex CorrespondRegex( );

    private static string ExtractCookiesFromSetCookie(HttpResponseHeaders headers, string fallback)
    {
        var values = new Dictionary<string, string>( );
        if (headers.TryGetValues("Set-Cookie", out var setCookies))
        {
            foreach (var sc in setCookies)
            {
                var parts = sc.Split('=', 2);
                if (parts.Length == 2 && WebCookieNames.Contains(parts[0].Trim( )))
                {
                    values[parts[0].Trim( )] = parts[1].Split(';')[0];
                }
            }
        }

        return WebCookieNames.All(values.ContainsKey)
            ? string.Join(';', WebCookieNames.Select(n => $"{n}={EscapeCookieValue(values[n])}"))
            : fallback;
    }

    // appkey / 签名密钥必须配对；TV 用云视听小电视，APP 用手机粉版
    private const string TvAppKey = "4409e2ce8ffd12b8";
    private const string TvAppSecret = "59b43e04ad6965f34319062b478f83dd";
    private const string PhoneAppKey = "783bbb7264451d82";
    private const string PhoneAppSecret = "2653583c8873dea268ab9386918b1d65";

    public static async Task<int> TV( )
    {
        var token = await LoginWithAppKey(TvAppKey, "android_tv_yst", TvAppSecret);
        if (token is null)
        {
            return 1;
        }

        await CredentialStore.SaveTvToken(token, issueTs: DateTimeOffset.UtcNow.ToUnixTimeSeconds( ));
        return 0;
    }

    public static async Task<int> App( )
    {
        var token = await LoginWithAppKey(PhoneAppKey, "android", PhoneAppSecret);
        if (token is null)
        {
            return 1;
        }

        await CredentialStore.SaveAppToken(token, issueTs: DateTimeOffset.UtcNow.ToUnixTimeSeconds( ));
        return 0;
    }

    // 签名 / QR 登录参数相关辅助方法（仅 TV/APP 登录流程使用）
    public static string GetSign(string parms)
    {
        return GetSign(parms, TvAppSecret);
    }

    // appkey 与签名密钥必须配对；手机端登录使用粉版 appkey 时须传入对应密钥
    public static string GetSign(string parms, string secret)
    {
        var toEncode = parms + secret;
        return string.Concat(MD5.HashData(Encoding.UTF8.GetBytes(toEncode)).Select(i => i.ToString("x2")));
    }

    public static string GetTimeStamp(bool bflag)
    {
        var ts = DateTimeOffset.Now;
        return (bflag ? ts.ToUnixTimeSeconds( ) : ts.ToUnixTimeMilliseconds( )).ToString( );
    }

    //  https://stackoverflow.com/questions/1344221/how-can-i-generate-random-alphanumeric-strings

    public static string GetRandomString(int length)
    {
        const string Chars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz_0123456789";
        return new string([.. Enumerable.Repeat(Chars, length).Select(s => s[Random.Shared.Next(s.Length)])]);
    }

    // https://stackoverflow.com/a/45088333
    public static string ToQueryString(NameValueCollection nameValueCollection)
    {
        var httpValueCollection = HttpUtility.ParseQueryString(string.Empty);
        httpValueCollection.Add(nameValueCollection);
        return httpValueCollection.ToString( )!;
    }

    public static Dictionary<string, string> ToDictionary(this NameValueCollection nameValueCollection)
    {
        Dictionary<string, string> dict = [];
        foreach (var key in nameValueCollection.AllKeys)
        {
            dict[key!] = nameValueCollection[key]!;
        }

        return dict;
    }

    public static NameValueCollection GetTVLoginParms( )
    {
        NameValueCollection paras = [];
        var now = DateTime.Now;
        var deviceId = GetRandomString(20);
        var buvid = GetRandomString(37);
        var fingerprint = $"{now:yyyyMMddHHmmssfff}{GetRandomString(45)}";
        paras.Add("appkey", "4409e2ce8ffd12b8");
        paras.Add("auth_code", "");
        paras.Add("bili_local_id", deviceId);
        paras.Add("build", "102801");
        paras.Add("buvid", buvid);
        paras.Add("channel", "master");
        paras.Add("device", "OnePlus");
        paras.Add("device_id", deviceId);
        paras.Add("device_name", "OnePlus7TPro");
        paras.Add("device_platform", "Android10OnePlusHD1910");
        paras.Add("fingerprint", fingerprint);
        paras.Add("guid", buvid);
        paras.Add("local_fingerprint", fingerprint);
        paras.Add("local_id", buvid);
        paras.Add("mobi_app", "android_tv_yst");
        paras.Add("networkstate", "wifi");
        paras.Add("platform", "android");
        paras.Add("sys_ver", "29");
        paras.Add("ts", GetTimeStamp(true));
        paras.Add("sign", GetSign(ToQueryString(paras)));

        return paras;
    }

    // 纯扫码流程：生成二维码、轮询、解释状态，成功后返回 access_token；落盘由各自入口负责
    private static async Task<string?> LoginWithAppKey(string appKey, string mobiApp, string appSecret)
    {
        var qrPath = Path.Combine(Path.GetTempPath( ), "BBDown_qrcode.png");
        NameValueCollection? tvParms = null;
        string? token = null;
        try
        {
            await RunQrLoginAsync(new QrLoginPlan(
                Generate: async ( ) =>
                {
                    Log("获取登录地址...");
                    Uri loginUrl = new(BiliApi.TvQrCodeAuth);
                    var parms = GetTVLoginParms( );
                    parms.Set("appkey", appKey);
                    parms.Set("mobi_app", mobiApp);
                    parms.Set("sign", GetSign(ToQueryString(parms), appSecret));
                    using var loginContent = new FormUrlEncodedContent(parms.ToDictionary( ));
                    using var loginRequest = new HttpRequestMessage(HttpMethod.Post, loginUrl) { Content = loginContent };
                    loginRequest.Headers.TryAddWithoutValidation("User-Agent", HTTPUtil.UserAgent);
                    using var response = await HTTPUtil.AppHttpClient.SendAsync(loginRequest);
                    using var doc = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync( ));
                    var data = doc.RootElement.GetProperty("data");
                    var url = data.GetProperty("url").GetString( )!;
                    var authCode = data.GetProperty("auth_code").GetString( )!;
                    parms.Set("auth_code", authCode);
                    parms.Set("ts", GetTimeStamp(true));
                    parms.Remove("sign");
                    parms.Add("sign", GetSign(ToQueryString(parms), appSecret));
                    tvParms = parms;
                    return (url, authCode);
                },
                Poll: async _ =>
                {
                    Uri pollUrl = new(BiliApi.TvQrCodePoll);
                    using var pollContent = new FormUrlEncodedContent(tvParms!.ToDictionary( ));
                    using var pollRequest = new HttpRequestMessage(HttpMethod.Post, pollUrl) { Content = pollContent };
                    pollRequest.Headers.TryAddWithoutValidation("User-Agent", HTTPUtil.UserAgent);
                    using var response = await HTTPUtil.AppHttpClient.SendAsync(pollRequest);
                    using var doc = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync( ));
                    return doc.RootElement.Clone( );
                },
                Interpret: InterpretTv,
                Persist: data =>
                {
                    Log($"登录成功：AccessToken={MaskSecret(data)}");
                    token = data;
                    return Task.CompletedTask;
                },
                ExpiredText: "二维码已过期，请重新执行登录指令。"), qrPath);
        }
        catch (Exception ex) { LogError(ex.Message); }
        finally { DeleteQrCode(qrPath); }

        return token;
    }
}
