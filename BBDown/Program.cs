using System;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core;

using static BBDown.Core.Logger;

namespace BBDown;

internal sealed class Program
{
    // AppContext.BaseDirectory 指向入口程序集所在目录；Environment.ProcessPath 在 `dotnet BBDown.dll` 下返回宿主路径，会写错位置（P1-13）
    public static readonly string AppDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);

    // 全局取消源：Ctrl+C 时取消，令牌沿 Fetcher → Parser → HTTP → 下载 → 外部进程 全链路透传
    private static readonly CancellationTokenSource cancelSource = new();
    internal static CancellationToken CancellationToken => cancelSource.Token;

    // WorkSetup.Build 内部有二进制查找等进程级初始化，串行化后 serve 并发任务不会互相踩踏（P1-16）
    private static readonly Lock workContextGate = new();

    private static void Console_CancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        // 抑制运行时默认的进程终止，改为靠令牌优雅取消
        e.Cancel = true;
        LogWarn("收到取消信号，正在安全退出...");
        try
        {
            Console.ResetColor();
            Console.CursorVisible = true;
            if (!OperatingSystem.IsWindows())
            {
                System.Diagnostics.Process.Start("stty", "echo");
            }
        }
        catch { }

        cancelSource.Cancel();
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
                return Login.TV();
            }

            if (result.GetValue(loginAppOption))
            {
                return Login.App();
            }

            return Login.Web();
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

        var parserConfiguration = new ParserConfiguration()
        {
            EnablePosixBundling = true,
        };

        var rootResult = rootCommand.Parse(args, parserConfiguration);

        Console.BackgroundColor = ConsoleColor.DarkBlue;
        Console.ForegroundColor = ConsoleColor.White;
        var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version!;
        Console.Write($"BBDown version {ver.Major}.{ver.Minor}.{ver.Build}, Bilibili Downloader.\r\n");
        Console.ResetColor();
        Console.WriteLine();

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
                PrintUsageExample();
                return 0;
            }
        }

        if (!TryReportParseErrors(rootResult))
        {
            return 1;
        }

        return await rootResult.InvokeAsync(new InvocationConfiguration() { EnableDefaultExceptionHandler = true });
    }

    private static bool HasUrlArgument(ParseResult parseResult)
    {
        return parseResult.CommandResult.Children
            .OfType<ArgumentResult>()
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
        Console.ResetColor();
        Console.Error.WriteLine("请使用 BBDown --help 查看帮助");
        return false;
    }

    private static void PrintUsageExample()
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
        var server = new BBDownApiServer();
        server.SetUpServer(workDir, serveToken: serveToken);
#pragma warning disable CA2234 // 保留 Run(string) 内的 URL 合法性校验与友好退出
        server.Run(string.IsNullOrEmpty(listenUrl) ? DefaultListenUrl : listenUrl);
#pragma warning restore CA2234
    }

    /// <summary>
    /// 下载主干：准备运行参数 → 解析视频信息 → 逐分 P 下载。CLI 与 serve 共用同一条链路，
    /// 差异只有 <paramref name="relatedTask"/>（serve 用它回填标题与进度）。
    /// </summary>
    internal static async Task RunDownloadAsync(DownloadOptions myOption, DownloadTask? relatedTask = null, CancellationToken ct = default)
    {
        WorkContext ctx;
        lock (workContextGate)
        {
            ctx = WorkSetup.Build(myOption);
        }

        ctx = await VideoInfo.FetchAsync(myOption, ctx, ct);
        if (relatedTask is not null)
        {
            relatedTask.Title = ctx.VInfo!.Title;
            relatedTask.Pic = ctx.VInfo.Pic;
            relatedTask.VideoPubTime = ctx.VInfo.PubTime;
        }

        await PageQueue.RunAsync(myOption, ctx, relatedTask, ct);
    }

    private static async Task<int> DoWorkAsync(DownloadOptions myOption, CancellationToken ct = default)
    {
        try
        {
            await RunDownloadAsync(myOption, relatedTask: null, ct);
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
            var msg = Config.DebugLog ? e.ToString() : e.Message;
            Console.Write($"{msg}{Environment.NewLine}请升级到最新版本后重试。");
            Console.ResetColor();
            Console.WriteLine();
            return 1;
        }
    }
}
