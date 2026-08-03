using System;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core;

using static BBDown.Core.Logger;
using static BBDown.Core.Util.HTTPUtil;

namespace BBDown;

/// <summary>
/// 账号探测与 WBI 密钥派生：nav 接口解析账号信息 + 由 img/sub url 派生 WBI mixin key。
/// </summary>
internal static class Account
{
    public static async Task<(AccountInfo Info, string Wbi)> ProbeAccountAsync(Core.AppConfig cfg, CancellationToken ct = default)
    {
        try
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
        catch (Exception ex)
        {
            LogDebug("获取账号信息失败: {0}", ex.Message);
            return (new AccountInfo(false, "", 0, false, ""), "");
        }
    }

    /// <summary>
    /// 从 nav 接口的 data 节点解析账号信息（昵称/等级/大会员等）。
    /// 各字段均做了缺失保护，避免接口结构变动导致整体解析失败。
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
