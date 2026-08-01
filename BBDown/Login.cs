using System;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

using BBDown.Core;
using BBDown.Core.Util;

using QRCoder;

using static BBDown.Core.Logger;
using static BBDown.Utils;

namespace BBDown;

public static class Login
{
    public enum QrState { Expired, WaitingScan, WaitingConfirm, Success }

    private static readonly string[] WebCookieNames = ["DedeUserID", "DedeUserID__ckMd5", "SESSDATA", "bili_jct"];

    // code 字段在 Web 与 TV 两套接口间类型不一致（整数/字符串），ToString 对两种 ValueKind 都能取到原文
    private static int ReadCode(JsonElement element) => int.Parse(element.ToString( ));

    private static string ReadMessage(JsonElement element)
        => element.TryGetProperty("message", out var m) ? (m.GetString( ) ?? "") : "";

    public static (QrState State, string? Data) InterpretWeb(JsonElement root)
    {
        var outer = ReadCode(root.GetProperty("code"));
        if (outer != 0) throw new InvalidOperationException($"轮询失败：{outer} {ReadMessage(root)}");

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
        var (url, key) = await plan.Generate();
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
                    if (data is not null) await plan.Persist(data);
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
        new ConsoleQRCode(qrCodeData).GetGraphic( );
    }

    private static void DeleteQrCode(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }

    public static async Task Web( )
    {
        var qrPath = Path.Combine(Path.GetTempPath( ), "BBDown_qrcode.png");
        try
        {
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
                    var pollUrl = $"{BiliApi.QrCodePoll}?qrcode_key={key}&source=main-fe-header";
                    using var doc = JsonDocument.Parse(await HTTPUtil.GetWebSourceAsync(pollUrl, Core.AppConfig.Empty));
                    return doc.RootElement.Clone( );
                },
                Interpret: InterpretWeb,
                Persist: async url =>
                {
                    Log("登录成功：SESSDATA=" + GetQueryString("SESSDATA", url));
                    await CredentialStore.SaveWebCookie(BuildWebCookie(url));
                },
                ExpiredText: "二维码已过期，请重新执行登录指令。"), qrPath);
        }
        catch (Exception e) { LogError(e.Message); }
        finally { DeleteQrCode(qrPath); }
    }

    public static async Task TV( )
    {
        var qrPath = Path.Combine(Path.GetTempPath( ), "BBDown_qrcode.png");
        NameValueCollection? tvParms = null;
        try
        {
            await RunQrLoginAsync(new QrLoginPlan(
                Generate: async ( ) =>
                {
                    Log("获取登录地址...");
                    Uri loginUrl = new(BiliApi.TvQrCodeAuth);
                    var parms = GetTVLoginParms( );
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
                    parms.Add("sign", GetSign(ToQueryString(parms)));
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
                Persist: async data =>
                {
                    Log("登录成功：AccessToken=" + data);
                    await CredentialStore.SaveTvToken("access_token=" + data);
                },
                ExpiredText: "二维码已过期，请重新执行登录指令。"), qrPath);
        }
        catch (Exception e) { LogError(e.Message); }
        finally { DeleteQrCode(qrPath); }
    }
}
