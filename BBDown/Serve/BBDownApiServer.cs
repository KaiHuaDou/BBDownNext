using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json.Serialization.Metadata;
using System.Threading.RateLimiting;
using System.Threading.Tasks;

using BBDown.Serve.Http;
using BBDown.Serve.Tasks;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;

namespace BBDown.Serve;

public class BBDownApiServer
{
    internal const string DefaultListenUrl = "http://127.0.0.1:23333";

    private const int AuthFailureLimit = 5;
    private static readonly TimeSpan AuthFailureWindow = TimeSpan.FromMinutes(1);
    private const int AuthFailureCap = 1024;

    // 认证失败滑动窗口（实例级：每个服务实例独立限速）：每 IP 每分钟 5 次，超限 429（防令牌暴力）
    private readonly ConcurrentDictionary<string, (int Count, DateTimeOffset Start)> authFailures = new( );

    private WebApplication? app;
    private string? serveToken;
    private bool authRequired;
    private bool authFinalized;

    private static string GenerateServeToken( )
    {
        return Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
    }

    // 决定是否需要鉴权：显式提供令牌 / 绑定非回环地址 => 强制；仅本机回环 => 免令牌
    private void FinalizeAuth(string url)
    {
        if (authFinalized)
        {
            return;
        }

        authFinalized = true;
        if (serveToken is not null) { authRequired = true; return; }

        if (SsrfGuard.IsLoopbackUrl(url)) { authRequired = false; return; }

        serveToken = GenerateServeToken( );
        authRequired = true;
        Console.BackgroundColor = ConsoleColor.DarkYellow;
        Console.ForegroundColor = ConsoleColor.Black;
        Console.WriteLine($"serve 模式绑定到非回环地址，已生成鉴权令牌，客户端需带 X-BBDown-Token: {serveToken}");
        Console.ResetColor( );
    }

    internal void SetUpServer(ServeConfig config)
    {
        if (app is not null)
        {
            return;
        }

        serveToken = config.ServeToken;
        // 鉴权判定须在注册认证管道前完成（回环免令牌 / 非回环强制令牌），Run 传入的 URL 与之等价
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

        // CORS 默认全关（§2.1-C）：浏览器跨源（含恶意网页）的预检会因缺少 ACAO 头被拦，从根本上消除 CSRF 面。
        // 仅当显式给出 --cors-origin 时才开放给该单一来源（用于同源之外的 Web 前端）。
        if (!string.IsNullOrWhiteSpace(config.CorsOrigin))
        {
            builder.Services.AddCors((options) =>
            {
                options.AddPolicy("AllowSpecificOrigin",
                    policy => policy.WithOrigins(config.CorsOrigin).AllowAnyMethod( ).AllowAnyHeader( ));
            });
        }

        // 全局限流（per-IP 固定窗口）兜底滥用；任务提交独立策略（防批量拉任务）
        builder.Services.AddRateLimiter(options =>
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
        });

        builder.Services.AddAuthentication(ApiKeyAuthenticationOptions.DefaultScheme)
            .AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(
                ApiKeyAuthenticationOptions.DefaultScheme,
                options => options.ExpectedToken = serveToken);
        // UseAuthorization 要求授权服务始终注册；FallbackPolicy 默认拒绝仅鉴权模式启用，
        // 回环免令牌时无 FallbackPolicy，匿名全放行
        builder.Services.AddAuthorization(options =>
        {
            if (authRequired)
            {
                options.FallbackPolicy = new AuthorizationPolicyBuilder( ).RequireAuthenticatedUser( ).Build( );
            }
        });

        builder.Services.AddSingleton(config);
        builder.Services.AddSingleton<TaskQueue>(new TaskQueue( ));
        builder.Services.AddSingleton(sp => new TaskStore(config, sp.GetRequiredService<TaskQueue>( )));
        builder.Services.AddSingleton(sp => new TaskWorker(sp.GetRequiredService<TaskQueue>( ), sp.GetRequiredService<TaskStore>( ), config.MaxConcurrent));
        builder.Services.AddHostedService(sp => sp.GetRequiredService<TaskWorker>( ));
        builder.Services.AddSingleton(sp => new TaskSocketHub(sp.GetRequiredService<TaskStore>( )));

        app = builder.Build( );

        // 交互开启时：日志消息经桥接器按任务路由进事件流（WebSocket）。
        // MessageBus 静态订阅持有桥接器，存活至进程退出，无需字段引用或释放。
        if (config.Interactive)
        {
            // 桥接器被 MessageBus 静态订阅持有，存活至进程退出，无需字段引用或释放
#pragma warning disable CA2000
            _ = new TaskMessageBridge(app.Services.GetRequiredService<TaskStore>( ));
#pragma warning restore CA2000
        }

        // 安全响应头：所有响应（含 4xx）统一携带
        app.Use(async (context, next) =>
        {
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["X-Frame-Options"] = "DENY";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            context.Response.Headers["Permissions-Policy"] = "geolocation=(), microphone=(), camera=()";
            context.Response.Headers["Content-Security-Policy"] = "default-src 'none'";
            await next( );
        });
        if (!string.IsNullOrWhiteSpace(config.CorsOrigin))
        {
            app.UseCors("AllowSpecificOrigin");
        }

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
            Console.WriteLine($"{url} 不是合法的 http URL，url 示例：http://0.0.0.0:5000");
            Console.WriteLine("如果您需要 https，请额外配置反向代理");
            Console.ResetColor( );
            Console.WriteLine( );
            Environment.Exit(1);
        }

        app.Run(url);
    }

    /// <summary>
    /// 仅供集成测试：在随机端口启动服务并返回可访问的 base URL。
    /// 与阻塞的 <see cref="Run(Uri)"/> 不同，这里用 StartAsync 以便在测试结束时 <see cref="StopForTestAsync"/>。
    /// </summary>
    internal async Task<string> StartForTestAsync(string listenUrl = "http://127.0.0.1:0", string? serveToken = null)
    {
        SetUpServer(new ServeConfig(ListenUrl: listenUrl, ServeToken: serveToken));
        if (app is null)
        {
            throw new InvalidOperationException("WebApplication 未创建");
        }

        await app.StartAsync( );
        return app.Urls.First(u => u.Contains("127.0.0.1", StringComparison.OrdinalIgnoreCase));
    }

    internal Task StopForTestAsync( )
    {
        return app is null ? Task.CompletedTask : app.StopAsync( );
    }

    private bool ExceedsAuthFailureLimit(string ip)
    {
        var now = DateTimeOffset.UtcNow;
        var (count, _) = authFailures.AddOrUpdate(ip, (1, now), (_, v) =>
            now - v.Start > AuthFailureWindow ? (1, now) : (v.Count + 1, v.Start));
        if (count > AuthFailureLimit)
        {
            return true;
        }

        // 防条目无限增长：超上限整体清空（滑动窗口语义下影响有限）
        if (authFailures.Count > AuthFailureCap)
        {
            authFailures.Clear( );
        }

        return false;
    }
}
