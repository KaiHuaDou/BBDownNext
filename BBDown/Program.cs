using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core;
using BBDown.Core.Entity;
using BBDown.Core.Fetcher;
using BBDown.Core.Util;

using static BBDown.BBDownDownloadUtil;
using static BBDown.Core.Entity.Entity;
using static BBDown.Core.Logger;
using static BBDown.Core.Parser;
using static BBDown.Utils;

namespace BBDown;

internal partial class Program
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
            LogError($"格式化日期出错: {ex.Message}");
            return ts.ToString( );
        }
    }

    [JsonSerializable(typeof(MyOption))]
    [JsonSerializable(typeof(ServeRequestOptions))]
    private partial class MyOptionJsonContext : JsonSerializerContext { }

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

        RootCommand rootCommand = CommandLineInvoker.GetRootCommand(RunApp);
        rootCommand.Description = "BBDown是一个免费且便捷高效的哔哩哔哩下载/解析软件.";
        rootCommand.TreatUnmatchedTokensAsErrors = false;

        Command loginCommand = new("login", "通过APP扫描二维码以登录您的WEB账号");
        loginCommand.SetAction(_ => Login.Web( ));
        rootCommand.Subcommands.Add(loginCommand);

        Command loginTVCommand = new("logintv", "通过APP扫描二维码以登录您的TV账号");
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

        ParseResult rootResult = rootCommand.Parse(args, new ParserConfiguration( )
        {
            EnablePosixBundling = true,
        });

        // 显式抛出异常
        if (rootResult.Errors.Any( ))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine(rootResult.Errors[0].Message);
            Console.ResetColor( );
            Console.Error.WriteLine("请使用 BBDown --help 查看帮助");
            return 1;
        }

        var argsList = new List<string>( );

        foreach (SymbolResult item in rootResult.CommandResult.Children)
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

        if (argsList.Contains("--debug"))
        {
            Config.DEBUG_LOG = true;
        }

        Console.BackgroundColor = ConsoleColor.DarkBlue;
        Console.ForegroundColor = ConsoleColor.White;
        Version ver = System.Reflection.Assembly.GetExecutingAssembly( ).GetName( ).Version!;
        Console.Write($"BBDown version {ver.Major}.{ver.Minor}.{ver.Build}, Bilibili Downloader.\r\n");
        Console.ResetColor( );
        Console.WriteLine( );

        //处理配置文件
        BBDownConfigParser.HandleConfig(argsList, rootCommand);

        return await rootResult.InvokeAsync(new InvocationConfiguration( ) { EnableDefaultExceptionHandler = false });
    }

    private static Task RunApp(MyOption myOption)
    {
        return DoWorkAsync(myOption);
    }

    private static void StartServer(string? listenUrl)
    {
        var defaultListenUrl = "http://0.0.0.0:23333";
        var server = new BBDownApiServer( );
        server.SetUpServer( );
        server.Run(string.IsNullOrEmpty(listenUrl) ? defaultListenUrl : listenUrl);
    }

    public static (Dictionary<string, byte> encodingPriority, Dictionary<string, int> dfnPriority, string firstEncoding,
        bool downloadDanmaku, BBDownDanmakuFormat[] downloadDanmakuFormats, string input, string savePathFormat, string lang, string aidOri, int delay)
        SetUpWork(MyOption myOption)
    {
        //处理废弃选项
        HandleDeprecatedOptions(myOption);

        //处理冲突选项
        HandleConflictingOptions(myOption);

        //寻找并设置所需的二进制文件路径
        FindBinaries(myOption);

        //切换工作目录
        ChangeWorkingDir(myOption);

        //解析优先级
        Dictionary<string, byte> encodingPriority = ParseEncodingPriority(myOption, out var firstEncoding);
        Dictionary<string, int> dfnPriority = ParseDfnPriority(myOption);

        //优先使用用户设置的UA
        HTTPUtil.UserAgent = string.IsNullOrEmpty(myOption.UserAgent) ? HTTPUtil.UserAgent : myOption.UserAgent;

        var downloadDanmaku = myOption.DownloadDanmaku || myOption.DanmakuOnly;
        BBDownDanmakuFormat[] downloadDanmakuFormats = ParseDownloadDanmakuFormats(myOption);

        var input = myOption.Url;
        var savePathFormat = myOption.FilePattern;
        var lang = myOption.Language;
        var aidOri = ""; //原始aid
        var delay = int.TryParse(myOption.DelayPerPage, out var delayValue) ? delayValue : 0;
        Config.DEBUG_LOG = myOption.Debug;

        LogDebug("AppDirectory: {0}", APP_DIR);
        LogDebug("运行参数：{0}", JsonSerializer.Serialize(myOption, MyOptionJsonContext.Default.MyOption));
        return (encodingPriority, dfnPriority, firstEncoding, downloadDanmaku, downloadDanmakuFormats, input, savePathFormat, lang, aidOri, delay);
    }

    public static async Task<(string fetchedAid, VInfo vInfo, string apiType, AppConfig cfg)> GetVideoInfoAsync(MyOption myOption, string aidOri, string input)
    {
        // 加载认证信息
        var (cookie, token) = LoadCredentials(myOption);

        var cfg = new AppConfig(cookie, token, myOption.Host, myOption.EpHost, myOption.TvHost, myOption.Area, "");

        // 检测是否登录了账号
        if (myOption is { UseIntlApi: false, UseTvApi: false } && cfg.Area == "")
        {
            Log("检测账号登录...");
            var (isLogin, wbi) = await CheckLogin(cfg);
            cfg = cfg with { Wbi = wbi };
            if (!isLogin)
            {
                LogWarn("你尚未登录B站账号, 解析可能受到限制");
            }
        }

        Log("获取aid...");
        aidOri = await GetAvIdAsync(input, cfg);
        Log($"获取aid结束: {aidOri}");

        if (string.IsNullOrEmpty(aidOri))
        {
            throw new Exception("输入有误");
        }

        Log("获取视频信息...");
        VInfo? vInfo = null;

        // 只输入 EP/SS 时优先按番剧查找，如果找不到则尝试按课程查找
        try
        {
            vInfo = await FetcherRegistry.FetchAsync(aidOri, cfg, myOption.UseIntlApi);
        }
        catch (KeyNotFoundException e)
        {
            if (e.Message != "Arg_KeyNotFound") throw; // 错误消息不符合预期，抛出异常
            if (aidOri.StartsWith("cheese:")) throw; // 已经按课程查找过，不再重复尝试

            LogWarn("未找到此 EP/SS 对应番剧信息, 正在尝试按课程查找。");

            aidOri = aidOri.Replace("ep", "cheese");
            Log("新的 aid: " + aidOri);

            if (string.IsNullOrEmpty(aidOri))
            {
                throw new Exception("输入有误");
            }

            Log("获取视频信息...");
            vInfo = await FetcherRegistry.FetchAsync(aidOri, cfg, myOption.UseIntlApi);
        }

        var title = vInfo.Title;
        var pubTime = vInfo.PubTime;
        LogColor("视频标题: " + title);
        if (pubTime != 0)
        {
            Log("发布时间: " + FormatTimeStamp(pubTime, "yyyy-MM-dd HH:mm:ss zzz"));
        }

        var bvid = vInfo.PagesInfo.FirstOrDefault( )?.bvid;
        if (!string.IsNullOrEmpty(bvid) && !myOption.UseIntlApi)
        {
            Log($"视频URL: https://www.bilibili.com/video/{bvid}/");
        }

        var mid = vInfo.PagesInfo.FirstOrDefault(p => !string.IsNullOrEmpty(p.ownerMid))?.ownerMid;
        if (!string.IsNullOrEmpty(mid))
        {
            Log($"UP主页: https://space.bilibili.com/{mid}");
        }

        if (vInfo.IsSteinGate && myOption.UseTvApi)
        {
            Log("视频为互动视频，暂时不支持tv下载，修改为默认下载");
            myOption.UseTvApi = false;
        }

        var apiType = myOption.UseTvApi ? "TV" : (myOption.UseAppApi ? "APP" : (myOption.UseIntlApi ? "INTL" : "WEB"));

        //打印分P信息
        List<Page> pagesInfo = vInfo.PagesInfo;
        var more = false;
        foreach (Page p in pagesInfo)
        {
            if (!myOption.ShowAll)
            {
                if (more && p.index != pagesInfo.Count) continue;
                if (!more && p.index > 5)
                {
                    Log("......");
                    more = true;
                    continue;
                }
            }

            Log($"P{p.index}: [{p.cid}] [{p.title}] [{FormatTime(p.dur)}]");
        }

        return (aidOri, vInfo, apiType, cfg);
    }


    private static async Task DoWorkAsync(MyOption myOption)
    {
        try
        {
            (Dictionary<string, byte> encodingPriority, Dictionary<string, int> dfnPriority, var firstEncoding, var downloadDanmaku, BBDownDanmakuFormat[] downloadDanmakuFormats,
                var input, var savePathFormat, var lang, var aidOri, var delay) = SetUpWork(myOption);
            (var fetchedAid, VInfo vInfo, var apiType, AppConfig cfg) = await GetVideoInfoAsync(myOption, aidOri, input);
            await DownloadPagesAsync(myOption, vInfo, encodingPriority, dfnPriority, firstEncoding, downloadDanmaku, downloadDanmakuFormats,
                input, savePathFormat, lang, fetchedAid, delay, apiType, cfg);
        }
        catch (Exception e)
        {
            Console.BackgroundColor = ConsoleColor.Red;
            Console.ForegroundColor = ConsoleColor.White;
            var msg = Config.DEBUG_LOG ? e.ToString( ) : e.Message;
            Console.Write($"{msg}{Environment.NewLine}请尝试升级到最新版本后重试!");
            Console.ResetColor( );
            Console.WriteLine( );
            Thread.Sleep(1);
            Environment.Exit(1);
        }
    }

}
