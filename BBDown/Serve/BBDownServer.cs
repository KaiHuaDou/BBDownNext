using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Channels;
using System.Threading.RateLimiting;

using BBDown.Serve.Auth;
using BBDown.Serve.Http;
using BBDown.Serve.Tasks;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace BBDown.Serve;

public class BBDownServer
{
    internal const string DefaultListenUrl = "http://127.0.0.1:23333";

    private const int AuthFailureLimit = 5;
    private static readonly TimeSpan AuthFailureWindow = TimeSpan.FromMinutes(1);
    private const int AuthFailureCap = 1024;

    // 认证失败滑动窗口（实例级：每个服务实例独立限速）：每 IP 每分钟 5 次，超限 429（防令牌暴力）。
    // 第二项是最后一次失败时刻，既用于判定窗口是否过期，也用于超限时的淘汰排序
    private readonly ConcurrentDictionary<string, (int Count, DateTimeOffset Last)> authFailures = new( );

    private WebApplication? app;
    private string? serveToken;
    private bool authRequired;
    private bool authFinalized;

    // 鉴权判定：显式传入 --serve-token 才启用强制鉴权（所有访问均须带令牌）；未传入则默认免令牌开放，仅打印警告
    private void FinalizeAuth(string url)
    {
        if (authFinalized)
        {
            return;
        }

        authFinalized = true;
        if (serveToken is not null)
        {
            authRequired = true;
            return;
        }

        // 未指定令牌：任何能连上本监听端口的客户端都可无令牌调用，仅以警告提示暴露风险（不自动生成令牌）；绑定非回环时风险更高
        authRequired = false;
        var nonLoopback = !SsrfGuard.IsLoopbackUrl(url);
        Console.BackgroundColor = ConsoleColor.DarkYellow;
        Console.ForegroundColor = ConsoleColor.Black;
        Console.Write(nonLoopback
            ? "serve 未设置鉴权令牌且绑定到非回环地址：任何能访问本端口的客户端均可无令牌调用，存在被公网 / 局域网滥用的高风险，请立即以 --serve-token 指定令牌或加反向代理。"
            : "serve 未设置鉴权令牌：任何能访问本监听端口的客户端均可无令牌调用，请勿暴露到公网 / 局域网。如需鉴权请以 --serve-token 指定令牌。");
        Console.ResetColor( );
        Console.WriteLine( );
    }

    internal void SetUpServer(ServeConfig config)
    {
        if (app is not null)
        {
            return;
        }

        serveToken = config.ServeToken;
        // 鉴权判定须在注册认证管道前完成（--serve-token 传入才强制，否则免令牌仅警告），Run 传入的 URL 与之等价
        FinalizeAuth(config.ListenUrl ?? DefaultListenUrl);

        var builder = WebApplication.CreateSlimBuilder( );
        // 仅供集成测试：在指定地址（通常为 http://127.0.0.1:0 随机端口）绑定，避免占用生产默认端口
        if (!string.IsNullOrEmpty(config.ListenUrl))
        {
            builder.WebHost.UseUrls(config.ListenUrl);
        }

        builder.WebHost.ConfigureKestrel(options =>
        {
            // serve 请求体很小，1 MB 上限防大体重放；请求头超时收紧
            options.Limits.MaxRequestBodySize = 1024 * 1024;
            options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(15);
        });

        builder.Services.ConfigureHttpJsonOptions((options) => options.SerializerOptions.TypeInfoResolver = JsonTypeInfoResolver.Combine(options.SerializerOptions.TypeInfoResolver, AppJsonSerializerContext.Default));

        // CORS：放行回环来源（127.0.0.1 / localhost）与显式 --cors-origin 的浏览器请求。
        // 安全前提：CORS 校验的是请求方 Origin 而非目标地址，恶意网页（非回环 Origin）依旧无 ACAO 头被浏览器拦截。
        // 注意它挡不住 DNS rebinding——攻击者域名解析到 127.0.0.1 后，页面发起的是「同源」请求，
        // 同源 GET 不携带 Origin。该场景由 Host 头白名单中间件兜底（见 ConfigurePipeline）。
        builder.Services.AddCors((options) =>
        {
            options.AddPolicy("AllowSpecificOrigin",
                policy => policy
                    .SetIsOriginAllowed(origin => TaskSocketHub.IsAllowedOrigin(origin, config))
                    .AllowAnyMethod( )
                    .AllowAnyHeader( ));
        });

        AddServeRateLimiting(builder.Services);

        builder.Services.AddAuthentication(ApiKeyAuthenticationOptions.DefaultScheme)
            .AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(
                ApiKeyAuthenticationOptions.DefaultScheme,
                options => options.ExpectedToken = serveToken);
        // UseAuthorization 要求授权服务始终注册；FallbackPolicy 默认拒绝仅鉴权模式启用，
        // 未启用强制鉴权（authRequired == false）时无 FallbackPolicy，匿名全放行
        builder.Services.AddAuthorization(options =>
        {
            if (authRequired)
            {
                options.FallbackPolicy = new AuthorizationPolicyBuilder( ).RequireAuthenticatedUser( ).Build( );
            }
        });

        builder.Services.AddSingleton(config);
        // 有界（100）执行队列提供背压：写满时受理返回 429（见 TaskStore.EnqueueAsync）
        var taskChannel = Channel.CreateBounded<TaskEnvelope>(new BoundedChannelOptions(100)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false,
        });
        builder.Services.AddSingleton(_ => new TaskStore(config, taskChannel.Writer));
        builder.Services.AddSingleton(sp => new TaskWorker(taskChannel.Reader, sp.GetRequiredService<TaskStore>( ), config.MaxConcurrent));
        builder.Services.AddHostedService(sp => sp.GetRequiredService<TaskWorker>( ));
        builder.Services.AddSingleton(sp => new TaskSocketHub(sp.GetRequiredService<TaskStore>( )));
        builder.Services.AddSingleton<QrLoginStore>( );

