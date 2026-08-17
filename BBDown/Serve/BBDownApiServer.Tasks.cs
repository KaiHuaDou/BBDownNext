#pragma warning disable CA1001

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core;
using BBDown.Core.Download;
using BBDown.Core.Pipeline;

namespace BBDown.Serve;

public partial class BBDownApiServer
{
    private static List<DownloadTask> Snapshot(ConcurrentDictionary<ResourceId, DownloadTask> tasks)
    {
        return [.. tasks.Values];
    }

    // 请求线程不等待下载完成，因此这里必须自己兜住所有异常，否则会变成 UnobservedTaskException
    private async Task RunTaskAndCallBackAsync(ServeRequestOptions req)
    {
        DownloadTask? downloadTask;
        try
        {
            downloadTask = await AddDownloadTaskAsync(req.ToDownloadRequest( ));
        }
        catch (Exception e)
        {
            Logger.LogError($"任务创建失败：{e.Message}");
            return;
        }

        if (string.IsNullOrEmpty(req.CallBackWebHook))
        {
            return;
        }

        if (!Uri.TryCreate(req.CallBackWebHook, UriKind.Absolute, out var hookUri) || !SsrfGuard.IsSafeWebHook(hookUri))
        {
            Logger.LogWarn("忽略不安全的 CallBackWebHook（仅允许公网 http/https，拒绝内网/回环地址）");
            return;
        }

        try
        {
            var jsonContent = JsonSerializer.Serialize(downloadTask, AppJsonSerializerContext.Default.DownloadTask);
            using var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
            // 走专用 WebHookClient：关重定向 + 连接前二次校验私网（§2.3），不使用共享的 AppHttpClient
            using var response = await SsrfGuard.WebHookClient.PostAsync(hookUri, content, AppEnv.CancellationToken);
        }
        catch (Exception e)
        {
            Logger.LogDebug("回调失败：{0}", e.Message);
        }
    }

    private async Task<DownloadTask> AddDownloadTaskAsync(DownloadRequest option)
    {
        option = ApplyServeWorkDir(option);
        option = ApplyServeHost(option);

        var cfg = WorkSetup.ResolveConfig(option, option.Api);
        // 解析阶段尚无任务级令牌（任务在解析成功后创建），用进程级令牌：服务器关停即可中断排队中的解析
        var id = await InputResolver.ResolveIdAsync(option.Url, cfg, AppEnv.CancellationToken);
        // 任务去重键：ResourceId 值相等，同资源必命中同键，无需字符串形态
        var task = CreateTask(id, option.Url);
        var claimed = runningTasks.GetOrAdd(id, task);
        if (!ReferenceEquals(claimed, task))
        {
            return claimed;
        }

        try
        {
            await RunGatedAsync(task, ( ) => DownloadPipeline.RunAsync(option, SinkFor(task), task.Cts.Token), task.Cts.Token);
            task.IsSuccessful = true;
        }
        catch (OperationCanceledException) when (task.Cts.IsCancellationRequested)
        {
            // 关服（Ctrl+C）或单独停止任务都会取消 task.Cts：前者走进程级令牌，后者走 /stop-task 端点。
            // 排队中的任务会在闸门处被取消，属正常退出路径，不该刷成"下载失败"
            if (AppEnv.CancellationToken.IsCancellationRequested)
            {
                Logger.LogWarn($"{id} 已取消（服务器正在退出）");
            }
            else
            {
                Logger.LogWarn($"{id} 已取消（任务被单独停止）");
            }
        }
        catch (Exception e)
        {
            // 走 Logger 才有全局锁，serve 模式并发任务直接写 Console 会互相插字（P1-17）
            var msg = Config.DebugLog ? e.ToString( ) : e.Message;
            Logger.LogError($"{id} 下载失败：{msg}");
        }

        task.Status = DownloadStatus.Finished;
        task.TaskFinishTime = DateTimeOffset.Now.ToUnixTimeMilliseconds( );
        if (task.IsSuccessful)
        {
            task.Progress = 1f;
            var elapsedMs = task.TaskFinishTime.Value - task.TaskCreateTime;
            task.DownloadSpeed = elapsedMs > 0 ? task.TotalDownloadedBytes * 1000 / elapsedMs : 0;
        }

        runningTasks.TryRemove(id, out _);
        finishedTasks[id] = task;
        TrimFinishedTasks( );
        // 任务已结束，释放与进程级令牌的链接注册（不取消任何下载，仅释放资源）
        task.Cts.Dispose( );
        return task;
    }

    // 把可变的任务对象收束在 serve 内部：下载链路只拿到回调，不持有 DownloadTask 引用
    internal static PipelineSink SinkFor(DownloadTask task)
    {
        return new PipelineSink(
            v =>
            {
                task.Title = v.Title;
                task.Pic = v.Pic;
                task.VideoPubTime = v.PubTime;
            },
            task.SavePaths.Add,
            task.ApplySample,
            null);
    }

    // 任务的初始状态（是否排队）由服务端限流闸门决定，抽成方法便于单测观测
    internal DownloadTask CreateTask(ResourceId id, string url)
    {
        return new(id, url, DateTimeOffset.Now.ToUnixTimeMilliseconds( ))
        {
            // 未限流时不存在排队阶段，直接标 Running，避免 /get-tasks 出现假 Queued
            Status = taskGate is null ? DownloadStatus.Running : DownloadStatus.Queued,
        };
    }

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
    internal DownloadRequest ApplyServeWorkDir(DownloadRequest option)
    {
        if (!string.IsNullOrEmpty(serveWorkDir))
        {
            return option with { WorkDir = serveWorkDir };
        }

        return option;
    }

    // serve 模式的 API host 由启动参数（--host/--ep-host/--tv-host）决定，覆盖请求体（请求体已不含该字段），
    // 客户端无法把请求导向自己控制的服务器、从而窃走操作者的 SESSDATA（P0-1）。空值回落官方默认 host。
    internal DownloadRequest ApplyServeHost(DownloadRequest option)
    {
        return option with
        {
            Host = string.IsNullOrWhiteSpace(serveHost) ? BiliApi.MainHost : serveHost.Trim( ),
            EpHost = string.IsNullOrWhiteSpace(serveEpHost) ? BiliApi.MainHost : serveEpHost.Trim( ),
            TvHost = string.IsNullOrWhiteSpace(serveTvHost) ? BiliApi.TvHost : serveTvHost.Trim( ),
        };
    }

    // 已完成任务无上限增长会造成内存泄漏，超过阈值后按完成时间淘汰最旧的（P1-18）
    private const int MaxFinishedTasks = 200;

    private void TrimFinishedTasks( )
    {
        if (finishedTasks.Count <= MaxFinishedTasks)
        {
            return;
        }

        // 一次排序淘汰最旧的一批，避免循环内反复 OrderBy 造成 O(n²)
        foreach (var (id, oldest) in finishedTasks.OrderBy(kv => kv.Value.TaskFinishTime).Take(finishedTasks.Count - MaxFinishedTasks))
        {
            finishedTasks.TryRemove(id, out _);
        }
    }
}
