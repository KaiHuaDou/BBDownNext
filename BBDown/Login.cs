using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

using BBDown.Core.Util;

using QRCoder;

using static BBDown.Core.Logger;
using static BBDown.Utils;

namespace BBDown;

internal static class Login
{
    private static async Task ShowQrCodeAsync(string url)
    {
        Log("生成二维码...");
        using QRCodeGenerator qrGenerator = new( );
        using var qrCodeData = qrGenerator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q);
        using PngByteQRCode pngByteCode = new(qrCodeData);
        await File.WriteAllBytesAsync("qrcode.png", pngByteCode.GetGraphic(7));
        Log("生成二维码成功：qrcode.png，请打开并扫描，或扫描打印的二维码。");
        new ConsoleQRCode(qrCodeData).GetGraphic( );
    }

    private static async Task SaveCredentialAsync(string fileName, string content)
    {
        await File.WriteAllTextAsync(Path.Combine(Program.APP_DIR, fileName), content);
        File.Delete("qrcode.png");
    }

    public static async Task<string> GetLoginStatusAsync(string qrcodeKey)
    {
        var queryUrl = $"https://passport.bilibili.com/x/passport-login/web/qrcode/poll?qrcode_key={qrcodeKey}&source=main-fe-header";
        return await HTTPUtil.GetWebSourceAsync(queryUrl, Core.AppConfig.Empty);
    }

    public static async Task Web( )
    {
        try
        {
            Log("获取登录地址...");
            var loginUrl = "https://passport.bilibili.com/x/passport-login/web/qrcode/generate?source=main-fe-header";
            var url = JsonDocument.Parse(await HTTPUtil.GetWebSourceAsync(loginUrl, Core.AppConfig.Empty)).RootElement.GetProperty("data").GetProperty("url").ToString( );
            var qrcodeKey = GetQueryString("qrcode_key", url);
            var flag = false;
            await ShowQrCodeAsync(url);

            while (true)
            {
                await Task.Delay(1000);
                var w = await GetLoginStatusAsync(qrcodeKey);
                var code = JsonDocument.Parse(w).RootElement.GetProperty("data").GetProperty("code").GetInt32( );
                if (code == 86038)
                {
                    LogColor("二维码已过期，请重新执行登录指令。");
                    break;
                }
                else if (code == 86101) //等待扫码
                {
                    continue;
                }
                else if (code == 86090) //等待确认
                {
                    if (!flag)
                    {
                        Log("扫码成功，请确认...");
                        flag = !flag;
                    }
                }
                else
                {
                    var cc = JsonDocument.Parse(w).RootElement.GetProperty("data").GetProperty("url").ToString( );
                    Log("登录成功: SESSDATA=" + GetQueryString("SESSDATA", cc));
                    //导出cookie, 转义英文逗号 否则部分场景会出问题
                    await SaveCredentialAsync("BBDown.data", cc[(cc.IndexOf('?') + 1)..].Replace("&", ";").Replace(",", "%2C"));
                    break;
                }
            }
        }
        catch (Exception e) { LogError(e.Message); }
    }

    public static async Task TV( )
    {
        try
        {
            Uri loginUrl = new("https://passport.snm0516.aisee.tv/x/passport-tv-login/qrcode/auth_code");
            Uri pollUrl = new("https://passport.bilibili.com/x/passport-tv-login/qrcode/poll");
            var parms = GetTVLoginParms( );
            Log("获取登录地址...");
            using var loginContent = new FormUrlEncodedContent(parms.ToDictionary( ));
            var responseArray = await (await HTTPUtil.AppHttpClient.PostAsync(loginUrl, loginContent)).Content.ReadAsByteArrayAsync( );
            var web = Encoding.UTF8.GetString(responseArray);
            var url = JsonDocument.Parse(web).RootElement.GetProperty("data").GetProperty("url").ToString( );
            var authCode = JsonDocument.Parse(web).RootElement.GetProperty("data").GetProperty("auth_code").ToString( );
            await ShowQrCodeAsync(url);
            parms.Set("auth_code", authCode);
            parms.Set("ts", GetTimeStamp(true));
            parms.Remove("sign");
            parms.Add("sign", GetSign(ToQueryString(parms)));
            while (true)
            {
                await Task.Delay(1000);
                using var pollContent = new FormUrlEncodedContent(parms.ToDictionary( ));
                responseArray = await (await HTTPUtil.AppHttpClient.PostAsync(pollUrl, pollContent)).Content.ReadAsByteArrayAsync( );
                web = Encoding.UTF8.GetString(responseArray);
                var code = JsonDocument.Parse(web).RootElement.GetProperty("code").ToString( );
                if (code == "86038")
                {
                    LogColor("二维码已过期，请重新执行登录指令。");
                    break;
                }
                else if (code == "86039") //等待扫码
                {
                    continue;
                }
                else
                {
                    var cc = JsonDocument.Parse(web).RootElement.GetProperty("data").GetProperty("access_token").ToString( );
                    Log("登录成功：AccessToken=" + cc);
                    await SaveCredentialAsync("BBDownTV.data", "access_token=" + cc);
                    break;
                }
            }
        }
        catch (Exception e) { LogError(e.Message); }
    }
}