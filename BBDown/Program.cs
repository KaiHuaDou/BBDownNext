using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
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
    public static string SinglePageDefaultSavePath { get; set; } = "<videoTitle>";
    public static string MultiPageDefaultSavePath { get; set; } = "<videoTitle>/[P<pageNumberWithZero>]<pageTitle>";

    public static readonly string APP_DIR = Path.GetDirectoryName(Environment.ProcessPath)!;

    private static string FormatTimeStamp(long ts, string format)
    {
        try
        {
            return ts == 0 ? "null" : DateTimeOffset.FromUnixTimeSeconds(ts).ToLocalTime( ).ToString(format);
        }
        catch (Exception ex)
        {
            LogError($"格式化日期出错：{ex.Message}。");
            return ts.ToString( );
        }
    }

    [JsonSerializable(typeof(MyOption))]
    [JsonSerializable(typeof(ServeRequestOptions))]
    private sealed partial class MyOptionJsonContext : JsonSerializerContext { }

    private static void Console_CancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        LogWarn("Force Exit...");
        try
        {
            Console.ResetColor( );
            Console.CursorVisible = true;
            if (!OperatingSystem.IsWindows( ))
                System.Diagnostics.Process.Start("stty", "echo");
        }
        catch { }

        Environment.Exit(0);
    }

    public static async Task<int> Main(params string[] args)
    {
        Console.CancelKeyPress += Console_CancelKeyPress;

        var resolvedArgs = new List<string>( );
        var rootCommand = CommandLineInvoker.GetRootCommand(o => RunApp(o, resolvedArgs));
        rootCommand.Description = "BBDown 是一个免费且便捷高效的哔哩哔哩下载/解析软件。";
        rootCommand.TreatUnmatchedTokensAsErrors = false;

        Command loginCommand = new("login", "通过 APP 扫描二维码以登录您的账号");
        loginCommand.SetAction(_ => Login.Web( ));
        rootCommand.Subcommands.Add(loginCommand);

        Command loginTVCommand = new("logintv", "通过 APP 扫描二维码以登录您的 TV 账号");
        loginTVCommand.SetAction(_ => Login.TV( ));
        rootCommand.Subcommands.Add(loginTVCommand);

        Command serverCommand = new("serve", "以服务器模式运行")
        {
            new Option<string>("--listen", "-l")
            {
                Description = "服务器监听地址"
            }
        };
        serverCommand.SetAction(result => StartServer(result.GetValue<string>("--listen")));
        rootCommand.Subcommands.Add(serverCommand);

        var rootResult = rootCommand.Parse(args, new ParserConfiguration( )
        {
            EnablePosixBundling = true,
        });

        // 显式抛出异常
        if (rootResult.Errors.Count != 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine(rootResult.Errors[0].Message);
            Console.ResetColor( );
            Console.Error.WriteLine("请使用 BBDown --help 查看帮助");
            return 1;
        }

        var argsList = new List<string>( );

        foreach (var item in rootResult.CommandResult.Children)
        {
            if (item is ArgumentResult a)
            {
                if (a.Tokens.Count > 0)
                {
                    argsList.Add(a.Tokens[0].Value);
                }
            }
            else if (item is OptionResult o)
            {
                argsList.Add($"--{o.Option.Name}");
                argsList.AddRange(o.Tokens.Select(t => t.Value));
            }
        }

        resolvedArgs.AddRange(argsList);

        if (argsList.Contains("--debug"))
        {
            Config.SetDebugLog(true);
        }

        Console.BackgroundColor = ConsoleColor.DarkBlue;
        Console.ForegroundColor = ConsoleColor.White;
        var ver = System.Reflection.Assembly.GetExecutingAssembly( ).GetName( ).Version!;
        Console.Write($"BBDown version {ver.Major}.{ver.Minor}.{ver.Build}, Bilibili Downloader.\r\n");
        Console.ResetColor( );
        Console.WriteLine( );

        //处理配置文件
        BBDownConfigParser.HandleConfig(argsList, rootCommand);

        return await rootResult.InvokeAsync(new InvocationConfiguration( ) { EnableDefaultExceptionHandler = false });
    }

    private static Task RunApp(MyOption myOption, List<string> argsList)
    {
        Log($"任务开始时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        return DoWorkAsync(myOption, argsList);
    }

    private static void StartServer(string? listenUrl)
    {
        var defaultListenUrl = "http://0.0.0.0:23333";
        var server = new BBDownApiServer( );
        server.SetUpServer( );
#pragma warning disable CA2234 // 保留 Run(string) 内的 URL 合法性校验与友好退出
        server.Run(string.IsNullOrEmpty(listenUrl) ? defaultListenUrl : listenUrl);
#pragma warning restore CA2234
    }

    public static WorkContext BuildWorkContext(MyOption myOption, List<string>? argsList = null)
    {
        //处理冲突选项
        HandleConflictingOptions(myOption);

        //寻找并设置所需的二进制文件路径
        FindBinaries(myOption);

        //切换工作目录
        ChangeWorkingDir(myOption);

        //解析优先级
        var (encodingPriority, firstEncoding) = ParseEncodingPriority(myOption);
        var dfnPriority = ParseDfnPriority(myOption);

        //用户同时指定了编码与清晰度优先级时，以命令行书写的先后为准
        //(serve 模式由 JSON 注入参数，无命令行顺序，默认清晰度优先)
        var encodingFirst = argsList != null
            && argsList.Contains("--encoding-priority")
            && argsList.Contains("--dfn-priority")
            && argsList.FindIndex(s => s == "--encoding-priority")
               < argsList.FindIndex(s => s == "--dfn-priority");

        //优先使用用户设置的 UA
        HTTPUtil.UserAgent = string.IsNullOrEmpty(myOption.UserAgent) ? HTTPUtil.UserAgent : myOption.UserAgent;

        var downloadDanmaku = myOption.DownloadDanmaku || myOption.DanmakuOnly;
        var downloadDanmakuFormats = ParseDownloadDanmakuFormats(myOption);

        var input = myOption.Url;
        var savePathFormat = myOption.FilePattern;
        var lang = myOption.Language;
        var delay = int.TryParse(myOption.DelayPerPage, out var delayValue) ? delayValue : 0;
        Config.SetDebugLog(myOption.Debug);

        LogDebug("AppDirectory: {0}", APP_DIR);
        LogDebug("运行参数：{0}", JsonSerializer.Serialize(myOption, MyOptionJsonContext.Default.MyOption));
        return new WorkContext(
            EncodingPriority: encodingPriority,
            DfnPriority: dfnPriority,
            FirstEncoding: firstEncoding,
            EncodingFirst: encodingFirst,
            DownloadDanmaku: downloadDanmaku,
            DownloadDanmakuFormats: downloadDanmakuFormats,
            Input: input,
            SavePathFormat: savePathFormat,
            Lang: lang,
            Delay: delay,
            FetchedAid: "",
            VInfo: null,
            ApiType: "",
            Cfg: default);
    }

    public static async Task<WorkContext> GetVideoInfoAsync(MyOption myOption, WorkContext ctx)
    {
        // 加载认证信息
        var (cookie, token) = CredentialStore.LoadAll(myOption.Cookie, myOption.AccessToken, myOption.UseTvApi, myOption.UseAppApi);

        var cfg = new AppConfig(cookie, token, myOption.Host, myOption.EpHost, myOption.TvHost, myOption.Area, "");

        // 检测是否登录了账号
        if (myOption is { UseIntlApi: false, UseTvApi: false } && cfg.Area is { Length: 0 })
        {
            Log("检测账号登录...");
            var (info, wbi) = await ProbeAccountAsync(cfg);
            cfg = cfg with { Wbi = wbi };
            PrintAccountStatus(info);
        }
        else if (!string.IsNullOrEmpty(token))
        {
            Log($"已使用 {DetermineApiType(myOption)} 凭据");
        }

        Log("获取 aid...");
        var aid = await GetAvIdAsync(ctx.Input, cfg);
        Log($"获取 aid 结束：{aid}");

        if (string.IsNullOrEmpty(aid))
        {
            throw new ArgumentException("输入有误");
        }

        (aid, var vInfo) = await FetchVideoInfoAsync(aid, cfg, myOption.UseIntlApi);
        NormalizeOptionsAfterFetch(myOption, vInfo);
        PrintVideoSummary(vInfo, myOption);
        var apiType = DetermineApiType(myOption);
        PrintPagesInfo(vInfo, myOption);

        return ctx with { FetchedAid = aid, VInfo = vInfo, ApiType = apiType, Cfg = cfg };
    }

    private static void PrintAccountStatus(AccountInfo info)
    {
        if (info.IsLogin)
        {
            var vip = info.IsVip ? $" · {info.VipLabel}" : "";
            Log($"已登录：{info.UserName}（LV{info.Level}{vip}）");
        }
        else
        {
            LogWarn("你尚未登录 B 站账号，解析可能受到限制");
        }
    }

    /// <summary>
    /// 视频信息解析完成后，依据视频属性消解选项冲突。
    /// 与 HandleConflictingOptions 分工：后者只处理不依赖视频信息的冲突，
    /// 此处处理需要 vInfo 才能判断的冲突 (如互动视频不支持 TV 下载)。
    /// </summary>
    private static void NormalizeOptionsAfterFetch(MyOption myOption, VInfo vInfo)
    {
        if (vInfo.IsSteinGate && myOption.UseTvApi)
        {
            Log("视频为互动视频，暂时不支持 TV 下载，修改为默认下载。");
            myOption.UseTvApi = false;
        }
    }

    private static async Task<(string aid, VInfo vInfo)> FetchVideoInfoAsync(string aid, AppConfig cfg, bool useIntlApi)
    {
        // EP/SS 优先按番剧查找，找不到时由 FetcherRegistry 内部回退到课程 (cheese) 查找
        var vInfo = await FetcherRegistry.FetchAsync(aid, cfg, useIntlApi);
        return (aid, vInfo);
    }

    private static void PrintVideoSummary(VInfo vInfo, MyOption myOption)
    {
        var title = vInfo.Title;
        var pubTime = vInfo.PubTime;
        LogColor("视频标题：" + title);
        if (pubTime != 0)
        {
            Log("发布时间：" + FormatTimeStamp(pubTime, "yyyy-MM-dd HH:mm:ss zzz"));
        }

        var bvid = vInfo.PagesInfo.FirstOrDefault( )?.bvid;
        if (!string.IsNullOrEmpty(bvid) && !myOption.UseIntlApi)
        {
            Log($"视频 URL：https://www.bilibili.com/video/{bvid}/");
        }

        var mid = vInfo.PagesInfo.FirstOrDefault(p => !string.IsNullOrEmpty(p.ownerMid))?.ownerMid;
        if (!string.IsNullOrEmpty(mid))
        {
            Log($"UP 主页：https://space.bilibili.com/{mid}");
        }
    }

    internal static string DetermineApiType(MyOption myOption)
        => myOption.UseTvApi ? "TV" : (myOption.UseAppApi ? "APP" : (myOption.UseIntlApi ? "INTL" : "WEB"));

    private static void PrintPagesInfo(VInfo vInfo, MyOption myOption)
    {
        //打印分 P 信息
        var pagesInfo = vInfo.PagesInfo;
        var more = false;
        foreach (var p in pagesInfo)
        {
            if (!myOption.ShowAll)
            {
                if (more && p.index != pagesInfo.Count) continue;
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

    private static async Task DoWorkAsync(MyOption myOption, List<string>? argsList = null)
    {
        try
        {
            var ctx = BuildWorkContext(myOption, argsList);
            ctx = await GetVideoInfoAsync(myOption, ctx);
            await DownloadPagesAsync(myOption, ctx);
        }
        catch (Exception e)
        {
            Console.BackgroundColor = ConsoleColor.Red;
            Console.ForegroundColor = ConsoleColor.White;
            var msg = Config.DebugLog ? e.ToString( ) : e.Message;
            Console.Write($"{msg}{Environment.NewLine}请尝试升级到最新版本后重试！");
            Console.ResetColor( );
            Console.WriteLine( );
            Thread.Sleep(1);
            Environment.Exit(1);
        }
    }
}
