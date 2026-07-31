using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Tasks;

using BBDown.Core;
using BBDown.Core.Util;

using Microsoft.AspNetCore.Builder;
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

    /// <summary>
    /// serve 模式没有任何认证，这几个字段直接决定被拉起的进程及其参数与落盘位置，
    /// 接受请求注入等同于把远程命令执行开放给任何能访问该端口的人，故一律以服务端配置为准
    /// </summary>
    private void OverrideHostControlledOptions(ServeRequestOptions option)
    {
        option.FFmpegPath = "";
        option.Mp4boxPath = "";
        option.Aria2cPath = "";
        option.Aria2cArgs = "";
        option.WorkDir = serveWorkDir;
    }

    public void SetUpServer(string? workDir = null)
    {
        if (app is not null) return;
        serveWorkDir = workDir ?? "";
        var builder = WebApplication.CreateSlimBuilder( );
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
        var finishedRemovalApi = app.MapGroup("remove-finished");
        finishedRemovalApi.MapGet("/", ( ) => { finishedTasks.Clear( ); return Results.Ok( ); });
        finishedRemovalApi.MapGet("/failed", ( ) =>
        {
            foreach (var (aid, t) in finishedTasks)
            {
                if (!t.IsSuccessful) finishedTasks.TryRemove(aid, out _);
            }

            return Results.Ok( );
        });
        finishedRemovalApi.MapGet("/{id}", (string id) => { finishedTasks.TryRemove(id, out _); return Results.Ok( ); });
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

        try
        {
            var jsonContent = JsonSerializer.Serialize(downloadTask, AppJsonSerializerContext.Default.DownloadTask);
            using var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
            using var response = await HTTPUtil.AppHttpClient.PostAsync(new Uri(req.CallBackWebHook), content, Program.CancellationToken);
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
            var taskCtx = Program.BuildWorkContext(option);
            taskCtx = await Program.GetVideoInfoAsync(option, taskCtx);
            task.Title = taskCtx.VInfo!.Title;
            task.Pic = taskCtx.VInfo.Pic;
            task.VideoPubTime = taskCtx.VInfo.PubTime;
            await Program.DownloadPagesAsync(option, taskCtx, task, Program.CancellationToken);
            task.IsSuccessful = true;
        }
        catch (Exception e)
        {
            Console.BackgroundColor = ConsoleColor.Red;
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine($"{aid} 下载失败");
            var msg = Config.DebugLog ? e.ToString( ) : e.Message;
            Console.Write($"{msg}{Environment.NewLine}请尝试升级到最新版本后重试！");
            Console.ResetColor( );
            Console.WriteLine( );
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
        return task;
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
