using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
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
    private string serveWorkDir = "";
    private readonly ConcurrentDictionary<string, DownloadTask> runningTasks = new( );
    private readonly ConcurrentDictionary<string, DownloadTask> finishedTasks = new( );
    private static readonly object s_workContextGate = new( );

    /// <summary>
    /// serve 模式没有任何认证，这几个字段直接决定被拉起的进程及其参数与落盘位置，
    /// 接受请求注入等同于把远程命令执行开放给任何能访问该端口的人，故一律以服务端配置为准
    /// </summary>
    internal void OverrideHostControlledOptions(ServeRequestOptions option)
    {
        option.FFmpegPath = "";
        option.Mp4boxPath = "";
        option.Aria2cPath = "";
        option.Aria2cArgs = "";
        option.WorkDir = serveWorkDir;
        // FilePattern / MultiFilePattern 决定落盘文件名，若允许请求注入可借 ../ 逃逸工作目录写任意位置（P0-2）
        option.FilePattern = "";
        option.MultiFilePattern = "";
        // Debug 与 UserAgent 在 BuildWorkContext 里写进程级全局，逐请求取值会让并发任务互相改配置（P1-16）
        option.Debug = false;
        option.UserAgent = "";
    }

    /// <summary>
    /// CallBackWebHook 仅允许公网 http/https，拒绝回环与内网地址，避免 SSRF 探活 169.254.169.254 等元数据服务（P1-14）
    /// </summary>
    internal static bool IsSafeWebHook(Uri uri)
    {
        if (uri.Scheme is not ("http" or "https")) return false;
        if (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return false;
        return !IPAddress.TryParse(uri.Host, out var ip) || !IsPrivateAddress(ip);
    }

    private static bool IsPrivateAddress(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip)) return true;
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

    public void SetUpServer(string? workDir = null, string? listenUrl = null)
    {
        if (app is not null) return;
        serveWorkDir = workDir ?? "";
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
        app.MapPost("/add-task", (MyOptionBindingResult<ServeRequestOptions> bindingResult) =>
        {
            if (!bindingResult.IsValid)
            {
                //var exception = bindingResult.Exception;
                return Results.BadRequest("输入有误");
            }

            var req = bindingResult.Result!;
            OverrideHostControlledOptions(req);
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
                if (!t.IsSuccessful) finishedTasks.TryRemove(aid, out _);
            }

            return Results.Ok( );
        });
        finishedRemovalApi.MapPost("/{id}", (string id) => { finishedTasks.TryRemove(id, out _); return Results.Ok( ); });
    }

    private static List<DownloadTask> Snapshot(ConcurrentDictionary<string, DownloadTask> tasks) => [.. tasks.Values];

    // 请求线程不等待下载完成，因此这里必须自己兜住所有异常，否则会变成 UnobservedTaskException
    private async Task RunTaskAndCallBackAsync(ServeRequestOptions req)
    {
        DownloadTask? downloadTask;
        try
        {
            downloadTask = await AddDownloadTaskAsync(req);
        }
        catch (Exception e)
        {
            Logger.LogError($"任务创建失败: {e.Message}");
            return;
        }

        if (string.IsNullOrEmpty(req.CallBackWebHook)) return;

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
        if (app is null) return;
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
        if (app is null) throw new InvalidOperationException("WebApplication 未创建");
        await app.StartAsync( );
        return app.Urls.First(u => u.Contains("127.0.0.1", StringComparison.OrdinalIgnoreCase));
    }

    internal Task StopForTestAsync( )
    {
        return app is null ? Task.CompletedTask : app.StopAsync( );
    }

    private async Task<DownloadTask> AddDownloadTaskAsync(MyOption option)
    {
        var (cookie, token) = CredentialStore.LoadAll(option.Cookie, option.AccessToken, option.UseTvApi, option.UseAppApi);
        var aid = await Utils.GetAvIdAsync(option.Url, new AppConfig(cookie, token, option.Host, option.EpHost, option.TvHost, option.Area, ""));
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
            lock (s_workContextGate)
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

    // 已完成任务无上限增长会造成内存泄漏，超过阈值后按完成时间淘汰最旧的（P1-18）
    private const int MaxFinishedTasks = 200;

    private void TrimFinishedTasks( )
    {
        while (finishedTasks.Count > MaxFinishedTasks)
        {
            var oldest = finishedTasks.Values.OrderBy(t => t.TaskFinishTime).FirstOrDefault( );
            if (oldest is null || !finishedTasks.TryRemove(oldest.Aid, out _)) break;
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
};
public record DownloadTaskSnapshot(IReadOnlyList<DownloadTask> Running, IReadOnlyList<DownloadTask> Finished);

internal record struct MyOptionBindingResult<T>(T? Result, Exception? Exception)
{
    public bool IsValid => Exception is null;

    public static async ValueTask<MyOptionBindingResult<T>> BindAsync(HttpContext httpContext)
    {
        try
        {
            var jsonTypeInfo = MyOptionJsonContext.Default.GetTypeInfo(typeof(T));
            if (jsonTypeInfo is null)
            {
                return new(default, new InvalidOperationException($"Cannot find TypeInfo for type {typeof(T)}"));
            }

            var item = await httpContext.Request.ReadFromJsonAsync(jsonTypeInfo);

            if (item is null) return new(default, new NoNullAllowedException( ));

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
public partial class AppJsonSerializerContext : JsonSerializerContext
{

}
