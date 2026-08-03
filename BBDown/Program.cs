using System;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core;
using BBDown.Core.Entity;
using BBDown.Core.Fetcher;
using BBDown.Core.Util;

using static BBDown.Core.Logger;
using static BBDown.Utils;

namespace BBDown;

internal sealed partial class Program
{
    private const string BACKUP_HOST = "upos-sz-mirrorcoso1.bilivideo.com";
    public static string SinglePageDefaultSavePath { get; } = "<videoTitle>";
    public static string MultiPageDefaultSavePath { get; } = "<videoTitle>/[P<pageNumberWithZero>]<pageTitle>";

    // AppContext.BaseDirectory 指向入口程序集所在目录；Environment.ProcessPath 在 `dotnet BBDown.dll` 下返回宿主路径，会写错位置（P1-13）
    public static readonly string AppDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);

    // 全局取消源：Ctrl+C 时取消，令牌沿 Fetcher → Parser → HTTP → 下载 → 外部进程 全链路透传
    private static readonly CancellationTokenSource cancelSource = new( );
    internal static CancellationToken CancellationToken => cancelSource.Token;

    // Web Cookie 主动续期只跑一次，避免批量下载时每个视频都打 /cookie/info
    private static int cookieRefreshed;

    // nav 探测（wbi 密钥）缓存：进程内只探测一次，批量下载不再逐 URL 打 nav 接口
    private static Task<(AccountInfo Info, string Wbi)>? accountProbeTask;

    private static void Console_CancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        // 抑制运行时默认的进程终止，改为靠令牌优雅取消
        e.Cancel = true;
        LogWarn("收到取消信号，正在安全退出...");
        try
        {
            Console.ResetColor( );
            Console.CursorVisible = true;
            if (!OperatingSystem.IsWindows( ))
            {
                System.Diagnostics.Process.Start("stty", "echo");
            }
        }
        catch { }

        cancelSource.Cancel( );
    }

    public static async Task<int> Main(params string[] args)
    {
        Console.CancelKeyPress += Console_CancelKeyPress;

        var rootCommand = CommandLineInvoker.GetRootCommand(RunApp);
        rootCommand.Description = "BBDown 是一个免费、便捷且高效的哔哩哔哩视频下载 / 解析命令行工具。";
        rootCommand.TreatUnmatchedTokensAsErrors = false;

        var loginTvOption = new Option<bool>("--tv") { Description = "登录 TV 账号（默认登录 WEB 账号）" };
        var loginAppOption = new Option<bool>("--app") { Description = "登录 APP 账号（默认登录 WEB 账号）" };
        Command loginCommand = new("login", "通过 APP 扫描二维码以登录您的账号（默认 WEB，加 --tv 登录 TV，加 --app 登录 APP）");
        loginCommand.Options.Add(loginTvOption);
        loginCommand.Options.Add(loginAppOption);
        loginCommand.SetAction(result =>
        {
            if (result.GetValue(loginTvOption))
            {
                return Login.TV( );
            }

            if (result.GetValue(loginAppOption))
            {
                return Login.App( );
            }

            return Login.Web( );
        });
        rootCommand.Subcommands.Add(loginCommand);

        Command serverCommand = new("serve", "以服务器模式运行")
        {
            new Option<string>("--listen", "-l")
            {
                Description = "服务器监听地址，默认 http://127.0.0.1:23333（仅本机可访问，无需令牌）；绑定到非回环地址时会强制要求令牌"
            },
            new Option<string>("--serve-token")
            {
                Description = "serve 模式鉴权令牌；未提供且绑定到非回环地址时自动生成并打印，客户端需带 X-BBDown-Token 头或 ?token= 查询参数"
            },
            new Option<string>("--work-dir")
            {
                Description = "所有任务的工作目录，请求中的同名字段会被忽略"
            }
        };
        serverCommand.SetAction(result => StartServer(result.GetValue<string>("--listen"), result.GetValue<string>("--work-dir"), result.GetValue<string>("--serve-token")));
        rootCommand.Subcommands.Add(serverCommand);

        var parserConfiguration = new ParserConfiguration( )
        {
            EnablePosixBundling = true,
        };

        var rootResult = rootCommand.Parse(args, parserConfiguration);

        Console.BackgroundColor = ConsoleColor.DarkBlue;
        Console.ForegroundColor = ConsoleColor.White;
        var ver = System.Reflection.Assembly.GetExecutingAssembly( ).GetName( ).Version!;
        Console.Write($"BBDown version {ver.Major}.{ver.Minor}.{ver.Build}, Bilibili Downloader.\r\n");
        Console.ResetColor( );
        Console.WriteLine( );

        // 配置文件只补齐命令行未显式指定的选项，补齐后需重新解析一次
        if (rootResult.CommandResult.Command == rootCommand)
        {
            var mergedArgs = ConfigParser.MergeWithConfig(args, rootResult, rootCommand);
            if (!ReferenceEquals(mergedArgs, args))
            {
                rootResult = rootCommand.Parse(mergedArgs, parserConfiguration);
            }

            // 命令行与配置文件都没给出视频地址时，打印用法而不是抛「缺少必需参数」（--help/--version 不产生错误，仍走原流程）
            if (rootResult.Errors.Count > 0 && !HasUrlArgument(rootResult))
            {
                PrintUsageExample( );
                return 0;
            }
        }

        if (!TryReportParseErrors(rootResult))
        {
            return 1;
        }

        return await rootResult.InvokeAsync(new InvocationConfiguration( ) { EnableDefaultExceptionHandler = true });
    }

    private static bool HasUrlArgument(ParseResult parseResult)
    {
        return parseResult.CommandResult.Children
            .OfType<ArgumentResult>( )
            .Any(a => a.Argument.Name == "url" && a.Tokens.Count > 0);
    }

    private static bool TryReportParseErrors(ParseResult parseResult)
    {
        if (parseResult.Errors.Count == 0)
        {
            return true;
        }

        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine(parseResult.Errors[0].Message);
        Console.ResetColor( );
        Console.Error.WriteLine("请使用 BBDown --help 查看帮助");
        return false;
    }

    private static void PrintUsageExample( )
    {
        Console.WriteLine("""
        BBDown 哔哩哔哩下载器

        用法示例：
          BBDown <视频地址>                下载视频（支持 av / BV / EP / SS）
          BBDown <视频地址> -p 1-5         仅下载第 1~5 集
          BBDown <视频地址> --audio-only   仅下载音频
          BBDown --help                    查看全部参数说明
        """);
    }

    private static Task<int> RunApp(DownloadOptions myOption)
    {
        Log($"任务开始时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        return DoWorkAsync(myOption, cancelSource.Token);
    }

    private static void StartServer(string? listenUrl, string? workDir, string? serveToken = null)
    {
        const string DefaultListenUrl = "http://127.0.0.1:23333";
        var server = new BBDownApiServer( );
        server.SetUpServer(workDir, serveToken: serveToken);
#pragma warning disable CA2234 // 保留 Run(string) 内的 URL 合法性校验与友好退出
        server.Run(string.IsNullOrEmpty(listenUrl) ? DefaultListenUrl : listenUrl);
#pragma warning restore CA2234
    }

    public static WorkContext BuildWorkContext(DownloadOptions myOption)
    {
        Config.SetDebugLog(myOption.Debug);

        // 处理冲突选项
        HandleConflictingOptions(myOption);

        // 寻找并设置所需的二进制文件路径
        FindBinaries(myOption);

        // 确定本次任务的工作目录（不修改进程全局 CurrentDirectory，serve 模式下多任务会互相踩踏）
        var workDir = ResolveWorkDir(myOption);

        // 解析优先级
        var (encodingPriority, firstEncoding) = ParseEncodingPriority(myOption);
        var dfnPriority = ParseDfnPriority(myOption);

        // 优先使用用户设置的 UA
        if (!string.IsNullOrEmpty(myOption.UserAgent))
        {
            HTTPUtil.SetUserAgent(myOption.UserAgent);
        }

        var downloadDanmaku = myOption.DownloadDanmaku || myOption.DanmakuOnly;
        var downloadDanmakuFormats = ParseDownloadDanmakuFormats(myOption);

        var input = myOption.Url;
        var savePathFormat = myOption.FilePattern;
        var lang = myOption.Lang;
        var delay = int.TryParse(myOption.DelayPerPage, out var delayValue) ? delayValue : 0;

        LogDebug("AppDirectory: {0}", AppDir);
        LogDebug("运行参数：{0}", JsonSerializer.Serialize(myOption.WithSecretsRedacted( ), DownloadOptionsJsonContext.Default.DownloadOptions));
        return new WorkContext(
            EncodingPriority: encodingPriority,
            DfnPriority: dfnPriority,
            FirstEncoding: firstEncoding,
            EncodingFirst: myOption.EncodingFirst,
            DownloadDanmaku: downloadDanmaku,
            DownloadDanmakuFormats: downloadDanmakuFormats,
            Input: input,
            SavePathFormat: savePathFormat,
            Lang: lang,
            Delay: delay,
            FetchedAid: "",
            VInfo: null,
            ApiType: "",
            Cfg: AppConfig.Empty,
            WorkDir: workDir);
    }

    public static async Task<WorkContext> GetVideoInfoAsync(DownloadOptions myOption, WorkContext ctx, CancellationToken ct = default)
    {
        // 加载认证信息
        var (cookie, token) = CredentialStore.LoadAll(myOption.Cookie, myOption.AccessToken, myOption.UseTvApi, myOption.UseAppApi);

        // 主动续期 web cookie（best-effort，持有 refresh_token 才尝试；进程内仅一次）
        if (Interlocked.CompareExchange(ref cookieRefreshed, 1, 0) == 0)
        {
            cookie = await Login.TryRefreshWebCookieIfStaleAsync(ct: ct);
        }

        var cfg = new AppConfig(cookie, token, myOption.Host, myOption.EpHost, myOption.TvHost, myOption.Area, "");

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

        await Buvid.InitAsync(ct);
        Log("获取 aid...");
        var aid = await InputResolver.GetAvIdAsync(ctx.Input, cfg);
        Log($"aid: {aid}");

        if (string.IsNullOrEmpty(aid))
        {
            throw new ArgumentException("aid 无效");
        }

        (aid, var vInfo) = await FetchVideoInfoAsync(aid, cfg, myOption.UseIntlApi, ct);
        NormalizeOptionsAfterFetch(myOption, vInfo);
        PrintVideoSummary(vInfo, myOption);
        var apiType = DetermineApiType(myOption);
        PrintPagesInfo(vInfo, myOption);

        return ctx with { FetchedAid = aid, VInfo = vInfo, ApiType = apiType, Cfg = cfg };
    }

    // nav 探测（wbi 密钥）进程内仅执行一次；后续调用复用同一 Task，避免批量下载时每个 URL 重复打 nav 接口。
    // 探测失败（wbi 为空）由调用方清空 accountProbeTask 触发重试。
    private static Task<(AccountInfo Info, string Wbi)> EnsureAccountProbedAsync(Core.AppConfig cfg, CancellationToken ct)
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
    private static void NormalizeOptionsAfterFetch(DownloadOptions myOption, VInfo vInfo)
    {
        if (vInfo.IsSteinGate && myOption.UseTvApi)
        {
            Log("视频为互动视频，暂时不支持 TV API，回退到 WEB API。");
            myOption.UseTvApi = false;
        }

        if (vInfo.IsCheese && myOption.UseIntlApi)
        {
            LogWarn("课程为国内内容，不支持 INTL API，回退到 WEB API。");
            myOption.UseIntlApi = false;
        }
    }

    private static async Task<(string aid, VInfo vInfo)> FetchVideoInfoAsync(string aid, AppConfig cfg, bool useIntlApi, CancellationToken ct = default)
    {
        // EP/SS 优先按番剧查找，找不到时由 FetcherRegistry 内部回退到课程 (cheese) 查找
        var vInfo = await FetcherRegistry.FetchAsync(aid, cfg, useIntlApi, ct);
        return (aid, vInfo);
    }

    private static void PrintVideoSummary(VInfo vInfo, DownloadOptions myOption)
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

    internal static string DetermineApiType(DownloadOptions myOption)
    {
        return myOption.UseTvApi ? "TV" : (myOption.UseAppApi ? "APP" : (myOption.UseIntlApi ? "INTL" : "WEB"));
    }

    private static void PrintPagesInfo(VInfo vInfo, DownloadOptions myOption)
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

    private static async Task<int> DoWorkAsync(DownloadOptions myOption, CancellationToken ct = default)
    {
        try
        {
            var ctx = BuildWorkContext(myOption);
            ctx = await GetVideoInfoAsync(myOption, ctx, ct);
            await DownloadPagesAsync(myOption, ctx, relatedTask: null, ct);
            return 0;
        }
        catch (OperationCanceledException)
        {
            LogWarn("下载已取消。已下载的部分会保留在临时文件中，重新运行命令可断点续传。");
            return 130;
        }
        catch (Exception e)
        {
            Console.BackgroundColor = ConsoleColor.Red;
            Console.ForegroundColor = ConsoleColor.White;
            var msg = Config.DebugLog ? e.ToString( ) : e.Message;
            Console.Write($"{msg}{Environment.NewLine}请升级到最新版本后重试。");
            Console.ResetColor( );
            Console.WriteLine( );
            return 1;
        }
    }
}
