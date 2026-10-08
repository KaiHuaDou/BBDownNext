using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core.Util;

using static BBDown.Core.Logger;
using static BBDown.Core.Util.HTTPUtil;

namespace BBDown.Core.Auth;

/// <summary>
/// 账号探测与 WBI 密钥派生：nav 接口解析账号信息 + 由 img/sub url 派生 WBI mixin key
/// </summary>
public static class Account
{
    public static async Task<(AccountInfo Info, string Wbi)> ProbeAccountAsync(Core.AppConfig cfg, CancellationToken ct = default)
    {
        try
        {
            return await ProbeAsync(cfg, ct);
        }
        catch (OperationCanceledException)
        {
            // 用户取消须上抛，否则会被压成「未登录」并让下载继续
            throw;
        }
        catch (Exception ex)
        {
            LogDebug("获取账号信息失败: {0}", ex.Message);
            return (new AccountInfo(false, "", 0, false, ""), "");
        }
    }

    /// <summary>
    /// nav 探测本体，异常上抛。调用方需自行区分「服务端否认」与「探测未完成」——
    /// <see cref="ProbeAccountAsync"/> 把两者一并压成未登录，只适用于不关心该差异的链路
    /// </summary>
    internal static async Task<(AccountInfo Info, string Wbi)> ProbeAsync(Core.AppConfig cfg, CancellationToken ct)
    {
        var source = await GetWebSourceAsync(BiliApi.Nav, cfg, null, ct);
        using var doc = JsonDocument.Parse(source);
        var data = doc.RootElement.GetProperty("data");
        var info = ParseNav(data);
        var wbi_img = data.GetProperty("wbi_img");
        var wbi = GetMixinKey(RSubString(wbi_img.GetProperty("img_url").GetString( )!) + RSubString(wbi_img.GetProperty("sub_url").GetString( )!));
        LogDebug("wbi: {0}", wbi);
        return (info, wbi);
    }

    /// <summary>
    /// 用 access_token 探测 APP / TV 通道账号信息。请求只带 User-Agent：令牌在 query 且目标是
    /// 写死的 B 站官方主机，不存在外发用户 Cookie 的路径，故不经凭据门
    /// （<see cref="BiliHeaders.TrustedCookieHosts"/> 的含义是「允许接收 Cookie」，加入该主机等于放开 SESSDATA 的可达范围）
    /// </summary>
    internal static async Task<AccountInfo> ProbeTokenAsync(string appKey, string appSecret, string accessToken, CancellationToken ct)
    {
        // 签名字符串与实际请求串须完全一致，参数按 key 字典序排列
        var query = $"access_key={accessToken}&appkey={appKey}&ts={SignUtil.UnixTimestamp( )}";
        var url = $"{BiliApi.AccountMyInfo}?{query}&sign={SignUtil.AppSign(query, appSecret)}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent", BiliHeaders.UserAgent);
        using var response = await HTTPUtil.AppHttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode( );

        using var doc = JsonDocument.Parse(await HttpTransfer.ReadBodyAsync(response.Content, ct));
        var root = doc.RootElement;
        var code = root.GetProperty("code").GetInt32( );
        return code switch
        {
            0 => ParseMyInfo(root.GetProperty("data")),
            // -101：格式合法但令牌无效 / 已过期；-400：令牌长度或字符集不合规（实测非 32 位 hex 即此码）
            // 两者都是「服务端不接受该凭据」，与探测未完成是两种情况
            -101 or -400 => new AccountInfo(false, "", 0, false, ""),
            _ => throw new InvalidOperationException($"账号信息查询失败：{code} {(root.TryGetProperty("message", out var m) ? m.GetString( ) : "")}")
        };
    }

    /// <summary>
    /// 从 nav 接口的 data 节点解析账号信息（昵称/等级/大会员等）
    /// 各字段均做了缺失保护，避免接口结构变动导致整体解析失败
    /// </summary>
    internal static AccountInfo ParseNav(JsonElement data)
    {
        var isLogin = data.TryGetProperty("isLogin", out var il) && il.GetBoolean( );
        var uname = data.TryGetProperty("uname", out var u) ? (u.GetString( ) ?? "") : "";
        var level = data.TryGetProperty("level_info", out var li) && li.TryGetProperty("current_level", out var cl) ? cl.GetInt32( ) : 0;
        var isVip = false;
        var vipLabel = "";
        if (data.TryGetProperty("vip", out var vip))
        {
            isVip = vip.TryGetProperty("vipStatus", out var vs) && vs.GetInt32( ) == 1;
            if (vip.TryGetProperty("label", out var label) && label.TryGetProperty("text", out var lt))
            {
                vipLabel = lt.GetString( ) ?? "";
            }
        }

        return new AccountInfo(isLogin, uname, level, isVip, vipLabel);
    }

    /// <summary>
    /// 从 account/myinfo 的 data 节点解析账号信息。字段名与 nav 不同（name / level / vip.status）
    /// 但字段一一对应，缺失保护与 <see cref="ParseNav"/> 同规格
    /// </summary>
    internal static AccountInfo ParseMyInfo(JsonElement data)
    {
        // 服务端改字段类型时 GetString / TryGetInt32 会抛，状态查询是尽力而为的独立分支，不得因单字段异常丢掉整行输出
        var uname = data.TryGetProperty("name", out var u) && u.ValueKind == JsonValueKind.String ? (u.GetString( ) ?? "") : "";
        var level = data.TryGetProperty("level", out var l) && l.ValueKind == JsonValueKind.Number && l.TryGetInt32(out var lv) ? lv : 0;
        var isVip = false;
        var vipLabel = "";
        if (data.TryGetProperty("vip", out var vip))
        {
            isVip = vip.TryGetProperty("status", out var vs) && vs.ValueKind == JsonValueKind.Number && vs.TryGetInt32(out var st) && st == 1;
            if (vip.TryGetProperty("label", out var label) && label.TryGetProperty("text", out var lt) && lt.ValueKind == JsonValueKind.String)
            {
                vipLabel = lt.GetString( ) ?? "";
            }
        }

        return new AccountInfo(true, uname, level, isVip, vipLabel);
    }

    // 取 url 末段文件名（去掉扩展名），用于拼接 WBI 原串
    public static string RSubString(string sub)
    {
        sub = sub[(sub.LastIndexOf('/') + 1)..];
        return sub[..sub.LastIndexOf('.')];
    }

    // WBI 固定置换表，把 64 位原串压缩为 32 位 mixin key
    internal static string GetMixinKey(string orig)
    {
        byte[] mixinKeyEncTab =
        [
            46, 47, 18, 2, 53, 8, 23, 32, 15, 50, 10, 31, 58, 3, 45, 35,
            27, 43, 5, 49, 33, 9, 42, 19, 29, 28, 14, 39, 12, 38, 41, 13
        ];

        var tmp = new StringBuilder(32);
        foreach (var index in mixinKeyEncTab)
        {
            tmp.Append(orig[index]);
        }

        return tmp.ToString( );
    }
}
