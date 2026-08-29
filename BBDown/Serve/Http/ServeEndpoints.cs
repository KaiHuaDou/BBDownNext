using System;
using System.Threading;

using BBDown.Core;
using BBDown.Serve.Tasks;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace BBDown.Serve.Http;

/// <summary>
/// serve 端点注册：任务增删查（/api/v1/tasks 组）与 WebSocket 事件通道（/hubs/tasks）。
/// 鉴权中间件在 SetUpServer 注册，本类不持有任何服务状态。
/// </summary>
internal static class ServeEndpoints
{
    public static void MapServeEndpoints(this WebApplication app)
    {
        var tasks = app.MapGroup("/api/v1/tasks");
        tasks.MapGet("", (TaskStore store) => Results.Json(new DownloadTaskSnapshot(store.RunningSnapshot( ), store.FinishedSnapshot( )), AppJsonSerializerContext.Default.DownloadTaskSnapshot));
        tasks.MapGet("/running", (TaskStore store) => Results.Json(store.RunningSnapshot( ).FindAll(t => t.Status != DownloadStatus.Pending), AppJsonSerializerContext.Default.ListDownloadTask));
        tasks.MapGet("/finished", (TaskStore store) => Results.Json(store.FinishedSnapshot( ), AppJsonSerializerContext.Default.ListDownloadTask));
        tasks.MapGet("/{id}", (string id, TaskStore store) =>
        {
            // 路径参数为规范 id（如 av170001、season2539），解析失败视为不存在
            if (!ResourceId.TryParse(id, out var rid) || store.Get(rid) is not { } task)
            {
                return Results.NotFound( );
            }

            return Results.Json(task, AppJsonSerializerContext.Default.DownloadTask);
        });
        tasks.MapPost("", async (ServeBindingResult<ServeRequestOptions> bindingResult, TaskStore store, HttpContext http, CancellationToken token) =>
        {
            if (!bindingResult.IsValid)
            {
                return Results.BadRequest("输入有误");
            }

            // mode=enqueue 仅入暂停表（待 start）；缺省或 execute 受理即执行
            var mode = http.Request.Query["mode"].ToString( ) == "enqueue" ? SubmitMode.Enqueue : SubmitMode.Execute;
            try
            {
                var result = await store.EnqueueAsync(bindingResult.Result!, mode, token);
                if (result.QueueFull)
                {
                    return Results.StatusCode(StatusCodes.Status429TooManyRequests);
                }

                var task = result.Task!;
                // 重复提交同资源：命中已有任务，返回 200；新受理返回 202 + 任务位置
                return result.Duplicate
                    ? Results.Ok(task)
                    : Results.Accepted($"/api/v1/tasks/{task.Id}", task);
            }
            catch (ArgumentException)
            {
                // URL 无法识别（如非法输入），受理前返回 400
                return Results.BadRequest("输入有误");
            }
        }).RequireRateLimiting("taskSubmit");
        tasks.MapStartEndpoint( );
        // 变更类端点必须用 POST/DELETE，不能暴露为 GET，否则与本就全开的 CORS 叠加形成 CSRF（P1-15）
        tasks.MapPost("/{id}/stop", (string id, TaskStore store) =>
        {
            if (!ResourceId.TryParse(id, out var rid) || !store.CancelRunning(rid))
            {
                return Results.NotFound( );
            }

            return Results.Ok( );
        });
        tasks.MapDelete("/finished", (TaskStore store) =>
        {
            store.ClearFinished( );
            return Results.Ok( );
        });
        tasks.MapDelete("/finished/failed", (TaskStore store) =>
        {
            store.ClearFailedFinished( );
            return Results.Ok( );
        });
        tasks.MapDelete("/{id}", (string id, TaskStore store) =>
        {
            // 规范 id 解析失败视为不存在，仍返回 200（与旧行为一致：无论是否找到都 200）；
            // RemoveTask 同时清理已完成与 enqueue 暂停态任务
            if (ResourceId.TryParse(id, out var rid))
            {
                store.RemoveTask(rid);
            }

            return Results.Ok( );
        });

        // WebSocket 事件通道：升级前做 Origin（CSWSH）与连接上限校验，升级后交 TaskSocketHub 收发帧
        app.Map("/hubs/tasks", async (HttpContext context, TaskSocketHub hub, ServeConfig config) =>
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            if (!TaskSocketHub.IsAllowedOrigin(context.Request.Headers.Origin.ToString( ), config))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            var ip = context.Connection.RemoteIpAddress?.ToString( );
            if (!hub.TryEnter(ip))
            {
                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                return;
            }

            try
            {
                using var socket = await context.WebSockets.AcceptWebSocketAsync( );
                await hub.HandleAsync(socket, context.RequestAborted);
            }
            finally
            {
                hub.Leave(ip);
            }
        });

        // 健康检查：匿名放行（探活不要求令牌）；携带事件流开关（--no-interactive 为 false）供前端判定通道可用性；
        // 计数排除 enqueue 暂停态（Pending 尚未进入执行队列，不计入运行中）
        app.MapGet("/healthz", (TaskStore store, ServeConfig config) =>
                Results.Ok(new HealthStatus("ok", store.RunningSnapshot( ).FindAll(t => t.Status != DownloadStatus.Pending).Count, config.Interactive)))
            .AllowAnonymous( );
    }

    /// <summary>
    /// 启动 enqueue 暂停的任务：从暂停表取出执行信封投入执行队列（详见 <see cref="TaskStore.Start"/>）。
    /// </summary>
    private static void MapStartEndpoint(this RouteGroupBuilder tasks)
    {
        tasks.MapPost("/{id}/start", (string id, TaskStore store) =>
        {
            if (!ResourceId.TryParse(id, out var rid))
            {
                return Results.NotFound( );
            }

            return store.Start(rid) switch
            {
                StartResult.Started => Results.Ok( ),
                StartResult.QueueFull => Results.StatusCode(StatusCodes.Status429TooManyRequests),
                _ => Results.NotFound( )
            };
        });
    }
}
