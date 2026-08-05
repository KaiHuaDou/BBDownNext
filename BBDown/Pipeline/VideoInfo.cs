using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Auth;
using BBDown.Core;
using BBDown.Core.Entity;
using BBDown.Core.Fetcher;
using BBDown.Util;

using static BBDown.Core.Logger;
using static BBDown.Util.Utils;

namespace BBDown.Pipeline;

/// <summary>
/// <see cref="VideoInfo.FetchAsync"/> 解析出的「跑中才得到」的结果：视频信息、运行配置、aid、api 类型。
/// 由调用方（<see cref="PageQueue.RunAsync"/>）与 <see cref="RunConfig"/> 组装进 <see cref="WorkContext"/>，不作为上下文字段回填（C5）。
/// </summary>
internal sealed record FetchResult(
    VInfo VInfo,
    AppConfig Cfg,
    string FetchedAid,
    string ApiType);

internal static class VideoInfo
{
    // Web Cookie 主动续期只跑一次，避免批量下载时每个视频都打 /cookie/info
    private static int cookieRefreshed;

    // nav 探测（wbi 密钥）缓存：进程内只探测一次，批量下载不再逐 URL 打 nav 接口
    private static Task<(AccountInfo Info, string Wbi)>? accountProbeTask;

    public static async Task<(DownloadRequest Effective, FetchResult Fetch)> FetchAsync(DownloadRequest myOption, RunConfig runConfig, CancellationToken ct = default)
    {
        // 加载认证信息
        var (cookie, token) = CredentialStore.LoadAll(myOption.Cookie, myOption.AccessToken, myOption.UseTvApi, myOption.UseAppApi);

        // 主动续期 web cookie（best-effort，持有 refresh_token 才尝试；进程内仅一次）
        if (Interlocked.CompareExchange(ref cookieRefreshed, 1, 0) == 0)
        {
            cookie = await Login.TryRefreshWebCookieIfStaleAsync(ct: ct);
        }

        // host 为空串/空白时回落官方默认，避免拼出 https:///... 抛不可读的 UriFormatException（§2.5）
        var host = string.IsNullOrWhiteSpace(myOption.Host) ? BiliApi.MainHost : myOption.Host.Trim( );
        var epHost = string.IsNullOrWhiteSpace(myOption.EpHost) ? BiliApi.MainHost : myOption.EpHost.Trim( );
        var tvHost = string.IsNullOrWhiteSpace(myOption.TvHost) ? BiliApi.TvHost : myOption.TvHost.Trim( );
        var cfg = new AppConfig(cookie, token, host, epHost, tvHost, myOption.Area, "");

        // nav 无需登录即可返回 wbi 密钥；TV/国际版模式同样会命中 wbi 接口（view、player/wbi/v2），
        // 跳过取密钥会让签名为空而被服务端拒绝（P1-27）。nav 探测与 buvid 拉取互不依赖，并行执行；
        // nav 结果进程内只探测一次（accountProbeTask 缓存），批量下载不再逐 URL 打 nav 接口。
        Log("检测账号登录...");
        var navTask = EnsureAccountProbedAsync(cfg, ct);
        var buvidTask = Buvid.InitAsync(ct);
        await Task.WhenAll(navTask, buvidTask);
        var (info, wbi) = await navTask;
        cfg = cfg with { Wbi = wbi };
        // 未拿到 wbi（网络抖动/未登录）时不缓存，允许后续 URL 重试
        if (string.IsNullOrEmpty(wbi))
        {
            accountProbeTask = null;
        }

        if (myOption is { UseIntlApi: false, UseTvApi: false })
        {
            PrintAccountStatus(info);
        }
        else if (!string.IsNullOrEmpty(token))
        {
            Log($"已使用 {DetermineApiType(myOption)} 凭据");
        }

        Log("获取 aid...");
        var aid = await InputResolver.GetAvIdAsync(runConfig.Input, cfg);
        Log($"aid: {aid}");

        if (string.IsNullOrEmpty(aid))
        {
            throw new ArgumentException("aid 无效");
        }

        (aid, var vInfo) = await FetchVideoInfoAsync(aid, cfg, myOption.UseIntlApi, ct);
        myOption = NormalizeOptionsAfterFetch(myOption, vInfo);
        PrintVideoSummary(vInfo, myOption);
        var apiType = DetermineApiType(myOption);
        PrintPagesInfo(vInfo, myOption);

        return (myOption, new FetchResult(vInfo, cfg, aid, apiType));
    }

