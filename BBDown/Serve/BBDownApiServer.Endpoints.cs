using System.Threading.Tasks;

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
}
