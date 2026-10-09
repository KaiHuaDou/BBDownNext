using System;
using System.Globalization;
using System.Linq;
using System.Threading;

using BBDown.Core;
using BBDown.Core.Live;
using BBDown.Serve.Tasks;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace BBDown.Serve.Http;

/// <summary>
/// serve 端点注册：任务增删查（/api/v1/tasks 组）与 WebSocket 事件通道（/hubs/tasks）
/// 鉴权中间件在 SetUpServer 注册，本类不持有任何服务状态
/// </summary>
internal static class ServeEndpoints
{
    public static void MapServeEndpoints(this WebApplication app)
    {
        // 队列有界的退避提示：写满说明消费端积压，客户端按此重试（与限流 429 的 Retry-After 一致）
        const int QueueFullRetryAfter = 60;

        var tasks = app.MapGroup("/api/v1/tasks");
        tasks.MapGet("", (TaskStore store) => Results.Json(new DownloadTaskSnapshot(store.RunningSnapshot( ), store.FinishedSnapshot( )), AppJsonSerializerContext.Default.DownloadTaskSnapshot));
        tasks.MapGet("/running", (TaskStore store) => Results.Json(store.RunningOnlySnapshot( ), AppJsonSerializerContext.Default.ListDownloadTask));
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

            // Url 为 null 时下游 InputResolver 会空引用并被记成 500，先在此判空返回 400
            if (string.IsNullOrWhiteSpace(bindingResult.Result!.Url))
            {
                return Results.BadRequest("url 不能为空");
            }

            // mode=enqueue 仅入暂停表（待 start）；未指定 mode 或 mode=execute 时受理即执行
            var mode = http.Request.Query["mode"].ToString( ) == "enqueue" ? SubmitMode.Enqueue : SubmitMode.Execute;
            try
            {
                var result = await store.EnqueueAsync(bindingResult.Result!, mode, token);
                if (result.QueueFull)
                {
                    http.Response.Headers.RetryAfter = QueueFullRetryAfter.ToString(CultureInfo.InvariantCulture);
                    return Results.StatusCode(StatusCodes.Status429TooManyRequests);
                }

                var task = result.Task!;
                // 重复提交同资源：命中已有任务，返回 200；新受理返回 202 + 任务位置
                if (result.Duplicate)
                {
                    return Results.Json(task, AppJsonSerializerContext.Default.DownloadTask);
                }

                // Results.Accepted 没有 JsonTypeInfo 重载，走非泛型重载会依赖运行时反射解析器；
                // 这里自行写 Location 头后交给 Results.Json，保持源生成序列化
                http.Response.Headers.Location = $"/api/v1/tasks/{task.Id}";
                return Results.Json(task, AppJsonSerializerContext.Default.DownloadTask, statusCode: StatusCodes.Status202Accepted);
            }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException)
            {
                // URL 无法识别或资源不可解析（短链展开失败 / 页面缺 epList 等），受理前返回 400
                return Results.BadRequest("输入有误");
            }
        }).RequireRateLimiting("taskSubmit");

        // 变更类端点必须用 POST/DELETE，不能暴露为 GET，否则与本就全开的 CORS 叠加形成 CSRF
        tasks.MapPost("/{id}/start", (string id, TaskStore store, HttpContext http) =>
        {
            if (!ResourceId.TryParse(id, out var rid))
            {
                return Results.NotFound( );
            }

            var started = store.Start(rid);
            if (started == StartResult.Started)
            {
                return Results.Ok( );
            }

            if (started == StartResult.QueueFull)
            {
                http.Response.Headers.RetryAfter = QueueFullRetryAfter.ToString(CultureInfo.InvariantCulture);
                return Results.StatusCode(StatusCodes.Status429TooManyRequests);
            }

            // 已在运行 / 已结束与「不存在」含义不同：前者是 409（重复的启动请求），后者才是 404
            return started == StartResult.AlreadyStarted
                ? Results.StatusCode(StatusCodes.Status409Conflict)
                : Results.NotFound( );
        });
        // 变更类端点必须用 POST/DELETE，不能暴露为 GET，否则与本就全开的 CORS 叠加形成 CSRF
        tasks.MapPost("/{id}/stop", (string id, TaskStore store) =>
        {
            if (!ResourceId.TryParse(id, out var rid))
            {
                return Results.NotFound( );
            }

            // 直播任务的「停止」＝停止录制并合并（与 GUI 停止录制按钮一致）：先请求录制端停录
            // 未在录制（排队中 / 尚未开录）时退化为整任务取消
            if (rid is ResourceId.LiveRoom && LiveSignal.TryRequestStop(ResourceIdJsonConverter.Format(rid)))
            {
                return Results.Ok( );
            }

            return store.Stop(rid) switch
            {
                StopResult.NotFound => Results.NotFound( ),
                _ => Results.Ok( )
            };
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
            // 规范 id 解析失败视为不存在，仍返回 200
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
                // 浏览器把令牌放在子协议头里，握手应答须原样回显客户端请求的首个子协议，否则浏览器判定不匹配并断开
                using var socket = await context.WebSockets.AcceptWebSocketAsync(FirstSubProtocol(context.Request));
                await hub.HandleAsync(socket, context.RequestAborted);
            }
            finally
            {
                hub.Leave(ip);
            }
        });

        // 健康检查：匿名放行（探活不要求令牌）；计数排除 enqueue 暂停态（Pending 尚未进入执行队列，不计入运行中）
        // 事件流（WebSocket /hubs/tasks）始终启用，无需开关字段；任务状态经 WS 推送感知，无轮询端点
        app.MapGet("/healthz", (TaskStore store) =>
                Results.Json(new HealthStatus("ok", store.RunningCount( )), AppJsonSerializerContext.Default.HealthStatus))
            .AllowAnonymous( );
    }

    // 握手应答回显客户端请求的首个子协议（浏览器把令牌放在此处）；未请求子协议时返回 null
    private static string? FirstSubProtocol(HttpRequest request)
    {
        return request.Headers.TryGetValue("Sec-WebSocket-Protocol", out var offered)
            ? offered.ToString( ).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).FirstOrDefault( )
            : null;
    }
}
