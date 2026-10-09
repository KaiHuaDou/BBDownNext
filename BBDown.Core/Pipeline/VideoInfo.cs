using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core.Auth;
using BBDown.Core.Download;
using BBDown.Core.Entity;
using BBDown.Core.Fetcher;
using BBDown.Core.Util;

using static BBDown.Core.Logger;
using static BBDown.Core.Util.Utils;

namespace BBDown.Core.Pipeline;

public static class VideoInfo
{
    // 已续期的 Cookie 集合：每个凭据各享一次续期额度，避免批量下载时每个视频都打 /cookie/info
    // 写者只有 FetchAsync：TryAdd 抢到的那次在续期前占位，取消时摘除条目让后续任务还能重试
    // 摘除按键而非整体清零：进程级单槽会让首个凭据用尽其余任务的机会（serve 下多凭据并存）
    private static readonly ConcurrentDictionary<string, byte> refreshedCookies = new( );

    // nav 探测缓存：按凭据分键，进程内每份凭据只探测一次
    // nav 返回的登录态随 Cookie 变化，wbi 密钥虽与凭据无关但与目标 host 有关，
    // 进程级单槽会把首个调用方的结果套给其余凭据
    // 惰性包在键外：GetOrAdd 的工厂可能被并发执行多次，直接存 Task 会重复打 nav
    private static readonly ConcurrentDictionary<string, Lazy<Task<(AccountInfo Info, string Wbi)>>> accountProbes = new( );

    public static async Task<(DownloadRequest Effective, FetchResult Fetch)> FetchAsync(DownloadRequest myOption, RunConfig runConfig, CancellationToken ct = default)
    {
        var cfg = WorkSetup.ResolveConfig(myOption, myOption.Api);

        // 主动续期 web cookie（best-effort，持有 refresh_token 才尝试；每份凭据一次）
        if (refreshedCookies.TryAdd(cfg.Cookie, 0))
        {
            try
            {
                var newCookie = await Login.TryRefreshWebCookieIfStaleAsync(token: ct);
                if (!string.IsNullOrEmpty(newCookie))
                {
                    cfg = cfg with { Cookie = newCookie };
                }
            }
            catch (OperationCanceledException)
            {
                // 取消不代表已续期，摘除占位：否则本进程内再无人尝试续期这一份凭据
                refreshedCookies.TryRemove(cfg.Cookie, out _);
                throw;
            }
        }

        // nav 无需登录即可返回 wbi 密钥；TV/国际版模式同样会命中 wbi 接口（view、player/wbi/v2）
        // 跳过取密钥会让签名为空而被服务端拒绝。nav 探测与 buvid 拉取互不依赖，并行执行
        Log("检测账号登录...");
        var navTask = EnsureAccountProbedAsync(cfg, ct);
        var buvidTask = Buvid.InitAsync(ct);
        try
        {
            await Task.WhenAll(navTask, buvidTask);
        }
        catch
        {
            // 探测抛异常（网络抖动等）：清空故障 Task，否则会被缓存导致后续所有抓取反复失败无恢复
            InvalidateProbe(cfg, navTask);
            throw;
        }

        var (info, wbi) = await navTask;
        cfg = cfg with { Wbi = wbi };
        // 未拿到 wbi（网络抖动/未登录）时不缓存，允许后续 URL 重试
        if (string.IsNullOrEmpty(wbi))
        {
            InvalidateProbe(cfg, navTask);
        }

        if (myOption.Api == ApiType.Web)
        {
            PrintAccountStatus(info);
        }
        else if (!string.IsNullOrEmpty(cfg.Token))
        {
            Log($"已使用 {myOption.Api.ToString( ).ToUpperInvariant( )} 凭据");
        }

        Log("获取 aid...");
        var id = await InputResolver.ResolveIdAsync(runConfig.Input, cfg, ct);
        Log($"id: {id}");

        // 抓取所用的 API 类型以发起抓取前的选项为准，下游据此保持一致，避免 cheese+intl 被回退后 ApiType 与抓取实情不符
        var fetchApi = myOption.Api;
        (id, var vInfo) = await FetchVideoInfoAsync(id, cfg, fetchApi == ApiType.Intl, ct);
        myOption = NormalizeOptionsAfterFetch(myOption, vInfo);
        PrintVideoSummary(vInfo, myOption);
        PrintPagesInfo(vInfo, myOption);

        return (myOption, new FetchResult(vInfo, cfg, id, fetchApi));
    }

    // nav 探测（登录态与 wbi 密钥）进程内每份凭据仅执行一次；后续调用复用同一 Task，避免批量下载时每个 URL 重复打 nav 接口
    // 共享任务不捕获调用方令牌：抢到槽者的 ct 一旦取消或超时，其余并发任务会一直 await 一个已死任务，
    // 故取消只在 await 处生效；探测失败（wbi 为空）由调用方 InvalidateProbe 触发重试
    private static Task<(AccountInfo Info, string Wbi)> EnsureAccountProbedAsync(AppConfig cfg, CancellationToken ct)
    {
        var probe = accountProbes.GetOrAdd(
            ProbeKey(cfg),
            _ => new Lazy<Task<(AccountInfo Info, string Wbi)>>( ( ) => Account.ProbeAccountAsync(cfg, CancellationToken.None) )).Value;
        return probe.WaitAsync(ct);
    }

