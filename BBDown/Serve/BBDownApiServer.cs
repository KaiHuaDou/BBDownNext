using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace BBDown.Serve;

public partial class BBDownApiServer
{
    private WebApplication? app;
    // 任务表以 ResourceId 为键：值相等性天然去重，同资源重复提交直接命中已有任务，无需字符串形态
    private readonly ConcurrentDictionary<ResourceId, DownloadTask> runningTasks = new( );
    private readonly ConcurrentDictionary<ResourceId, DownloadTask> finishedTasks = new( );
    private string? serveToken;
    private bool authRequired;
    private bool authFinalized;
    private string? serveWorkDir;
    private string? serveHost;
    private string? serveEpHost;
    private string? serveTvHost;
    private SemaphoreSlim? taskGate;   // null = 不限制（历史行为）

    // 主机可控字段（外部程序路径、落盘目录/文件名、进程级 Debug/UserAgent、本地配置、API host）
    // 一律由服务端决定：前四类根本不在 ServeRequestOptions 中；host 三兄弟原本也在 DTO 里，
    // 但因请求不带 cookie 时会回落本机 SESSDATA，攻击者填个恶意 host 就能把登录态骗到自己服务器（P0-1），
    // 故已移出请求契约，改为 serve 启动参数固定（见 ApplyServeHost）。

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

    private bool TokenMatches(HttpRequest request)
    {
        if (serveToken is null)
        {
            return false;
        }

        if (request.Headers.TryGetValue("X-BBDown-Token", out var headerToken) && headerToken == serveToken)
        {
            return true;
        }

        if (request.Query.TryGetValue("token", out var queryToken) && queryToken == serveToken)
        {
            return true;
        }

        return false;
    }

    public void SetUpServer(string? workDir = null, string? listenUrl = null, string? serveToken = null, string? host = null, string? epHost = null, string? tvHost = null, string? corsOrigin = null, int maxConcurrent = 0)
    {
        if (app is not null)
        {
            return;
        }

        this.serveToken = serveToken;
        serveWorkDir = workDir;
        serveHost = host;
        serveEpHost = epHost;
        serveTvHost = tvHost;
        // <=0 一律视为不限制：不建闸门，行为与旧版一致；>0 时仅限制同时下载的任务数，
        // 多余任务排队，单个任务内部的下载并行度交给多线程下载器自行决定（不再压到 1）
        if (maxConcurrent > 0)
        {
            taskGate = new SemaphoreSlim(maxConcurrent, maxConcurrent);
        }

        if (!string.IsNullOrEmpty(listenUrl))
        {
            FinalizeAuth(listenUrl);
        }

        var builder = WebApplication.CreateSlimBuilder( );
        // 仅供集成测试：在指定地址（通常为 http://127.0.0.1:0 随机端口）绑定，避免占用生产默认端口
        if (!string.IsNullOrEmpty(listenUrl))
        {
            builder.WebHost.UseUrls(listenUrl);
        }

        builder.Services.ConfigureHttpJsonOptions((options) => options.SerializerOptions.TypeInfoResolver = JsonTypeInfoResolver.Combine(options.SerializerOptions.TypeInfoResolver, AppJsonSerializerContext.Default));

        // CORS 默认全关（§2.1-C）：浏览器跨源（含恶意网页）的预检会因缺少 ACAO 头被拦，从根本上消除 CSRF 面。
        // 仅当显式给出 --cors-origin 时才开放给该单一来源（用于同源之外的 Web 前端）。
        if (!string.IsNullOrWhiteSpace(corsOrigin))
        {
            builder.Services.AddCors((options) =>
            {
                options.AddPolicy("AllowSpecificOrigin",
                    policy => policy.WithOrigins(corsOrigin).AllowAnyMethod( ).AllowAnyHeader( ));
            });
        }

        app = builder.Build( );
        if (!string.IsNullOrWhiteSpace(corsOrigin))
        {
            app.UseCors("AllowSpecificOrigin");
        }

        MapServeEndpoints(app);
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
        SetUpServer(null, listenUrl, serveToken);
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
}
