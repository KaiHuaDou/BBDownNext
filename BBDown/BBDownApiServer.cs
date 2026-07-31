using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace BBDown;

public class BBDownApiServer
{
    private WebApplication? app;
    private readonly List<DownloadTask> runningTasks = [];
    private readonly List<DownloadTask> finishedTasks = [];

    public void SetUpServer( )
    {
        if (app is not null) return;
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
        taskStatusApi.MapGet("/", handler: ( ) => Results.Json(new DownloadTaskSnapshot(runningTasks, finishedTasks), AppJsonSerializerContext.Default.DownloadTaskSnapshot));
        taskStatusApi.MapGet("/running", handler: ( ) => Results.Json(runningTasks, AppJsonSerializerContext.Default.ListDownloadTask));
        taskStatusApi.MapGet("/finished", handler: ( ) => Results.Json(finishedTasks, AppJsonSerializerContext.Default.ListDownloadTask));
        taskStatusApi.MapGet("/{id}", (string id) =>
        {
            var task = finishedTasks.FirstOrDefault(a => a.Aid == id);
            var rtask = runningTasks.FirstOrDefault(a => a.Aid == id);
            if (rtask is not null) task = rtask;
            if (task is null)
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
            _ = AddDownloadTaskAsync(req)
                .ContinueWith(async task =>
                {
                    // send request to callback webhook
                    if (string.IsNullOrEmpty(req.CallBackWebHook))
                    {
                        return;
                    }

                    var callback = req.CallBackWebHook;
                    using var client = new HttpClient( );
                    var downloadTask = await task;
                    var jsonContent = JsonSerializer.Serialize(downloadTask, AppJsonSerializerContext.Default.DownloadTask);
                    try
                    {
                        using var content = new StringContent(jsonContent, System.Text.Encoding.UTF8, "application/json");
                        await client.PostAsync(new Uri(callback), content);
                    }
                    catch (System.Exception e)
                    {
                        Logger.LogDebug("回调失败: {0}", e.Message);
                    }
                });
            return Results.Ok( );
        });
        var finishedRemovalApi = app.MapGroup("remove-finished");
        finishedRemovalApi.MapGet("/", ( ) => { finishedTasks.RemoveAll(t => true); return Results.Ok( ); });
        finishedRemovalApi.MapGet("/failed", ( ) => { finishedTasks.RemoveAll(t => !t.IsSuccessful); return Results.Ok( ); });
        finishedRemovalApi.MapGet("/{id}", (string id) => { finishedTasks.RemoveAll(t => t.Aid == id); return Results.Ok( ); });
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
            Thread.Sleep(1);
            Environment.Exit(1);
        }

        app.Run(url);
    }

    private async Task<DownloadTask> AddDownloadTaskAsync(MyOption option)
    {
        var aid = await Utils.GetAvIdAsync(option.Url, new AppConfig(option.Cookie ?? "", option.AccessToken?.Replace("access_token=", "") ?? "", option.Host, option.EpHost, option.TvHost, option.Area, ""));
        var runningTask = runningTasks.FirstOrDefault(task => task.Aid == aid);
        if (runningTask is not null)
        {
            return runningTask;
        }

        ;
        var task = new DownloadTask(aid, option.Url, DateTimeOffset.Now.ToUnixTimeSeconds( ));
        runningTasks.Add(task);
        try
        {
            var taskCtx = Program.BuildWorkContext(option);
            taskCtx = await Program.GetVideoInfoAsync(option, taskCtx);
            task.Title = taskCtx.VInfo!.Title;
            task.Pic = taskCtx.VInfo.Pic;
            task.VideoPubTime = taskCtx.VInfo.PubTime;
            await Program.DownloadPagesAsync(option, taskCtx, task);
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

        task.TaskFinishTime = DateTimeOffset.Now.ToUnixTimeSeconds( );
        if (task.IsSuccessful)
        {
            task.Progress = 1f;
            task.DownloadSpeed = (double) (task.TotalDownloadedBytes / (task.TaskFinishTime - task.TaskCreateTime));
        }

        runningTasks.Remove(task);
        finishedTasks.Add(task);
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
            var jsonTypeInfo = SourceGenerationContext.Default.GetTypeInfo(typeof(T));
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

[JsonSerializable(typeof(MyOption))]
[JsonSerializable(typeof(ServeRequestOptions))]
internal sealed partial class SourceGenerationContext : JsonSerializerContext
{

}