    // nav 探测（wbi 密钥）进程内仅执行一次；后续调用复用同一 Task，避免批量下载时每个 URL 重复打 nav 接口。
    // 探测失败（wbi 为空）由调用方清空 accountProbeTask 触发重试。
    private static Task<(AccountInfo Info, string Wbi)> EnsureAccountProbedAsync(AppConfig cfg, CancellationToken ct)
    {
        var existing = accountProbeTask;
        if (existing is null)
        {
            var created = Account.ProbeAccountAsync(cfg, ct);
            existing = Interlocked.CompareExchange(ref accountProbeTask, created, null) ?? created;
        }

        return existing;
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
    /// 视频信息解析完成后，依据视频属性消解选项冲突。
    /// 与 HandleConflictingOptions 分工：后者只处理不依赖视频信息的冲突，
    /// 此处处理需要 vInfo 才能判断的冲突
    /// </summary>
    private static DownloadRequest NormalizeOptionsAfterFetch(DownloadRequest myOption, VInfo vInfo)
    {
        if (vInfo.IsSteinGate && myOption.UseTvApi)
        {
            Log("视频为互动视频，暂时不支持 TV API，回退到 WEB API。");
            return myOption with { UseTvApi = false };
        }

        if (vInfo.IsCheese && myOption.UseIntlApi)
        {
            LogWarn("课程为国内内容，不支持 INTL API，回退到 WEB API。");
            return myOption with { UseIntlApi = false };
        }

        return myOption;
    }

    private static async Task<(string aid, VInfo vInfo)> FetchVideoInfoAsync(string aid, AppConfig cfg, bool useIntlApi, CancellationToken ct = default)
    {
        // EP/SS 优先按番剧查找，找不到时由 FetcherRegistry 内部回退到课程 (cheese) 查找
        var vInfo = await FetcherRegistry.FetchAsync(aid, cfg, useIntlApi, ct);
        return (aid, vInfo);
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

        var bvid = vInfo.PagesInfo.FirstOrDefault( )?.bvid;
        if (!string.IsNullOrEmpty(bvid) && !myOption.UseIntlApi)
        {
            Log($"视频 URL：{BiliApi.VideoPage}/{bvid}/");
        }

        var mid = vInfo.PagesInfo.FirstOrDefault(p => !string.IsNullOrEmpty(p.ownerMid))?.ownerMid;
        if (!string.IsNullOrEmpty(mid))
        {
            Log($"UP 主页：{BiliApi.SpacePage}/{mid}");
        }
    }

    internal static string DetermineApiType(DownloadRequest myOption)
    {
        return myOption.UseTvApi ? "TV" : (myOption.UseAppApi ? "APP" : (myOption.UseIntlApi ? "INTL" : "WEB"));
    }

    private static void PrintPagesInfo(VInfo vInfo, DownloadRequest myOption)
    {
        //打印分 P 信息
        var pagesInfo = vInfo.PagesInfo;
        var more = false;
        foreach (var p in pagesInfo)
        {
            if (!myOption.ShowAll)
            {
                if (more && p.index != pagesInfo.Count)
                {
                    continue;
                }

                if (!more && p.index > 5)
                {
                    Log("...");
                    more = true;
                    continue;
                }
            }

            Log($"P{p.index}: [{p.cid}] [{p.title}] [{FormatTime(p.dur)}]");
        }
    }
}
