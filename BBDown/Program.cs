using System;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.Linq;
using System.Threading.Tasks;

using BBDown.Core.Auth;
using BBDown.Cli;
using BBDown.Core;
using BBDown.Core.Live;
using BBDown.Core.Opus;
using BBDown.Core.Util;
using BBDown.Core.Pipeline;
using BBDown.Serve;

using static BBDown.Core.Logger;
using BBDown.Core.Download;

namespace BBDown;

internal sealed class Program
{
    private static void Console_CancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        e.Cancel = true;

        // Ctrl+Break（SIGQUIT）在录制直播时是「停录并混流」，绝不能触发全局取消——那会把随后的 ffmpeg 一起杀掉。
        // 非录制场景 TryRequestStop 恒为 false，直接落回原有的全局取消路径，既有行为零变化
        if (e.SpecialKey == ConsoleSpecialKey.ControlBreak && LiveSignal.TryRequestStop( ))
        {
            if (!Console.IsOutputRedirected)
            {
                Console.WriteLine( );
            }

            LogWarn("收到停止信号，正在结束录制并混流...");
            return;
        }

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
    }

    public static async Task<int> Main(params string[] args)
    {
        Console.CancelKeyPress += Console_CancelKeyPress;

        var rootCommand = CommandLineInvoker.GetRootCommand(RunApp);
        rootCommand.Description = "BBDown 是一个哔哩哔哩视频下载 / 解析命令行工具。";
        rootCommand.TreatUnmatchedTokensAsErrors = false;

        rootCommand.Subcommands.Add(BuildLoginCommand( ));
        rootCommand.Subcommands.Add(BuildServeCommand( ));

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

    // 子命令构造器：只负责把选项与动作装配成 Command，不含任何业务逻辑（业务逻辑在 RunApp / StartServer）
    private static Command BuildLoginCommand( )
    {
        var loginTvOption = new Option<bool>("--tv") { Description = "登录 TV 账号（默认登录 WEB 账号）" };
        var loginAppOption = new Option<bool>("--app") { Description = "登录 APP 账号（默认登录 WEB 账号）" };
        Command command = new("login", "通过 APP 扫描二维码以登录您的账号（默认 WEB，加 --tv 登录 TV，加 --app 登录 APP）");
        command.Options.Add(loginTvOption);
        command.Options.Add(loginAppOption);
        command.SetAction(result =>
        {
            if (result.GetValue(loginTvOption))
            {
                return Login.TV(AppEnv.CancellationToken);
            }

            if (result.GetValue(loginAppOption))
            {
                return Login.App(AppEnv.CancellationToken);
            }

            return Login.Web(AppEnv.CancellationToken);
        });
        return command;
    }

    private static Command BuildServeCommand( )
    {
        Command command = new("serve", "以服务器模式运行")
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
                Description = "同时下载的任务数上限，默认 0 表示不限制；大于 0 时最多 N 个任务同时下载，其余按提交顺序排队，单个任务内部的下载并行度由多线程下载器自行决定",
                DefaultValueFactory = _ => 0,
            }
        };
        command.SetAction(result => StartServer(new ServeConfig(
            result.GetValue<string>("--listen"),
            result.GetValue<string>("--work-dir"),
            result.GetValue<string>("--serve-token"),
            result.GetValue<string>("--host"),
            result.GetValue<string>("--ep-host"),
            result.GetValue<string>("--tv-host"),
            result.GetValue<string>("--cors-origin"),
            result.GetValue<int>("--max-concurrent"))));
        return command;
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
          BBDown <视频地址> -g a           仅下载音频
          BBDown <视频地址> -g av -W s     不下载字幕
          BBDown <专栏地址|cv 号>          导出专栏为 Markdown
          BBDown --help                    查看全部参数说明
        """);
    }

    private static async Task<int> RunApp(DownloadRequest myOption)
    {
        // 进程级全局状态只在每次 CLI 运行起点设置一次（serve 模式不在此路径；
        // ServeRequestOptions 已剔除 Debug/UserAgent，故 serve 任务不触碰这些全局，避免并发互相踩踏）。
        Config.SetDebugLog(myOption.Debug);
        if (!string.IsNullOrEmpty(myOption.UserAgent))
        {
            HTTPUtil.SetUserAgent(myOption.UserAgent);
        }

        Log($"任务开始时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        try
        {
            // 专栏导出走独立链路：不构造 WorkContext，也就不会因为缺少 ffmpeg 而失败
            if (OpusInputResolver.TryParse(myOption.Url, out _))
            {
                foreach (var debug in ContentSelector.DescribeInactive(myOption.Content, ContentMode.Opus))
                {
                    LogDebug(debug);
                }

                await OpusDownload.RunAsync(myOption, AppEnv.CancellationToken);
                return 0;
            }

            // 直播录制同样走独立链路：产物是无限增长的流，分 P 选择、清晰度优先级那套解析对它无意义
            if (LiveInputResolver.TryParse(myOption.Url, out var liveTarget))
            {
                foreach (var debug in ContentSelector.DescribeInactive(myOption.Content, ContentMode.Live))
                {
                    LogDebug(debug);
                }

                try
                {
                    await LiveDownload.RunAsync(myOption, liveTarget, AppEnv.CancellationToken);
                    return 0;
                }
                catch (OperationCanceledException) when (AppEnv.CancellationToken.IsCancellationRequested)
                {
                    LogWarn("录制已中断，已录制的分段文件保留在工作目录中（未混流）。");
                    return 130;
                }
            }

            await DownloadPipeline.RunAsync(myOption, sink: default, AppEnv.CancellationToken);
            return 0;
        }
        catch (Exception e)
        {
            return MapExitCode(e);
        }
    }

    // RunApp 的异常→退出码映射收成纯函数，便于独立验证；RunApp 只保留业务编排
    private static int MapExitCode(Exception e)
    {
        if (e is OperationCanceledException && AppEnv.CancellationToken.IsCancellationRequested)
        {
            LogWarn("下载已取消。已下载的部分会保留在临时文件中，重新运行命令可断点续传。");
            return 130;
        }

        // 必须排在通用分支之前，否则会打出"请升级到最新版本后重试"这句对充电权限毫无意义的误导文案
        if (IsChargedPreviewOnly(e))
        {
            LogWarn("全部所选分 P 均为充电专属试看片段，未产出文件（退出码 2）");
            return 2;
        }

        Console.BackgroundColor = ConsoleColor.Red;
        Console.ForegroundColor = ConsoleColor.White;
        var msg = Config.DebugLog ? e.ToString( ) : e.Message;
        Console.Write($"{msg}{Environment.NewLine}请升级到最新版本后重试。");
        Console.ResetColor( );
        Console.WriteLine( );
        return 1;
    }

    // 混合场景（部分充电试看 + 部分真实失败）返回 false 走退出码 1，
    // 让 exit == 2 成为强断言：没有任何分 P 因真实故障失败，唯一原因是充电权限
    internal static bool IsChargedPreviewOnly(Exception e)
    {
        return e is ChargedPreviewException
               || (e is AggregateException agg && agg.InnerExceptions.Count > 0
                   && agg.InnerExceptions.All(inner => inner is ChargedPreviewException));
    }

    private static void StartServer(ServeConfig config)
    {
        const string DefaultListenUrl = "http://127.0.0.1:23333";
        var server = new BBDownApiServer( );
        server.SetUpServer(config.WorkDir, serveToken: config.ServeToken, host: config.Host, epHost: config.EpHost, tvHost: config.TvHost, corsOrigin: config.CorsOrigin, maxConcurrent: config.MaxConcurrent);
#pragma warning disable CA2234 // 保留 Run(string) 内的 URL 合法性校验与友好退出
        server.Run(string.IsNullOrEmpty(config.ListenUrl) ? DefaultListenUrl : config.ListenUrl);
#pragma warning restore CA2234
    }
}
