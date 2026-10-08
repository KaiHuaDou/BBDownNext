using System;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.Linq;
using System.Threading.Tasks;

using BBDown.Cli;
using BBDown.Core;
using BBDown.Core.Download;
using BBDown.Core.Live;
using BBDown.Core.Pipeline;
using BBDown.Core.Util;

using static BBDown.Core.Logger;

namespace BBDown;

internal sealed class Program
{
    // 录制中的直播会话标识，供 Ctrl+Break handler 精准停止对应录制（并发场景互不干扰）
    // volatile：主线程写、CancelKeyPress handler 线程读，需内存屏障保证 handler 读到最新值
    // 已知窗口：RunAsync 返回到 finally 置 null 之间到达的 Ctrl+Break 会命中已结束的会话（TryRequestStop 返回 false，无害）
    // 同房间立即重启录制会复用同一规范串 id，极端时序下存在误停新会话的理论窗口，根治在 LiveSignal 的会话代次
    private static volatile string? liveSessionId;

    private static void Console_CancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        e.Cancel = true;

        // Ctrl+Break（SIGQUIT）在录制直播时是「停录并混流」，绝不能触发全局取消——那会把随后的 ffmpeg 一起杀掉
        // 非录制场景 liveSessionId 为 null，TryRequestStop 固定为 false，直接落回原有的全局取消路径，既有行为零变化
        if (e.SpecialKey == ConsoleSpecialKey.ControlBreak && liveSessionId is { } liveId && LiveSignal.TryRequestStop(liveId))
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

    public static async Task<int> Main(string[] args)
    {
        args = NormalizeArguments(args);
        Console.CancelKeyPress += Console_CancelKeyPress;
        // 业务消息渲染（CLI 展示）：Core 只产生消息，本渲染器决定控制台如何展示
        using var messageRenderer = new ConsoleMessageRenderer( );

        var rootCommand = CommandLineInvoker.GetRootCommand(RunApp);
        rootCommand.Description = "BBDown 是一个哔哩哔哩视频下载 / 解析命令行工具。";
        // 关闭未匹配 token 报错：配置文件补齐依赖重新解析整段参数表，且 url 位置参数
        // 的宽容匹配（任意串）由根命令声明；强校验交给 TryReportParseErrors 的显式错误路径
        rootCommand.TreatUnmatchedTokensAsErrors = false;

        rootCommand.Subcommands.Add(SubCommands.LoginCommand( ));
        rootCommand.Subcommands.Add(SubCommands.ServeCommand( ));

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
        // 首次解析只为读出 --config 与显式选项集合，合并结果必须整体重解析才能生效
        if (rootResult.CommandResult.Command == rootCommand)
        {
            var mergedArgs = ConfigParser.MergeWithConfig(args, rootResult, rootCommand);
            if (!ReferenceEquals(mergedArgs, args))
            {
                rootResult = rootCommand.Parse(mergedArgs, parserConfiguration);
            }

            // 既无 URL 参数、也无配置文件提供地址时，打印用法而不是抛「缺少必需参数」（--help/--version 不产生错误，仍走原流程）
            if (!HasUrlArgument(rootResult) && (rootResult.Errors.Count > 0 || string.IsNullOrEmpty(rootResult.GetValue<string>("--config"))))
            {
                PrintUsageExample( );
                // return 0
            }
        }

        if (!TryReportParseErrors(rootResult))
        {
            return 1;
        }

        return await rootResult.InvokeAsync(new InvocationConfiguration( ) { EnableDefaultExceptionHandler = true });
    }

