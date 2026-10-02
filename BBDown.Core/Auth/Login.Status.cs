using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core.Util;

using static BBDown.Core.Logger;
using static BBDown.Core.Util.Utils;

namespace BBDown.Core.Auth;

public static partial class Login
{
    private const string WebChannel = "WEB";
    private const string TvChannel = "TV";
    private const string AppChannel = "APP";

    private const int ChannelCells = 4;

    // 状态词最宽为「探测失败」「凭据无效」（各 4 个 CJK = 8 cell），留 2 cell 间隔与详情分列
    private const int StateCells = 10;

    /// <summary>
    /// 输出三通道登录状态。三通道探测并行发起（网络等待是唯一耗时），渲染按固定顺序，
    /// 输出不随网络返回快慢抖动。返回进程退出码。
    /// </summary>
    public static async Task<int> StatusAsync(CancellationToken token = default)
    {
        var credential = CredentialStore.LoadCredential( );
        var webTask = ProbeWebAsync(credential, token);
        var tvTask = ProbeTokenAsync(TvChannel, BiliApi.TvAppKey, BiliApi.TvAppSecret, credential.TvAccessToken, credential.TvTs, token);
        var appTask = ProbeTokenAsync(AppChannel, BiliApi.PhoneAppKey, BiliApi.PhoneAppSecret, credential.AppAccessToken, credential.AppTs, token);

        var statuses = new[] { await webTask, await tvTask, await appTask };
        foreach (var status in statuses)
        {
            Log(Format(status), time: false);
        }

        return ExitCode(statuses);
    }

    private static async Task<LoginStatus> ProbeWebAsync(CredentialStore.Credential credential, CancellationToken token)
    {
        if (string.IsNullOrEmpty(credential.Cookie))
        {
            return new LoginStatus(WebChannel, false, null, Empty, credential.Ts);
        }

        try
        {
            var cfg = new AppConfig(credential.Cookie, "", BiliApi.MainHost, BiliApi.MainHost, BiliApi.TvHost, "", "", "");
            var (info, _) = await Account.ProbeAsync(cfg, token);
            // 续期提示只在已确认登录后才有意义：未登录时提示「需要续期」会误导用户去续一个已失效的凭据
            var refresh = info.IsLogin && await NeedsCookieRefreshAsync(credential.Cookie, token);
            return new LoginStatus(WebChannel, true, info.IsLogin, info, credential.Ts, refresh);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            LogDebug("WEB 登录态探测失败：{0}", e.Message);
            return new LoginStatus(WebChannel, true, null, Empty, credential.Ts);
        }
    }

    private static async Task<LoginStatus> ProbeTokenAsync(string channel, string appKey, string appSecret, string? accessToken, long? issueTs, CancellationToken token)
    {
        if (string.IsNullOrEmpty(accessToken))
        {
            return new LoginStatus(channel, false, null, Empty, issueTs);
        }

        try
        {
            var info = await Account.ProbeTokenAsync(appKey, appSecret, accessToken, token);
            return new LoginStatus(channel, true, info.IsLogin, info, issueTs);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            LogDebug("{0} 登录态探测失败：{1}", channel, e.Message);
            return new LoginStatus(channel, true, null, Empty, issueTs);
        }
    }

    /// <summary>
    /// 问 passport 是否要求刷新 Cookie，用于在状态行提示续期。失败不影响登录态本身的结论，
    /// 故调用方在已判定登录后才走这一步。
    /// </summary>
    private static async Task<bool> NeedsCookieRefreshAsync(string cookie, CancellationToken token)
    {
        try
        {
            var (needRefresh, _) = await ReadCookieInfoAsync(cookie, token);
            return needRefresh;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            LogDebug("Cookie 续期状态查询失败：{0}", e.Message);
            return false;
        }
    }

    /// <summary>
    /// 状态行渲染。通道名与状态词按 cell 宽度补齐（CJK 占 2 cell），输出中不含任何凭据明文。
    /// </summary>
    internal static string Format(LoginStatus status)
    {
        var (state, detail) = Describe(status);
        return $"{Pad(status.Channel, ChannelCells)}{Pad(state, StateCells)}{detail}";
    }

    // 状态词与详情分离渲染，便于单测分别断言；空详情不产生尾随空格
    private static (string State, string Detail) Describe(LoginStatus status)
    {
        var credential = status.Channel == WebChannel ? "Cookie" : "access_token";
        if (!status.Saved)
        {
            return ("未登录", $"本地无 {credential}");
        }

        if (status.Verified is not { } verified)
        {
            return ("探测失败", "无法连接服务端");
        }

        if (!verified)
        {
            return ("凭据无效", $"本地 {credential} 存在，服务端未认可");
        }

        var vip = status.Account.IsVip ? $" · {status.Account.VipLabel}" : "";
        var detail = $"{status.Account.UserName}（LV{status.Account.Level}{vip}）";
        if (Issued(status.IssueTs) is { } issued)
        {
            detail = $"{detail}    凭据签发 {issued}";
        }

        return ("已登录", status.RefreshPending ? $"{detail} · Cookie 需要续期" : detail);
    }

    // 签发时间未记录时返回 null，调用方据此省略该段
    private static string? Issued(long? issueTs)
    {
        return issueTs is > 0 ? FormatTimeStamp(issueTs.Value, "yyyy-MM-dd HH:mm") : null;
    }

    private static string Pad(string text, int cells)
    {
        var width = CellWidth(text);
        return width >= cells ? text : text + new string(' ', cells - width);
    }

    /// <summary>
    /// 退出码：至少一个通道被服务端确认登录返回 0，否则返回 1。探测失败与未登录同归 1——
    /// 脚本只需判断「能不能用」，原因由状态行文本承载。
    /// </summary>
    internal static int ExitCode(IReadOnlyList<LoginStatus> statuses)
    {
        return statuses.Any(s => s.Verified == true) ? 0 : 1;
    }

    private static readonly AccountInfo Empty = new(false, "", 0, false, "");
}
