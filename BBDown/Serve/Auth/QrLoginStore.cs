using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core;
using BBDown.Core.Auth;

using static BBDown.Core.Logger;

namespace BBDown.Serve.Auth;

/// <summary>扫码登录起点结果：Session 非空时二维码已就绪；否则返回 Error（InvalidChannel 区分 400 与 504）。</summary>
internal sealed record StartLoginOutcome(QrLoginSession? Session, string? Error, bool InvalidChannel = false);

/// <summary>
/// 扫码登录会话容器：经 Core Login 编排二维码登录，供 WebUI 经 REST 起点与轮询。
/// 会话有界（并发上限 + 存活时间），超限 / 过期淘汰并取消对应后台登录任务；成功凭据同时写入 BBDown.data（与 CLI / GUI 一致）。
/// </summary>
public sealed class QrLoginStore
{
    private const int MaxSessions = 8;
    private static readonly TimeSpan SessionTtl = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan GenerateTimeout = TimeSpan.FromSeconds(10);

    private readonly ConcurrentDictionary<string, QrLoginSession> sessions = new( );

    /// <summary>起点一次扫码登录：建会话、后台跑 Core 登录编排，待二维码就绪后返回。</summary>
    internal async Task<StartLoginOutcome> StartAsync(string channel, CancellationToken token)
    {
        if (channel is not ("web" or "tv" or "app"))
        {
            return new StartLoginOutcome(null, "无效的登录通道（web / tv / app）", InvalidChannel: true);
        }

        PruneExpired( );
        MakeRoom( );
        var session = new QrLoginSession { Channel = channel };
        sessions[session.Key] = session;
        _ = RunAsync(session, session.TokenSource.Token);

        var completed = await Task.WhenAny(session.QrReady.Task, session.FailedReady.Task, Task.Delay(GenerateTimeout, token));
        if (ReferenceEquals(completed, session.QrReady.Task))
        {
            return new StartLoginOutcome(session, null);
        }

        RemoveSession(session.Key);
        if (ReferenceEquals(completed, session.FailedReady.Task))
        {
            var error = await session.FailedReady.Task;
            return new StartLoginOutcome(null, error ?? "登录二维码生成失败");
        }

        return new StartLoginOutcome(null, "登录二维码生成超时");
    }

    /// <summary>按轮询键取会话；不存在或已过期返回 false。</summary>
    public bool TryGet(string qrcodeKey, out QrLoginSession session)
    {
        session = null!;
        if (!sessions.TryGetValue(qrcodeKey, out var found) || found is null)
        {
            return false;
        }

        if (DateTimeOffset.UtcNow - found.CreatedAt <= SessionTtl)
        {
            session = found;
            return true;
        }

        RemoveSession(qrcodeKey);
        return false;
    }