    internal static string[] NormalizeArguments(string[] args)
    {
        return [.. args.Select(value => value.Trim('\r', '\n', '\t').Trim( ))];
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
        // b23.tv 短链需展开后再识别形式：独立链路（直播 / 专栏 / 集合）展开到对应 URL 才能正确分流
        // （否则落入通用解析抛"未知 id 类型"）。重复展开无副作用，下游 OpusDownload 二次展开无副作用
        var url = myOption.Url;
        if (url.Contains("b23.tv", StringComparison.OrdinalIgnoreCase))
        {
            url = await HTTPUtil.GetWebLocationAsync(url, AppEnv.CancellationToken);
            myOption = myOption with { Url = url };
        }

        // 进程级全局状态只在每次 CLI 运行起点设置一次（serve 模式不在此路径
        // ServeRequestOptions 已剔除 Debug/UserAgent，故 serve 任务不触碰这些全局，避免并发互相踩踏）
        Config.SetDebugLog(myOption.Debug);
        // UA 不在此设置：WorkSetup.ResolveConfig 已把 myOption.UserAgent 落入请求级 AppConfig
        // GUI 等并发宿主按任务各自生效，不经进程级全局

        Log($"任务开始时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        try
        {
            // 交互消费端先于一切装配：独立链路的空间动态会复用视频管道（-iap / -iaq 提问需有应答端）
            // 视频管道的解析期（逐集确认）同样可能触发；进度条钩子由 CliInteraction 静态属性保存
            using var interaction = new CliInteraction( );

            // 独立链路（直播 / 专栏 / 文集 / 空间图文 / 音频 / 空间动态）：输入形式识别为纯字符串逻辑（TryDispatch）
            // 不构造 WorkContext 也就不会因为缺少 ffmpeg 而失败；执行统一经 WorkerDispatcher 分发
            if (InputResolver.TryDispatch(myOption.Url, out var dispatchId))
            {
                foreach (var debug in ContentSelector.DescribeInactive(myOption.Content, ContentSelector.ModeOf(dispatchId)))
                {
                    LogDebug(debug);
                }

                if (dispatchId is ResourceId.LiveRoom)
                {
                    // 直播录制产物是无限增长的流，分 P 选择、清晰度优先级那套解析对它无意义
                    // liveSessionId 与 WorkerDispatcher 内 LiveSignal 注册键（规范串）一致，Ctrl+Break 据此停录
                    try
                    {
                        using var liveProgress = new LiveProgress( );
                        liveSessionId = ResourceIdJsonConverter.Format(dispatchId);
                        await WorkerDispatcher.RunAsync(dispatchId, myOption, default, null, AppEnv.CancellationToken);
                        return 0;
                    }
                    catch (OperationCanceledException) when (AppEnv.CancellationToken.IsCancellationRequested)
                    {
                        LogWarn("录制已中断，已录制的分段文件保留在工作目录中（未混流）。");
                        return 130;
                    }
                    finally
                    {
                        liveSessionId = null;
                    }
                }

                // 图片 / 音频可能上百条，进度条比逐条日志更直观（按条数上报 ProgressBus 样本）
                using var itemProgressBar = new ProgressBar(AppEnv.CancellationToken);
                await WorkerDispatcher.RunAsync(dispatchId, myOption, default, null, AppEnv.CancellationToken);
                return 0;
            }

            using var progressBar = new ProgressBar(AppEnv.CancellationToken);
            await DownloadPipeline.RunAsync(myOption, ct: AppEnv.CancellationToken);
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

        // 工作目录问题是用户侧输入错误，只打印清晰文案，不带「请升级」误导语（退出码 1）
        if (e is WorkDirException)
        {
            LogError(e.Message);
            return 1;
        }

        if (IsChargedPreviewOnly(e))
        {
            LogWarn("全部所选分 P 均为充电专属试看片段，未产出文件（退出码 2）");
            return 2;
        }

        string msg;
        if (Config.DebugLog)
        {
            msg = e.ToString( );
        }
        else
        {
            // 非 DebugLog 下展开内层异常，否则多分 P 失败只显示一行无信息文案
            // 有界展开，避免数万分 P 全失败时拼接成 MB 级单条消息
            msg = $"\n{Utils.FormatErrorMessage(e)}";
        }

        Console.BackgroundColor = ConsoleColor.Red;
        Console.ForegroundColor = ConsoleColor.White;
        Console.Write(msg);
        Console.ResetColor( );
        Console.WriteLine( );
        Console.WriteLine( );
        return 1;
    }

    // 混合场景（部分充电试看 + 部分真实失败）返回 false 走退出码 1
    // 让 exit == 2 成为强断言：没有任何分 P 因真实故障失败，唯一原因是充电权限
    internal static bool IsChargedPreviewOnly(Exception e)
    {
        return e is ChargedPreviewException
               || (e is AggregateException agg && agg.InnerExceptions.Count > 0
                   && agg.InnerExceptions.All(inner => inner is ChargedPreviewException));
    }
}