    // 仅当缓存中仍是当前探测任务时才清空：并发下他人可能已新建探测，无条件清空会误删其成果
    private static void InvalidateProbe(AppConfig cfg, Task<(AccountInfo Info, string Wbi)> task)
    {
        if (accountProbes.TryGetValue(ProbeKey(cfg), out var cached) && ReferenceEquals(cached.Value, task))
        {
            accountProbes.TryRemove(new KeyValuePair<string, Lazy<Task<(AccountInfo Info, string Wbi)>>>(ProbeKey(cfg), cached));
        }
    }

    // 键取 nav 请求里真正起作用的四项。Area / EpHost / TvHost 不参与 nav，Wbi 此刻尚未填充
    // 分隔符用 NUL：Cookie 与 Token 是 HTTP 头值，不含该字符，拼接不会歧义
    private static string ProbeKey(AppConfig cfg)
    {
        return string.Join('\0', cfg.Cookie, cfg.Token, cfg.Host, cfg.UserAgent);
    }

    private static void PrintAccountStatus(AccountInfo info)
    {
        if (info.IsLogin)
        {
            var vip = info.IsVip ? $" · {info.VipLabel}" : "";
            Log($"已登录：{info.UserName} (LV{info.Level}{vip})");
        }
        else
        {
            LogWarn("你尚未登录 bilibili 账号，解析可能受到限制");
        }
    }

    /// <summary>
    /// 视频信息解析完成后，依据视频属性消解选项冲突
    /// 与 HandleConflictingOptions 分工：后者只处理不依赖视频信息的冲突
    /// 此处处理需要 vInfo 才能判断的冲突
    /// </summary>
    private static DownloadRequest NormalizeOptionsAfterFetch(DownloadRequest myOption, VInfo vInfo)
    {
        if (vInfo.IsSteinGate && myOption.Api == ApiType.Tv)
        {
            Log("视频为互动视频，暂时不支持 TV API，回退到 WEB API。");
            return myOption with { Api = ApiType.Web };
        }

        if (vInfo.IsCheese && myOption.Api == ApiType.Intl)
        {
            LogWarn("课程为国内内容，不支持 INTL API，回退到 WEB API。");
            return myOption with { Api = ApiType.Web };
        }

        return myOption;
    }

    private static async Task<(ResourceId id, VInfo vInfo)> FetchVideoInfoAsync(ResourceId id, AppConfig cfg, bool useIntlApi, CancellationToken ct = default)
    {
        // EP/SS 优先按番剧查找，找不到时由 FetcherRegistry 内部回退到课程 (cheese) 查找
        var vInfo = await FetcherRegistry.FetchAsync(id, cfg, useIntlApi, ct);
        return (id, vInfo);
    }

    private static void PrintVideoSummary(VInfo vInfo, DownloadRequest myOption)
    {
        var title = vInfo.Title;
        var pubTime = vInfo.PubTime;
        LogColor("视频标题：" + title);
        if (pubTime != 0)
        {
            Log("发布时间：" + Utils.FormatTimeStamp(pubTime, "yyyy-MM-dd HH:mm:ss zzz"));
        }

        var bvid = vInfo.PagesInfo.FirstOrDefault( )?.Bvid;
        if (!string.IsNullOrEmpty(bvid) && myOption.Api != ApiType.Intl)
        {
            Log($"视频 URL：{BiliApi.VideoPage}/{bvid}/");
        }

        // 列表型输入（稍后再看 / 收藏夹等）可能混合多个 UP，此时展示首个 ownerMid 会误导
        // 仅当全部视频归属同一 UP 时才显示 UP 主页
        var ownerMids = vInfo.PagesInfo
            .Where(p => !string.IsNullOrEmpty(p.OwnerMid))
            .Select(p => p.OwnerMid)
            .Distinct( )
            .ToList( );
        if (ownerMids.Count == 1)
        {
            Log($"UP 主页：{BiliApi.SpacePage}/{ownerMids[0]}");
        }
    }

    private static void PrintPagesInfo(VInfo vInfo, DownloadRequest myOption)
    {
        var pagesInfo = vInfo.PagesInfo;
        var more = false;
        foreach (var p in pagesInfo)
        {
            if (!myOption.ShowAll)
            {
                if (more && p.Index != pagesInfo.Count)
                {
                    continue;
                }

                if (!more && p.Index > 5)
                {
                    Log("...");
                    more = true;
                    continue;
                }
            }

            Log($"P{p.Index}: [{p.Cid}] [{p.Title}] [{FormatTime(p.Dur)}]");
        }
    }
}