        app = builder.Build( );

        // 日志消息经桥接器按任务路由进事件流（WebSocket）。事件流始终启用，桥接器被静态订阅强持有，
        // 存活至进程退出，无需字段引用或释放。
        _ = new TaskMessageBridge(app.Services.GetRequiredService<TaskStore>( ));

        ConfigurePipeline(app, config);
    }

    // 全局限流（per-IP 固定窗口）兜底滥用；任务提交 / 登录二维码起点各自独立策略（防批量触发）
    private static void AddServeRateLimiting(IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString( ) ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 60,
                        Window = TimeSpan.FromMinutes(1),
                        AutoReplenishment = true,
                    }));
            options.AddPolicy("taskSubmit", context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString( ) ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromMinutes(1),
                        AutoReplenishment = true,
                    }));
            // 登录二维码起点限流：扫码系低频操作但生成动作昂贵（双请求 + 本地 PNG），独立策略防批量触发
            options.AddPolicy("loginSubmit", context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString( ) ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromMinutes(1),
                        AutoReplenishment = true,
                    }));
        });
    }

    /// <summary>
    /// 请求管线装配：安全响应头 → 回环 Host 边界 → CORS → 限流 → 写端点 Origin 校验 →
    /// 认证授权 → 认证失败限速 → WebSocket → 端点映射。
    /// </summary>
    private void ConfigurePipeline(WebApplication app, ServeConfig config)
    {
        // 安全响应头：所有响应（含 4xx）统一携带
        app.Use(async (context, next) =>
        {
            context.Response.Headers.XContentTypeOptions = "nosniff";
            context.Response.Headers.XFrameOptions = "DENY";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            context.Response.Headers["Permissions-Policy"] = "geolocation=(), microphone=(), camera=()";
            // 启用 WebUI 时放开为 'self' 族，否则 SPA 自身的脚本与样式会被 default-src 'none' 拦掉；其余安全头不变
            context.Response.Headers.ContentSecurityPolicy = config.EnableWebUi
                ? "default-src 'self'; img-src 'self' data:; style-src 'self' 'unsafe-inline'; script-src 'self'; connect-src 'self'"
                : "default-src 'none'";
            await next( );
        });

        // 免令牌时 serve 的信任边界就是「回环直连」，按请求目标的 Host 头判定：
        // 读端点（GET）拿不到 Origin（见上方 CORS 注释），只能靠 Host 把 rebinding 挡在外面。
        // 带令牌时跳过——此时认证才是边界，Host 可能是反向代理的域名。
        // 置于限流之前：连本机都不该来的请求不配消耗限流配额
        app.Use(async (context, next) =>
        {
            if (!authRequired && !SsrfGuard.IsLoopbackHost(context.Request.Host.Host))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            await next( );
        });

        app.UseCors("AllowSpecificOrigin");

        app.UseRateLimiter( );

        // 写端点（POST/DELETE）校验 Origin：回环或 --cors-origin，否则 403（CSRF 防线，浏览器与脚本客户端通用）
        app.Use(async (context, next) =>
        {
            if ((HttpMethods.IsPost(context.Request.Method) || HttpMethods.IsDelete(context.Request.Method))
                && !TaskSocketHub.IsAllowedOrigin(context.Request.Headers.Origin.ToString( ), config))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            await next( );
        });

        app.UseAuthentication( );
        app.UseAuthorization( );

        // 认证失败滑动窗口限速：401 响应后计数，超限改 429
        app.Use(async (context, next) =>
        {
            await next( );
            if (authRequired && context.Response.StatusCode == StatusCodes.Status401Unauthorized
                && ExceedsAuthFailureLimit(context.Connection.RemoteIpAddress?.ToString( ) ?? "unknown"))
            {
                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            }
        });

        // SlimBuilder 不注册 WebSocket 中间件，须显式启用（IsWebSocketRequest 依赖其 feature）
        app.UseWebSockets( );
        app.MapServeEndpoints( );
        app.MapLoginEndpoints( );

        // 内嵌 WebUI：扫描 webui.* 资源并建立查表映射；未嵌入却启用 --webui 时仅警告，不阻断服务
        var webUiResources = WebUiEndpoints.BuildResourceMap(typeof(BBDownServer).Assembly);
        if (config.EnableWebUi && webUiResources.Count == 0)
        {
            Console.BackgroundColor = ConsoleColor.DarkYellow;
            Console.ForegroundColor = ConsoleColor.Black;
            Console.Write("已启用 --webui，但可执行文件构建时未嵌入 WebUI dist（请先构建 BBDown.WebUI 再构建 BBDown），前端将无法提供。");
            Console.ResetColor( );
            Console.WriteLine( );
        }

        app.MapWebUiEndpoints(config, webUiResources);
    }

    public void Run(Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);
        Run(url.ToString( ));
    }

    public void Run(string url)
    {
        if (app is null)
        {
            return;
        }

        FinalizeAuth(url);
        var result = Uri.TryCreate(url, UriKind.Absolute, out var uriResult)
            && uriResult.Scheme == Uri.UriSchemeHttp;
        if (!result)
        {
            Console.BackgroundColor = ConsoleColor.Red;
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write($"{url} 不是合法的 http URL，url 示例：http://0.0.0.0:5000");
            Console.ResetColor( );
            Console.WriteLine( );
            Console.BackgroundColor = ConsoleColor.Red;
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write("如果您需要 https，请额外配置反向代理");
            Console.ResetColor( );
            Console.WriteLine( );
            Environment.Exit(1);
        }

        app.Run(url);
    }

    private bool ExceedsAuthFailureLimit(string ip)
    {
        var now = DateTimeOffset.UtcNow;
        var (count, _) = authFailures.AddOrUpdate(ip, (1, now), (_, v) =>
            now - v.Last > AuthFailureWindow ? (1, now) : (v.Count + 1, now));
        if (count > AuthFailureLimit)
        {
            return true;
        }

        TrimAuthFailures(now);
        return false;
    }

    /// <summary>
    /// 把失败记录压回条目上限。只清过期条目约束不住规模：用大量一次性 IP / XFF 值轰炸时
    /// 每条都是「刚刚失败」，永远不会过期。故先清过期，仍超限则按最后失败时间淘汰最旧的部分——
    /// 整体清空会把攻击者的计数一并重置，等于周期性放宽限速。
    /// </summary>
    private void TrimAuthFailures(DateTimeOffset now)
    {
        if (authFailures.Count <= AuthFailureCap)
        {
            return;
        }

        foreach (var (key, (_, last)) in authFailures)
        {
            if (now - last > AuthFailureWindow)
            {
                authFailures.TryRemove(key, out _);
            }
        }

        if (authFailures.Count <= AuthFailureCap)
        {
            return;
        }

        foreach (var key in authFailures.OrderBy(kv => kv.Value.Last)
                     .Take(authFailures.Count - AuthFailureCap)
                     .Select(kv => kv.Key)
                     .ToList( ))
        {
            authFailures.TryRemove(key, out _);
        }
    }
}
