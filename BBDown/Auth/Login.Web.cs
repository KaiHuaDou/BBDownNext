using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core;
using BBDown.Core.Util;

using static BBDown.Core.Logger;
using static BBDown.Util.Utils;

namespace BBDown.Auth;

public static partial class Login
{
    private static readonly string[] WebCookieNames = ["DedeUserID", "DedeUserID__ckMd5", "SESSDATA", "bili_jct"];

    /// <summary>
    /// crossDomain 回调 url 的 query 里混有 gourl / first_domain / Expires 等非 cookie 字段，只取真正的登录 cookie。
    /// </summary>
    public static string BuildWebCookie(string url)
    {
        var cookie = string.Join(';', url[(url.IndexOf('?') + 1)..]
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .Where(kv => kv.Length == 2 && WebCookieNames.Contains(kv[0]))
            .Select(kv => $"{kv[0]}={EscapeCookieValue(kv[1])}"));
        return cookie.Length > 0 ? cookie : throw new InvalidOperationException("登录响应中缺少 Cookie");
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

    public static async Task<int> Web(CancellationToken token = default)
    {
        var qrPath = Path.Combine(Path.GetTempPath( ), "BBDown_qrcode.png");
        var setCookies = new List<string>( );
        string? refreshToken = null;
        try
        {
            // 设备指纹（buvid3/4）非登录必需，但 B 站风控可能核查；尽力初始化，失败不阻断
            try { await Buvid.InitAsync(token); } catch (Exception e) { LogDebug("初始化设备指纹失败，继续登录：{0}", e.Message); }

            var ok = await RunQrLoginAsync(new QrLoginPlan(
                Generate: async cancellationToken =>
                {
                    Log("获取登录地址...");
                    var loginUrl = $"{BiliApi.QrCodeGenerate}?source=main-fe-header";
                    using var doc = JsonDocument.Parse(await HTTPUtil.GetWebSourceAsync(loginUrl, Core.AppConfig.Empty, null, cancellationToken));
                    var url = ReadData(doc.RootElement, "获取登录二维码失败").GetProperty("url").GetString( )!;
                    var key = GetQueryString("qrcode_key", url);
                    return (url, key);
                },
                Poll: async (key, cancellationToken) =>
                {
                    // 轮询响应的 Set-Cookie 头在 Persist 阶段还要用，这里先快照下来，避免跨闭包持有 HttpResponseMessage
                    var pollUrl = $"{BiliApi.QrCodePoll}?qrcode_key={key}&source=main-fe-header";
                    using var response = await HTTPUtil.GetRawResponseAsync(pollUrl, Core.AppConfig.Empty, cancellationToken);
                    setCookies.Clear( );
                    if (response.Headers.TryGetValues("Set-Cookie", out var setCookieHeaders))
                    {
                        setCookies.AddRange(setCookieHeaders);
                    }

                    using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
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
                Persist: async (url, cancellationToken) =>
                {
                    var cookie = await BuildWebCookieResilient(url, setCookies);
                    Log($"登录成功：SESSDATA={MaskSecret(GetCookieValue("SESSDATA", cookie))}");
                    await CredentialStore.SaveWebCookie(cookie, refreshToken: refreshToken, issueTs: DateTimeOffset.UtcNow.ToUnixTimeSeconds( ));
                    if (!string.IsNullOrEmpty(refreshToken))
                    {
                        Log("已保存 refresh_token，将用于后续 SESSDATA 主动续期。");
                    }

                    // 校验凭据可用并打印账号名（best-effort，失败不阻断）
                    try
                    {
                        var (info, _) = await Account.ProbeAccountAsync(new Core.AppConfig(cookie, "", BiliApi.MainHost, BiliApi.MainHost, BiliApi.TvHost, "", ""), cancellationToken);
                        if (info.IsLogin)
                        {
                            Log($"已登录账号：{info.UserName}");
                        }
                    }
                    catch (Exception e)
                    {
                        LogDebug("登录后账号校验失败（可忽略）：{0}", e.Message);
                    }
                },
                ExpiredText: "二维码已过期，请重新执行登录指令。"), qrPath, token);
            return ok ? 0 : 1;
        }
        catch (Exception e) { LogError(e.Message); return 1; }
        finally { DeleteQrCode(qrPath); }
    }

    /// <summary>
    /// 从多个来源合并出登录 cookie：优先 data.Url 的 query（旧通道 / 兜底），其次 poll 响应自身的
    /// Set-Cookie 头，再次 crossDomain 端点 GET 后的 CookieContainer（B 站当前正规通道）。任一来源补齐即采用，
    /// 全部缺失才抛错。英文逗号会被部分下游当作 cookie 分隔符，需转义。
    /// </summary>
    private static async Task<string> BuildWebCookieResilient(string url, IReadOnlyList<string> setCookies)
    {
        var values = new Dictionary<string, string>( );

        // 1) data.Url query（旧通道 / 兜底）
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
        foreach (var (name, value) in ParseSetCookies(setCookies))
        {
            values.TryAdd(name, value);
        }

        // 3) crossDomain 端点 GET 后的 CookieContainer（正规通道，仅当仍缺字段时再发请求）
        if (!WebCookieNames.All(values.ContainsKey) && !string.IsNullOrEmpty(url))
        {
            try
            {
                var jar = await HTTPUtil.GetCookieJarAsync(url, Core.AppConfig.Empty);
                foreach (Cookie cookie in jar.GetCookies(new Uri("https://bilibili.com")))
                {
                    if (WebCookieNames.Contains(cookie.Name))
                    {
                        values.TryAdd(cookie.Name, cookie.Value);
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

    // 从 Set-Cookie 行提取允许范围内的 cookie 键值；同名取首个，值截到分号前
    private static Dictionary<string, string> ParseSetCookies(IEnumerable<string> setCookies)
    {
        var values = new Dictionary<string, string>( );
        foreach (var sc in setCookies)
        {
            var parts = sc.Split('=', 2);
            var name = parts[0].Trim( );
            if (parts.Length == 2 && WebCookieNames.Contains(name) && !values.ContainsKey(name))
            {
                values[name] = parts[1].Split(';')[0];
            }
        }

        return values;
    }

    // 英文逗号会被部分下游当作 cookie 分隔符，需转义
    private static string EscapeCookieValue(string value)
    {
        return value.Replace(",", "%2C");
    }

    private static string? GetCookieValue(string name, string cookie)
    {
        return cookie.Split(';').Select(p => p.Trim( ).Split('=', 2))
                .FirstOrDefault(kv => kv.Length == 2 && kv[0] == name)?[1];
    }
}
