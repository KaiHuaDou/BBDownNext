using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Auth;
using BBDown.Core;
using BBDown.Pipeline;

namespace BBDown.Serve;

public partial class BBDownApiServer
{
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
            await RunGatedAsync(task, ( ) => DownloadPipeline.RunAsync(option, task, AppEnv.CancellationToken), AppEnv.CancellationToken);
            task.IsSuccessful = true;
        }
        catch (OperationCanceledException) when (AppEnv.CancellationToken.IsCancellationRequested)
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
    internal DownloadTask CreateTask(string aid, string url)
    {
        return new(aid, url, DateTimeOffset.Now.ToUnixTimeMilliseconds( ))
        {
            // 未限流时不存在排队阶段，直接标 Running，避免 /get-tasks 出现假 Queued
            Status = taskGate is null ? DownloadStatus.Running : DownloadStatus.Queued,
            MaxChunkParallelism = maxChunkParallelism,
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