    private static async Task RunAsync(QrLoginSession session, CancellationToken token)
    {
        try
        {
            switch (session.Channel)
            {
                case "web":
                {
                    var (cookie, refreshToken) = await Login.WebCredentialAsync(ShowQr(session), OnState(session), token);
                    if (cookie is null)
                    {
                        session.MarkExpired( );
                        break;
                    }

                    await CredentialStore.SaveWebCookie(cookie, refreshToken: refreshToken, issueTs: DateTimeOffset.UtcNow.ToUnixTimeSeconds( ));
                    session.Complete(cookie: cookie, refreshToken: refreshToken);
                    await ProbeAccountNameAsync(session, cookie, token);
                    break;
                }
                case "tv":
                case "app":
                    await SaveAccessTokenAsync(session, token);
                    break;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            session.MarkFailed("登录已取消");
        }
        catch (Exception e)
        {
            LogWarn($"扫码登录失败（{session.Channel}）：{e.Message}");
            session.MarkFailed(e.Message);
        }
    }

    private static async Task SaveAccessTokenAsync(QrLoginSession session, CancellationToken token)
    {
        var accessToken = session.Channel == "tv"
            ? await Login.TvCredentialAsync(ShowQr(session), OnState(session), token)
            : await Login.AppCredentialAsync(ShowQr(session), OnState(session), token);
        if (accessToken is null)
        {
            session.MarkExpired( );
            return;
        }

        if (session.Channel == "tv")
        {
            await CredentialStore.SaveTvToken(accessToken, issueTs: DateTimeOffset.UtcNow.ToUnixTimeSeconds( ));
        }
        else
        {
            await CredentialStore.SaveAppToken(accessToken, issueTs: DateTimeOffset.UtcNow.ToUnixTimeSeconds( ));
        }

        session.Complete(accessToken: accessToken);
    }

    /// <summary>成功后打印账号名（best-effort，失败不阻断）。</summary>
    private static async Task ProbeAccountNameAsync(QrLoginSession session, string cookie, CancellationToken token)
    {
        try
        {
            var config = new AppConfig(cookie, "", BiliApi.MainHost, BiliApi.MainHost, BiliApi.TvHost, "", "", "");
            var (info, _) = await Account.ProbeAccountAsync(config, token);
            if (info.IsLogin)
            {
                session.AccountName = info.UserName;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception e)
        {
            LogDebug("登录后账号校验失败（可忽略）：{0}", e.Message);
        }
    }

    private static Func<string, Task> ShowQr(QrLoginSession session)
    {
        return url =>
        {
            session.QrUrl ??= url;
            session.QrReady.TrySetResult(url);
            return Task.CompletedTask;
        };
    }

    private static Action<Login.QrState> OnState(QrLoginSession session)
    {
        return state => session.State = state switch
        {
            Login.QrState.WaitingScan => QrLoginState.WaitingScan,
            Login.QrState.WaitingConfirm => QrLoginState.WaitingConfirm,
            Login.QrState.Expired => QrLoginState.Expired,
            _ => QrLoginState.Success,
        };
    }

    private void PruneExpired( )
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var (key, session) in sessions)
        {
            if (session.IsTerminal || now - session.CreatedAt > SessionTtl)
            {
                RemoveSession(key);
            }
        }
    }

    private void MakeRoom( )
    {
        if (sessions.Count < MaxSessions)
        {
            return;
        }

        PruneExpired( );
        if (sessions.Count >= MaxSessions)
        {
            foreach (var key in sessions.OrderBy(kv => kv.Value.CreatedAt)
                         .Take(sessions.Count - MaxSessions + 1)
                         .Select(kv => kv.Key)
                         .ToList( ))
            {
                RemoveSession(key);
            }
        }
    }

    private void RemoveSession(string key)
    {
        if (sessions.TryRemove(key, out var session))
        {
            session.TokenSource.Cancel( );
            session.TokenSource.Dispose( );
        }
    }
}

/// <summary>单次扫码登录会话（WebUI 轮询目标），凭据仅经成功态回传一次。由 QrLoginStore 持有并淘汰。</summary>
public sealed class QrLoginSession
{
    /// <summary>轮询凭据键（短随机 id，防猜测）。</summary>
    public string Key { get; } = Guid.NewGuid( ).ToString("N")[..12];
    /// <summary>登录通道（web / tv / app）。</summary>
    public required string Channel { get; init; }
    public DateTimeOffset CreatedAt { get; } = DateTimeOffset.UtcNow;
    public QrLoginState State { get; internal set; } = QrLoginState.WaitingScan;
    public string? QrUrl { get; internal set; }
    public string? AccountName { get; internal set; }
    public string? Cookie { get; internal set; }
    public string? AccessToken { get; internal set; }
    public string? RefreshToken { get; internal set; }
    public string? Error { get; internal set; }

    internal bool IsTerminal => State is QrLoginState.Success or QrLoginState.Failed or QrLoginState.Expired;
    internal TaskCompletionSource<string> QrReady { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource<string?> FailedReady { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal CancellationTokenSource TokenSource { get; } = CancellationTokenSource.CreateLinkedTokenSource(AppEnv.CancellationToken);

    internal void Complete(string? cookie = null, string? refreshToken = null, string? accessToken = null)
    {
        State = QrLoginState.Success;
        Cookie = cookie;
        RefreshToken = refreshToken;
        AccessToken = accessToken;
    }

    internal void MarkExpired( )
    {
        State = QrLoginState.Expired;
    }

    internal void MarkFailed(string error)
    {
        State = QrLoginState.Failed;
        Error = error;
    }
}