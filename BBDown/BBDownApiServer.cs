using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Tasks;
using System.Threading;

using BBDown.Core;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace BBDown;

public class BBDownApiServer
{
    private WebApplication? app;
    private readonly ConcurrentDictionary<string, DownloadTask> runningTasks = new( );
    private readonly ConcurrentDictionary<string, DownloadTask> finishedTasks = new( );
    private string? serveToken;
    private bool authRequired;
    private bool authFinalized;
    private string? serveWorkDir;
    private string? serveHost;
    private string? serveEpHost;
    private string? serveTvHost;
    private SemaphoreSlim? taskGate;   // null = 不限制（历史行为）
    private int maxChunkParallelism;   // 0 = 交给 ProcessorCount

    // 回调专用 client（§2.3）：禁止自动重定向，杜绝 302 跳进内网/云元数据面；
    // 并在真正建立 TCP 连接前对最终端点 IP 做二次校验，消除 DNS 重绑定窗口（TOCTOU-free）。
    private static readonly HttpClient WebHookClient = new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        ConnectCallback = async (context, token) =>
        {
            var endpoint = context.DnsEndPoint;
            IPAddress ip;
            if (IPAddress.TryParse(endpoint.Host, out var literal))
            {
                ip = literal;
            }
            else
            {
                var addresses = await Dns.GetHostAddressesAsync(endpoint.Host, token);
                if (addresses.Length == 0)
                {
                    throw new HttpRequestException($"CallBackWebHook 无法解析 {endpoint.Host}");
                }

                ip = addresses[0];
            }

            // 连接前最终判定：私网/回环/链路本地/未指定地址一律拒绝
            if (IsPrivateAddress(ip))
            {
                throw new HttpRequestException($"CallBackWebHook 拒绝内网/回环地址 {ip}");
            }

            var socket = new Socket(ip.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            await socket.ConnectAsync(new IPEndPoint(ip, endpoint.Port), token);
            return new NetworkStream(socket, ownsSocket: true);
        }
    })
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    // 主机可控字段（外部程序路径、落盘目录/文件名、进程级 Debug/UserAgent、本地配置、API host）
    // 一律由服务端决定：前四类根本不在 ServeRequestOptions 中；host 三兄弟原本也在 DTO 里，
    // 但因请求不带 cookie 时会回落本机 SESSDATA，攻击者填个恶意 host 就能把登录态骗到自己服务器（P0-1），
    // 故已移出请求契约，改为 serve 启动参数固定（见 ApplyServeHost）。

    /// <summary>
    /// CallBackWebHook 仅允许公网 http/https，拒绝回环与内网地址，避免 SSRF 探活 169.254.169.254 等元数据服务（P1-14）
    /// </summary>
    internal static bool IsSafeWebHook(Uri uri)
    {
        if (uri.Scheme is not ("http" or "https"))
        {
            return false;
        }

        if (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !IPAddress.TryParse(uri.Host, out var ip) || !IsPrivateAddress(ip);
    }

    // 内部可见：供单测覆盖新增的私网段（§2.4）
    internal static bool IsPrivateAddress(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip))
        {
            return true;
        }

        // 未指定地址：IPv6 :: 作为出向目标等同本机，应拒绝（原实现漏网，§2.4）
        if (IPAddress.IPv6Any.Equals(ip))
        {
            return true;
        }

        var bytes = ip.GetAddressBytes( );
        return ip.AddressFamily switch
        {
            AddressFamily.InterNetwork =>
                bytes[0] == 10 ||
                (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) ||
                (bytes[0] == 192 && bytes[1] == 168) ||
                // 链路本地，含 169.254.169.254 云元数据地址
                (bytes[0] == 169 && bytes[1] == 254) ||
                bytes[0] == 127 ||
                // 0.0.0.0/8 为保留/未指定地址，作为出向 webhook 目标等同本机（P1-14）
                bytes[0] == 0 ||
                // CGNAT 共享地址（运营商级 NAT，原实现漏网，§2.4）
                (bytes[0] == 100 && bytes[1] is >= 64 and <= 127) ||
                // 192.0.0.0/24（原实现漏网，§2.4）
                (bytes[0] == 192 && bytes[1] == 0 && bytes[2] == 0) ||
                // 198.18.0.0/15 基准网络（benchmark，原实现漏网，§2.4）
                (bytes[0] == 198 && bytes[1] is >= 18 and <= 19) ||
                // 多播 224.0.0.0/4（原实现漏网，§2.4）
                (bytes[0] is >= 224 and <= 239),
            AddressFamily.InterNetworkV6 =>
                ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal ||
                // 用内建判定替代脆弱的字符串前缀比较（原实现对 fc/fd 做 StartsWith，§2.4）
                ip.IsIPv6UniqueLocal || ip.IsIPv6Multicast,
            _ => true
        };
    }

    private static bool IsLoopbackUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return IPAddress.TryParse(uri.Host, out var ip) && IPAddress.IsLoopback(ip);
    }

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

        if (IsLoopbackUrl(url)) { authRequired = false; return; }

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
        // <=0 一律视为不限制：不建闸门、分片并发交回 ProcessorCount，行为与旧版一致
        if (maxConcurrent > 0)
        {
            taskGate = new SemaphoreSlim(maxConcurrent, maxConcurrent);
            // 任务并发 × 分片并发 = N：限流时把单文件分片并发压到 1，总下载连接数即不超过 N
            maxChunkParallelism = 1;
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
        app.Use(async (context, next) =>
        {
            if (!authRequired) { await next( ); return; }

            if (TokenMatches(context.Request)) { await next( ); return; }

            context.Response.StatusCode = 401;
            await context.Response.WriteAsync("401 Unauthorized: 缺少或错误的 X-BBDown-Token");
        });
        var taskStatusApi = app.MapGroup("/get-tasks");
        taskStatusApi.MapGet("/", handler: ( ) => Results.Json(new DownloadTaskSnapshot(Snapshot(runningTasks), Snapshot(finishedTasks)), AppJsonSerializerContext.Default.DownloadTaskSnapshot));
        taskStatusApi.MapGet("/running", handler: ( ) => Results.Json(Snapshot(runningTasks), AppJsonSerializerContext.Default.ListDownloadTask));
        taskStatusApi.MapGet("/finished", handler: ( ) => Results.Json(Snapshot(finishedTasks), AppJsonSerializerContext.Default.ListDownloadTask));
        taskStatusApi.MapGet("/{id}", (string id) =>
        {
            if (!runningTasks.TryGetValue(id, out var task) && !finishedTasks.TryGetValue(id, out task))
            {
                return Results.NotFound( );
            }

            return Results.Json(task, AppJsonSerializerContext.Default.DownloadTask);
        });
        app.MapPost("/add-task", (ServeBindingResult<ServeRequestOptions> bindingResult) =>
        {
            if (!bindingResult.IsValid)
            {
                //var exception = bindingResult.Exception;
                return Results.BadRequest("输入有误");
            }

            var req = bindingResult.Result!;
            _ = RunTaskAndCallBackAsync(req);
            return Results.Ok( );
        });
        // 变更类端点必须用 POST，不能暴露为 GET，否则与本就全开的 CORS 叠加形成 CSRF（P1-15）
        var finishedRemovalApi = app.MapGroup("remove-finished");
        finishedRemovalApi.MapPost("/", ( ) => { finishedTasks.Clear( ); return Results.Ok( ); });
        finishedRemovalApi.MapPost("/failed", ( ) =>
        {
            foreach (var (aid, t) in finishedTasks)
            {
                if (!t.IsSuccessful)
                {
                    finishedTasks.TryRemove(aid, out _);
                }
            }

            return Results.Ok( );
        });
        finishedRemovalApi.MapPost("/{id}", (string id) => { finishedTasks.TryRemove(id, out _); return Results.Ok( ); });
    }

    private static List<DownloadTask> Snapshot(ConcurrentDictionary<string, DownloadTask> tasks)
    {
        return [.. tasks.Values];
    }

    // 请求线程不等待下载完成，因此这里必须自己兜住所有异常，否则会变成 UnobservedTaskException
    private async Task RunTaskAndCallBackAsync(ServeRequestOptions req)
    {
        DownloadTask? downloadTask;
        try
        {
            downloadTask = await AddDownloadTaskAsync(req.ToDownloadOptions( ));
        }
        catch (Exception e)
        {
            Logger.LogError($"任务创建失败: {e.Message}");
            return;
        }

        if (string.IsNullOrEmpty(req.CallBackWebHook))
        {
            return;
        }

        if (!Uri.TryCreate(req.CallBackWebHook, UriKind.Absolute, out var hookUri) || !IsSafeWebHook(hookUri))
        {
            Logger.LogWarn("忽略不安全的 CallBackWebHook（仅允许公网 http/https，拒绝内网/回环地址）");
            return;
        }

        try
        {
            var jsonContent = JsonSerializer.Serialize(downloadTask, AppJsonSerializerContext.Default.DownloadTask);
            using var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
            // 走专用 WebHookClient：关重定向 + 连接前二次校验私网（§2.3），不使用共享的 AppHttpClient
            using var response = await WebHookClient.PostAsync(hookUri, content, Program.CancellationToken);
        }
        catch (Exception e)
        {
            Logger.LogDebug("回调失败: {0}", e.Message);
        }
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
    /// 与阻塞的 <see cref="Run"/> 不同，这里用 StartAsync 以便在测试结束时 <see cref="StopForTestAsync"/>。
    /// </summary>
    internal async Task<string> StartForTestAsync(string listenUrl = "http://127.0.0.1:0")
    {
        SetUpServer(null, listenUrl);
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

    private async Task<DownloadTask> AddDownloadTaskAsync(DownloadOptions option)
    {
        option = ApplyServeWorkDir(option);
        option = ApplyServeHost(option);

        var (cookie, token) = CredentialStore.LoadAll(option.Cookie, option.AccessToken, option.UseTvApi, option.UseAppApi);
        var aid = await InputResolver.GetAvIdAsync(option.Url, new AppConfig(cookie, token, option.Host, option.EpHost, option.TvHost, option.Area, ""));
        var task = CreateTask(aid, option.Url);
        var claimed = runningTasks.GetOrAdd(aid, task);
        if (!ReferenceEquals(claimed, task))
        {
            return claimed;
        }

        try
        {
            await RunGatedAsync(task, ( ) => Program.RunDownloadAsync(option, task, Program.CancellationToken), Program.CancellationToken);
            task.IsSuccessful = true;
        }
        catch (OperationCanceledException) when (Program.CancellationToken.IsCancellationRequested)
        {
            // 关服（Ctrl+C）时排队中的任务会在闸门处被取消，属正常退出路径，不该刷成"下载失败"
            Logger.LogWarn($"{aid} 已取消（服务器正在退出）");
        }
        catch (Exception e)
        {
            // 走 Logger 才有全局锁，serve 模式并发任务直接写 Console 会互相插字（P1-17）
            var msg = Config.DebugLog ? e.ToString( ) : e.Message;
            Logger.LogError($"{aid} 下载失败：{msg}");
        }

        task.Status = DownloadStatus.Finished;
        task.TaskFinishTime = DateTimeOffset.Now.ToUnixTimeMilliseconds( );
        if (task.IsSuccessful)
        {
            task.Progress = 1f;
            var elapsedMs = task.TaskFinishTime.Value - task.TaskCreateTime;
            task.DownloadSpeed = elapsedMs > 0 ? task.TotalDownloadedBytes * 1000 / elapsedMs : 0;
        }

        runningTasks.TryRemove(aid, out _);
        finishedTasks[aid] = task;
        TrimFinishedTasks( );
        return task;
    }

    // 任务的初始状态与分片并发上限完全由服务端限流配置决定，抽成方法便于单测观测
    internal DownloadTask CreateTask(string aid, string url) => new(aid, url, DateTimeOffset.Now.ToUnixTimeMilliseconds( ))
    {
        // 未限流时不存在排队阶段，直接标 Running，避免 /get-tasks 出现假 Queued
        Status = taskGate is null ? DownloadStatus.Running : DownloadStatus.Queued,
        MaxChunkParallelism = maxChunkParallelism,
    };

    // 任务级并发闸门：未限流时直接执行；限流时先排队取额度（期间 Status=Queued），
    // 取到后转 Running，无论成败都在 finally 归还额度（不占线程、不持锁）
    internal async Task RunGatedAsync(DownloadTask task, Func<Task> download, CancellationToken ct)
    {
        if (taskGate is null)
        {
            task.Status = DownloadStatus.Running;
            await download( );
            return;
        }

        await taskGate.WaitAsync(ct);
        task.Status = DownloadStatus.Running;
        try
        {
            await download( );
        }
        finally
        {
            taskGate.Release( );
        }
    }

    // serve 模式的工作目录由启动参数 --work-dir 决定，覆盖请求体（请求体根本不含该字段），
    // 这样客户端无法把落盘位置指向任意目录（P0-2 / P1-16）
    internal DownloadOptions ApplyServeWorkDir(DownloadOptions option)
    {
        if (!string.IsNullOrEmpty(serveWorkDir))
        {
            option.WorkDir = serveWorkDir;
        }

        return option;
    }

    // serve 模式的 API host 由启动参数（--host/--ep-host/--tv-host）决定，覆盖请求体（请求体已不含该字段），
    // 客户端无法把请求导向自己控制的服务器、从而窃走操作者的 SESSDATA（P0-1）。空值回落官方默认 host。
    internal DownloadOptions ApplyServeHost(DownloadOptions option)
    {
        option.Host = string.IsNullOrWhiteSpace(serveHost) ? BiliApi.MainHost : serveHost.Trim( );
        option.EpHost = string.IsNullOrWhiteSpace(serveEpHost) ? BiliApi.MainHost : serveEpHost.Trim( );
        option.TvHost = string.IsNullOrWhiteSpace(serveTvHost) ? BiliApi.TvHost : serveTvHost.Trim( );
        return option;
    }

    // 已完成任务无上限增长会造成内存泄漏，超过阈值后按完成时间淘汰最旧的（P1-18）
    private const int MaxFinishedTasks = 200;

    private void TrimFinishedTasks( )
    {
        while (finishedTasks.Count > MaxFinishedTasks)
        {
            var oldest = finishedTasks.Values.OrderBy(t => t.TaskFinishTime).FirstOrDefault( );
            if (oldest is null || !finishedTasks.TryRemove(oldest.Aid, out _))
            {
                break;
            }
        }
    }
}

[JsonConverter(typeof(JsonStringEnumConverter<DownloadStatus>))]
public enum DownloadStatus
{
    Queued,   // 已受理、等待并发额度（仅 --max-concurrent > 0 时出现）
    Running,  // 下载中
    Finished, // 已结束，成败见 IsSuccessful
}

public record DownloadTask(string Aid, string Url, long TaskCreateTime)
{
    public string? Title { get; set; }
    public string? Pic { get; set; }
    public long? VideoPubTime { get; set; }
    public long? TaskFinishTime { get; set; }
    public double Progress { get; set; }
    public double DownloadSpeed { get; set; }
    public double TotalDownloadedBytes { get; set; }
    public bool IsSuccessful { get; set; }
    public DownloadStatus Status { get; set; }

    // 服务端限流用：单文件分片并发上限；<=0 表示不限制（Parallel 取 ProcessorCount）。
    // internal 属性不会被 AppJsonSerializerContext 序列化，客户端也无法设置。
    internal int MaxChunkParallelism { get; set; }

    public Collection<string> SavePaths { get; } = [];

    /// <summary>进度条的采样回调：<paramref name="bytesDelta"/> 是本采样周期新增的字节数。</summary>
    public void ApplySample(double ratio, long bytesDelta)
    {
        Progress = ratio;
        // 一个周期一个字节都没到（卡住或已下完）时保留上一次的速度，不要显示成 0
        if (bytesDelta <= 0)
        {
            return;
        }

        DownloadSpeed = bytesDelta;
        TotalDownloadedBytes += bytesDelta;
    }
};
public record DownloadTaskSnapshot(IReadOnlyList<DownloadTask> Running, IReadOnlyList<DownloadTask> Finished);

internal record struct ServeBindingResult<T>(T? Result, Exception? Exception)
{
    public readonly bool IsValid => Exception is null;

    public static async ValueTask<ServeBindingResult<T>> BindAsync(HttpContext httpContext)
    {
        try
        {
            var jsonTypeInfo = DownloadOptionsJsonContext.Default.GetTypeInfo(typeof(T));
            if (jsonTypeInfo is null)
            {
                return new(default, new InvalidOperationException($"Cannot find TypeInfo for type {typeof(T)}"));
            }

            var item = await httpContext.Request.ReadFromJsonAsync(jsonTypeInfo);

            if (item is null)
            {
                return new(default, new NoNullAllowedException( ));
            }

            return new((T) item, null);
        }
        catch (Exception ex)
        {
            return new(default, ex);
        }
    }
}

[JsonSerializable(typeof(ProblemDetails))]
[JsonSerializable(typeof(ValidationProblemDetails))]
[JsonSerializable(typeof(HttpValidationProblemDetails))]
[JsonSerializable(typeof(DownloadTask))]
[JsonSerializable(typeof(List<DownloadTask>))]
[JsonSerializable(typeof(DownloadTaskSnapshot))]
public partial class AppJsonSerializerContext : JsonSerializerContext;
