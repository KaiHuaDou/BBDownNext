using System.CommandLine;
using System.CommandLine.Parsing;
using System.Linq;
using System.Threading.Tasks;

using BBDown.Core;
using BBDown.Core.Auth;
using BBDown.Core.Download;
using BBDown.Serve;

namespace BBDown.Cli;

/// <summary>
/// 子命令构造器：只负责把选项与动作装配成 Command，不含业务逻辑（扫码登录在 Login，关服在 StartServer）
/// 从 Program 迁出，使后者回到单文件行数上限以内
/// </summary>
internal static class SubCommands
{
    public static Command LoginCommand( )
    {
        var loginTvOption = new Option<bool>("--tv") { Description = "登录 TV 账号（默认登录 WEB 账号）" };
        var loginAppOption = new Option<bool>("--app") { Description = "登录 APP 账号（默认登录 WEB 账号）" };
        Command command = new("login", "登录账号（默认 WEB，加 --tv 登录 TV，加 --app 登录 APP）；status 查看当前登录状态，refresh 续期 WEB Cookie")
        {
            loginTvOption,
            loginAppOption,
            LoginStatusCommand( ),
            LoginRefreshCommand( ),
        };
        // status / refresh 一次处理全通道，与 --tv / --app 的「选一个通道登录」相冲突
        // 静默忽略通道选项会让用户以为筛了通道，实际拿到全量
        command.Validators.Add(result =>
        {
            if (result.Children.OfType<CommandResult>( ).Any(c => c.Command.Name is "status" or "refresh")
                && (result.GetValue(loginTvOption) || result.GetValue(loginAppOption)))
            {
                result.AddError("status / refresh 不能与 --tv / --app 同时使用");
            }
        });
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

    private static Command LoginStatusCommand( )
    {
        Command command = new("status", "输出 WEB / TV / APP 三个通道的当前登录状态");
        command.SetAction(( _) => Login.StatusAsync(AppEnv.CancellationToken));
        return command;
    }

    private static Command LoginRefreshCommand( )
    {
        Command command = new("refresh", "用本地 refresh_token 续期 WEB Cookie");
        command.SetAction(( _) => Login.RefreshAsync(AppEnv.CancellationToken));
        return command;
    }

    public static Command ServeCommand( )
    {
        Command command = new("serve", "以服务器模式运行")
        {
            new Option<string>("--listen", "-l")
            {
                Description = "服务器监听地址，默认 http://127.0.0.1:23333；是否强制令牌鉴权取决于是否传入 --serve-token，未传入则默认免令牌开放并仅警告"
            },
            new Option<string>("--serve-token")
            {
                Description = "serve 模式鉴权令牌；显式传入后才启用强制鉴权（HTTP 接口须带 X-BBDown-Token 头，\n仅 WebSocket 握手 /hubs/tasks 例外接受 ?token= 查询参数，因浏览器无法自定义握手头），未传入则默认免令牌开放并仅警告"
            },
            new Option<string>("--work-dir")
            {
                Description = "所有任务的下载输出目录，请求中的同名字段会被忽略"
            },
            new Option<string>("--host")
            {
                Description = "API 请求 Host，所有任务统一使用此值；请求体不能指定 host（防止凭据被导向外部服务器）"
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
            },
            new Option<bool>("--webui")
            {
                Description = "将内嵌的 Web 前端与 API 托管在同一端口（静态资源根路径托管，前端自动以该端口地址调用 API）。需构建时已将 WebUI dist 嵌入，否则该选项无效果。"
            }
        };
        // server.Run 阻塞整个进程生命周期直到关服：挪到线程池让 InvokeAsync 真正异步等待
        // 也与 login 命令的真异步动作一致
        command.SetAction(result => Task.Run(( ) => StartServer(new ServeConfig(
            result.GetValue<string>("--listen"),
            result.GetValue<string>("--work-dir"),
            result.GetValue<string>("--serve-token"),
            result.GetValue<string>("--host"),
            result.GetValue<string>("--ep-host"),
            result.GetValue<string>("--tv-host"),
            result.GetValue<string>("--cors-origin"),
            result.GetValue<int>("--max-concurrent"),
            result.GetValue<bool>("--webui")))));
        return command;
    }

    private static int StartServer(ServeConfig config)
    {
        // serve 的工作目录在启动时一次性校验：坏值会让每个任务都在运行时失败，不如启动即报错退出
        if (!string.IsNullOrWhiteSpace(config.WorkDir))
        {
            try
            {
                Core.Pipeline.WorkSetup.ValidateWorkDir(config.WorkDir);
            }
            catch (WorkDirException e)
            {
                Core.Logger.LogError(e.Message);
                return 1;
            }
        }

        // 渲染器已由 Main 顶层装配并覆盖 serve 生命周期，此处不得重复创建，避免双订阅导致日志双打印
        var server = new BBDownServer( );
        server.SetUpServer(config);
#pragma warning disable CA2234 // 保留 Run(string) 内的 URL 合法性校验与友好退出
        server.Run(string.IsNullOrEmpty(config.ListenUrl) ? BBDownServer.DefaultListenUrl : config.ListenUrl);
#pragma warning restore CA2234
        return 0;
    }
}
