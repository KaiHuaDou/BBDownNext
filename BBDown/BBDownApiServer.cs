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
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core;
using BBDown.Core.Util;

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
    private static readonly Lock workContextGate = new( );
    private string? serveToken;
    private bool authRequired;
    private bool authFinalized;
    private string? serveWorkDir;

    // 主机可控字段（外部程序路径、落盘目录/文件名、进程级 Debug/UserAgent、本地配置）一律由服务端决定，
    // 不会出现在 ServeRequestOptions 中，因此不存在远程注入这些字段的入口（P0-2 / P1-16）。

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

    private static bool IsPrivateAddress(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip))
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
                // 链路本地，含 169.254.169.254 元数据地址
                (bytes[0] == 169 && bytes[1] == 254) ||
                bytes[0] == 127 ||
                // 0.0.0.0/8 为保留/未指定地址，作为出向 webhook 目标等同于本机，应拒绝（P1-14）
                bytes[0] == 0,
            AddressFamily.InterNetworkV6 =>
                ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal ||
                ip.ToString( ).StartsWith("fc", StringComparison.OrdinalIgnoreCase) ||
                ip.ToString( ).StartsWith("fd", StringComparison.OrdinalIgnoreCase),
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

    public void SetUpServer(string? workDir = null, string? listenUrl = null, string? serveToken = null)
    {
        if (app is not null)
        {
            return;
        }

        this.serveToken = serveToken;
        serveWorkDir = workDir;
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
        builder.Services.AddCors((options) =>
        {
            options.AddPolicy("AllowAnyOrigin",
                policy =>
                {
                    policy.AllowAnyOrigin( )
                          .AllowAnyMethod( )
                          .AllowAnyHeader( );
                });
        });
        app = builder.Build( );
        app.UseCors("AllowAnyOrigin");
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
            using var response = await HTTPUtil.AppHttpClient.PostAsync(hookUri, content, Program.CancellationToken);
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

        var (cookie, token) = CredentialStore.LoadAll(option.Cookie, option.AccessToken, option.UseTvApi, option.UseAppApi);
        var aid = await InputResolver.GetAvIdAsync(option.Url, new AppConfig(cookie, token, option.Host, option.EpHost, option.TvHost, option.Area, ""));
        var task = new DownloadTask(aid, option.Url, DateTimeOffset.Now.ToUnixTimeMilliseconds( ));
        var claimed = runningTasks.GetOrAdd(aid, task);
        if (!ReferenceEquals(claimed, task))
        {
            return claimed;
        }

        try
        {
            // BuildWorkContext 内部有二进制查找等进程级初始化，串行化后并发请求不会互相踩踏（P1-16）
            WorkContext taskCtx;
            lock (workContextGate)
            {
                taskCtx = Program.BuildWorkContext(option);
            }

            taskCtx = await Program.GetVideoInfoAsync(option, taskCtx);
            task.Title = taskCtx.VInfo!.Title;
            task.Pic = taskCtx.VInfo.Pic;
            task.VideoPubTime = taskCtx.VInfo.PubTime;
            await Program.DownloadPagesAsync(option, taskCtx, task, Program.CancellationToken);
            task.IsSuccessful = true;
        }
        catch (Exception e)
        {
            // 走 Logger 才有全局锁，serve 模式并发任务直接写 Console 会互相插字（P1-17）
            var msg = Config.DebugLog ? e.ToString( ) : e.Message;
            Logger.LogError($"{aid} 下载失败：{msg}");
        }

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
