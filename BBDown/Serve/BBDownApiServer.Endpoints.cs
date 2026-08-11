using BBDown.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace BBDown.Serve;

public partial class BBDownApiServer
{
    // 端点注册：鉴权中间件 + 任务增删查路由。与 SetUpServer 同属一个 partial 类，
    // 可直接访问 authRequired / TokenMatches / runningTasks / finishedTasks / Snapshot / RunTaskAndCallBackAsync 等成员。
    private void MapServeEndpoints(WebApplication app)
    {
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
            // 路径参数为规范 id（如 av170001、season2539），解析失败视为不存在
            if (!ResourceId.TryParse(id, out var rid)
                || (!runningTasks.TryGetValue(rid, out var task) && !finishedTasks.TryGetValue(rid, out task)))
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
            foreach (var (id, t) in finishedTasks)
            {
                if (!t.IsSuccessful)
                {
                    finishedTasks.TryRemove(id, out _);
                }
            }

            return Results.Ok( );
        });
        finishedRemovalApi.MapPost("/{id}", (string id) =>
        {
            // 规范 id 解析失败视为不存在，仍返回 200（与旧行为一致：无论是否找到都 200）
            if (ResourceId.TryParse(id, out var rid))
            {
                finishedTasks.TryRemove(rid, out _);
            }

            return Results.Ok( );
        });
        // 变更类端点必须用 POST（同上 CSRF 考量）。单独取消某运行中的任务，不影响其他任务；
        // 取消经 task.Cts 触发，与进程级关停令牌 Link，故 Ctrl+C 仍会整体退出。
        app.MapPost("/stop-task/{id}", (string id) =>
        {
            if (!ResourceId.TryParse(id, out var rid) || !runningTasks.TryGetValue(rid, out var task))
            {
                return Results.NotFound( );
            }

            task.Cts.Cancel( );
            return Results.Ok( );
        });
    }
}
