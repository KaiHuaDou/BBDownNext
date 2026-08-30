using System;
using System.Collections.Specialized;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core.Util;

using static BBDown.Core.Logger;

namespace BBDown.Core.Auth;

public static partial class Login
{
    // appkey / 签名密钥必须配对；TV 用云视听小电视，APP 用手机粉版
    private const string TvAppKey = "4409e2ce8ffd12b8";
    private const string TvAppSecret = "59b43e04ad6965f34319062b478f83dd";
    private const string PhoneAppKey = "783bbb7264451d82";
    private const string PhoneAppSecret = "2653583c8873dea268ab9386918b1d65";

    /// <summary>TV 扫码登录：生成二维码后回调 showQr，成功后返回 access_token（不落盘）；过期返回 null。</summary>
    public static Task<string?> TvCredentialAsync(
        Func<string, Task>? showQr = null, Action<QrState>? onState = null, CancellationToken token = default)
    {
        return LoginWithAppKey(TvAppKey, "android_tv_yst", TvAppSecret, showQr, onState, token);
    }

    /// <summary>APP 扫码登录：生成二维码后回调 showQr，成功后返回 access_token（不落盘）；过期返回 null。</summary>
    public static Task<string?> AppCredentialAsync(
        Func<string, Task>? showQr = null, Action<QrState>? onState = null, CancellationToken token = default)
    {
        return LoginWithAppKey(PhoneAppKey, "android", PhoneAppSecret, showQr, onState, token);
    }

    public static async Task<int> TV(CancellationToken token = default)
    {
        try
        {
            var accessToken = await TvCredentialAsync(showQr: ShowQrCodeCliAsync, token: token);
            if (accessToken is null)
            {
                return 1;
            }

            Log($"登录成功：AccessToken={MaskSecret(accessToken)}");
            await CredentialStore.SaveTvToken(accessToken, issueTs: DateTimeOffset.UtcNow.ToUnixTimeSeconds( ));
            return 0;
        }
        catch (Exception e)
        {
            LogError(e.Message);
            return 1;
        }
        finally
        {
            DeleteQrCode(TempQrPath);
        }
    }

    public static async Task<int> App(CancellationToken token = default)
    {
        try
        {
            var accessToken = await AppCredentialAsync(showQr: ShowQrCodeCliAsync, token: token);
            if (accessToken is null)
            {
                return 1;
            }

            Log($"登录成功：AccessToken={MaskSecret(accessToken)}");
            await CredentialStore.SaveAppToken(accessToken, issueTs: DateTimeOffset.UtcNow.ToUnixTimeSeconds( ));
            return 0;
        }
        catch (Exception e)
        {
            LogError(e.Message);
            return 1;
        }
        finally
        {
            DeleteQrCode(TempQrPath);
        }
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
    /// 参数按 key 字典序排列，签名要求如此，新增字段须保持顺序。
    /// </summary>
    private static NameValueCollection NewLoginParams(string appKey, string mobiApp)
    {
        NameValueCollection paras = [];
        var now = DateTime.Now;
        var deviceId = GetRandomString(20);
        var buvid = GetRandomString(37);
        var fingerprint = $"{now:yyyyMMddHHmmssfff}{GetRandomString(45)}";
        paras.Add("appkey", appKey);
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
        paras.Add("mobi_app", mobiApp);
        paras.Add("networkstate", "wifi");
        paras.Add("platform", "android");
        paras.Add("sys_ver", "29");
        paras.Add("ts", GetTimeStamp(true));

        return paras;
    }

    /// <summary>
    /// 签名覆盖除 sign 外的全部参数，任何参数变动后都必须重新签名；旧 sign 残留在待签串里会让服务端返回 -3 签名错误。
    /// </summary>
    private static void ApplySign(NameValueCollection parms, string secret)
    {
        parms.Remove("sign");
        parms.Add("sign", GetSign(ToQueryString(parms), secret));
    }

    // 纯扫码流程：生成二维码、轮询、解释状态，成功后返回 access_token；落盘由各自入口负责
    private static async Task<string?> LoginWithAppKey(
        string appKey, string mobiApp, string appSecret,
        Func<string, Task>? showQr, Action<QrState>? onState, CancellationToken token)
    {
        NameValueCollection? tvParms = null;
        return await RunQrLoginAsync(new QrLoginPlan(
            Generate: async cancellationToken =>
            {
                Log("获取登录地址...");
                Uri loginUrl = new(BiliApi.TvQrCodeAuth);
                var parms = NewLoginParams(appKey, mobiApp);
                ApplySign(parms, appSecret);
                using var loginContent = new FormUrlEncodedContent(parms.ToDictionary( ));
                using var loginRequest = new HttpRequestMessage(HttpMethod.Post, loginUrl) { Content = loginContent };
                loginRequest.Headers.TryAddWithoutValidation("User-Agent", BiliHeaders.UserAgent);
                using var response = await HTTPUtil.AppHttpClient.SendAsync(loginRequest, cancellationToken);
                using var doc = JsonDocument.Parse(await HttpTransfer.ReadBodyBytesAsync(response.Content, cancellationToken));
                var data = ReadData(doc.RootElement, "获取登录二维码失败");
                var url = data.GetProperty("url").GetString( )!;
                var authCode = data.GetProperty("auth_code").GetString( )!;
                parms.Set("auth_code", authCode);
                parms.Set("ts", GetTimeStamp(true));
                ApplySign(parms, appSecret);
                tvParms = parms;
                return (url, authCode);
            },
            Poll: async (_, cancellationToken) =>
            {
                Uri pollUrl = new(BiliApi.TvQrCodePoll);
                using var pollContent = new FormUrlEncodedContent(tvParms!.ToDictionary( ));
                using var pollRequest = new HttpRequestMessage(HttpMethod.Post, pollUrl) { Content = pollContent };
                pollRequest.Headers.TryAddWithoutValidation("User-Agent", BiliHeaders.UserAgent);
                using var response = await HTTPUtil.AppHttpClient.SendAsync(pollRequest, cancellationToken);
                using var doc = JsonDocument.Parse(await HttpTransfer.ReadBodyBytesAsync(response.Content, cancellationToken));
                return doc.RootElement.Clone( );
            },
            Interpret: InterpretTv,
            ExpiredText: "二维码已过期，请重新登录。"), showQr, onState, token);
    }
}
