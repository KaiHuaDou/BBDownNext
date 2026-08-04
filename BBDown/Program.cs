using System;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.Linq;
using System.Threading.Tasks;

using BBDown.Auth;
using BBDown.Cli;
using BBDown.Core.Opus;
using BBDown.Core;
using BBDown.Pipeline;
using BBDown.Serve;

using static BBDown.Core.Logger;

namespace BBDown;

internal sealed class Program
{
    private static void Console_CancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        e.Cancel = true;

        // 这样“正在退出”不会被残留的渲染定时器冲掉；随后换行再打印提示
        AppEnv.Cancel( );
        if (!Console.IsOutputRedirected)
        {
            Console.WriteLine( );
        }

        LogWarn("收到取消信号，正在退出...");
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

        AppEnv.Cancel( );
    }

    public static async Task<int> Main(params string[] args)
    {
        Console.CancelKeyPress += Console_CancelKeyPress;

        var rootCommand = CommandLineInvoker.GetRootCommand(myOption => RunApp(myOption, opusCommand: false));
        rootCommand.Description = "BBDown 是一个哔哩哔哩视频下载 / 解析命令行工具。";
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

        rootCommand.Subcommands.Add(CommandLineInvoker.GetOpusCommand(myOption => RunApp(myOption, opusCommand: true)));

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
            },
            new Option<string>("--host")
            {
                Description = "API 请求 Host，所有任务统一使用此值；请求体不再能指定 host（防止凭据被导向外部服务器）"
            },
            new Option<string>("--ep-host")
            {
                Description = "番剧/影视 API 请求 Host，所有任务统一使用此值"
            },
            new Option<string>("--tv-host")
            {
                Description = "TV 端 API 请求 Host，所有任务统一使用此值"
            },
            new Option<string>("--cors-origin")
            {
                Description = "仅允许该单一来源跨域调用 serve（CORS）。不指定则完全关闭 CORS，从根本上阻止恶意网页发起请求"
            },
            new Option<int>("--max-concurrent")
            {
                Description = "同时下载的任务数上限，默认 0 表示不限制；大于 0 时多余任务排队等待，并把每个任务的分片并发压到 1，使总下载连接数不超过该值",
                DefaultValueFactory = _ => 0,
            }
        };
        serverCommand.SetAction(result => StartServer(
            result.GetValue<string>("--listen"),
            result.GetValue<string>("--work-dir"),
            result.GetValue<string>("--serve-token"),
            result.GetValue<string>("--host"),
            result.GetValue<string>("--ep-host"),
            result.GetValue<string>("--tv-host"),
            result.GetValue<string>("--cors-origin"),
            result.GetValue<int>("--max-concurrent")));
        rootCommand.Subcommands.Add(serverCommand);

        var parserConfiguration = new ParserConfiguration( )
        {
            EnablePosixBundling = true,
        };

        var rootResult = rootCommand.Parse(args, parserConfiguration);

        Console.BackgroundColor = ConsoleColor.DarkBlue;
        Console.ForegroundColor = ConsoleColor.White;
        var ver = System.Reflection.Assembly.GetExecutingAssembly( ).GetName( ).Version!;
        Console.Write($"BBDown Next v{ver.Major}.{ver.Minor}.{ver.Build}");
        Console.ResetColor( );
        Console.WriteLine( );
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
          BBDown opus <专栏地址|cv号>      导出专栏为 Markdown
          BBDown --help                    查看全部参数说明
        """);
    }

    /// <param name="opusCommand">
    /// 用户显式使用了 opus 子命令。此时才允许把裸数字当作 opus id / cv 号，
    /// 根命令下的裸数字仍归属视频链路（av 号）。
    /// </param>
    private static async Task<int> RunApp(DownloadOptions myOption, bool opusCommand)
    {
        Log($"任务开始时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        try
        {
            // 专栏导出走独立链路：不构造 WorkContext，也就不会因为缺少 ffmpeg 而失败
            if (opusCommand || OpusInputResolver.TryParse(myOption.Url, out _))
            {
                await OpusDownload.RunAsync(myOption, allowBareId: opusCommand, AppEnv.CancellationToken);
                return 0;
            }

            await DownloadPipeline.RunAsync(myOption, relatedTask: null, AppEnv.CancellationToken);
            return 0;
        }
        catch (OperationCanceledException) when (AppEnv.CancellationToken.IsCancellationRequested)
        {
            LogWarn("下载已取消。已下载的部分会保留在临时文件中，重新运行命令可断点续传。");
            return 130;
        }
        // 必须排在通用 catch 之前，否则会打出"请升级到最新版本后重试"这句对充电权限毫无意义的误导文案
        catch (Exception e) when (IsChargedPreviewOnly(e))
        {
            LogWarn("全部所选分 P 均为充电专属试看片段，未产出文件（退出码 2）");
            return 2;
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

    // 混合场景（部分充电试看 + 部分真实失败）返回 false 走退出码 1，
    // 让 exit == 2 成为强断言：没有任何分 P 因真实故障失败，唯一原因是充电权限
    internal static bool IsChargedPreviewOnly(Exception e)
    {
        return e is ChargedPreviewException
               || (e is AggregateException agg && agg.InnerExceptions.Count > 0
                   && agg.InnerExceptions.All(inner => inner is ChargedPreviewException));
    }

    private static void StartServer(string? listenUrl, string? workDir, string? serveToken = null, string? host = null, string? epHost = null, string? tvHost = null, string? corsOrigin = null, int maxConcurrent = 0)
    {
        const string DefaultListenUrl = "http://127.0.0.1:23333";
        var server = new BBDownApiServer( );
        server.SetUpServer(workDir, serveToken: serveToken, host: host, epHost: epHost, tvHost: tvHost, corsOrigin: corsOrigin, maxConcurrent: maxConcurrent);
#pragma warning disable CA2234 // 保留 Run(string) 内的 URL 合法性校验与友好退出
        server.Run(string.IsNullOrEmpty(listenUrl) ? DefaultListenUrl : listenUrl);
#pragma warning restore CA2234
    }
}
