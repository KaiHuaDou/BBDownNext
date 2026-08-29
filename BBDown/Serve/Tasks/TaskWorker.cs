using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core;
using BBDown.Core.Download;
using BBDown.Core.Live;
using BBDown.Core.Logging;
using BBDown.Core.Pipeline;
using BBDown.Core.Workflow;

using Microsoft.Extensions.Hosting;

namespace BBDown.Serve.Tasks;

/// <summary>
/// 后台任务消费者：从 TaskQueue 取已受理任务，经并发闸门后执行下载，收尾回写 TaskStore 并触发回调。
/// </summary>
internal sealed partial class TaskWorker : BackgroundService
{
    private readonly TaskQueue queue;
    private readonly TaskStore store;
    private readonly SemaphoreSlim? gate;   // null = 不限制（历史行为）
    // scope（task.Id 的 record ToString）→ 任务：进度样本按字符串匹配回写，不经 ResourceId 解析
    private readonly ConcurrentDictionary<string, DownloadTask> byScope = new( );

    public TaskWorker(TaskQueue queue, TaskStore store, int maxConcurrent)
    {
        this.queue = queue;
        this.store = store;
        // <=0 一律视为不限制：不建闸门，行为与旧版一致；>0 时仅限制同时下载的任务数，
        // 多余任务排队，单个任务内部的下载并行度交给多线程下载器自行决定（不再压到 1）
        if (maxConcurrent > 0)
        {
            gate = new SemaphoreSlim(maxConcurrent, maxConcurrent);
        }

        // 进度样本统一经 ProgressBus：任务执行期间（BeginScope）的样本回写 DownloadTask（/get-tasks 契约）
        ProgressBus.Subscribe(OnProgress);
    }

    // 阶段内样本回写任务进度字段；阶段边界事件由桥接器（interactive）送事件流
    private void OnProgress(WorkflowEvent evt)
    {
        if (evt is not ProgressSampleEvent sample)
        {
            return;
        }

        if (sample.Scope is { } scope && byScope.TryGetValue(scope, out var task))
        {
            task.Progress = sample.Ratio;
            // 一个周期一个字节都没到（卡住或已下完）时保留上一次的速度，不要显示成 0
            if (sample.Speed > 0)
            {
                task.DownloadSpeed = sample.Speed;
            }

            task.TotalDownloadedBytes = sample.TotalBytes;
        }
    }

    // 消费循环不等待单个任务完成：每取一个任务即启动执行，并发由 RunGatedAsync 的闸门限制
    // （不设闸门时全部并发，设 N 时最多 N 个同时在跑）；队列有界（100）已提供背压。
    // 列表仅做周期清理、不持有完成信号；队列关闭（关服）时等待全部在途任务收尾
    // （单任务异常已在 RunTaskAsync 内兜底，Task.WhenAll 不会因任务失败而抛出）。
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pending = new List<Task>( );
        try
        {
            await foreach (var envelope in queue.Reader.ReadAllAsync(stoppingToken))
            {
                pending.Add(RunTaskAsync(envelope));
                if (pending.Count >= PendingDrainBatch)
                {
                    pending.RemoveAll(t => t.IsCompleted);
                }
            }
        }
        finally
        {
            await Task.WhenAll(pending);
        }
    }

    // 在途任务列表的裁剪批次：避免列表无限增长（队列上限 100，批内完成即清）
    private const int PendingDrainBatch = 8;

    public override void Dispose( )
    {
        gate?.Dispose( );
        base.Dispose( );
    }

    // 任务级并发闸门：未限流时直接执行；限流时先排队取额度（期间 Status=Queued），
    // 取到后转 Running，无论成败都在 finally 归还额度（不占线程、不持锁）
    internal async Task RunGatedAsync(DownloadTask task, Func<Task> download, CancellationToken token)
    {
        if (gate is null)
        {
            task.Status = DownloadStatus.Running;
            await download( );
            return;
        }

        await gate.WaitAsync(token);
        task.Status = DownloadStatus.Running;
        try
        {
            await download( );
        }
        finally
        {
            gate.Release( );
        }
    }

    private async Task RunTaskAsync(TaskEnvelope envelope)
    {
        var task = envelope.Task;
        // 消息作用域：下载期间 Logger 的业务消息经 MessageBus 携带本任务 id（ResourceIdJsonConverter.Format 规范串，
        // 与 /get-tasks 返回的 id 形态一致，桥接器 / 进度回写据此字符串匹配），收尾后退出作用域
        var scope = ResourceIdJsonConverter.Format(task.Id);
        byScope[scope] = task;
        using (MessageBus.BeginScope(scope))
        {
            try
            {
                await RunGatedAsync(task, ( ) => RunDownloadAsync(task, envelope), task.Cts.Token);
                task.IsSuccessful = true;
            }
            catch (OperationCanceledException) when (task.Cts.IsCancellationRequested)
            {
                // 关服（Ctrl+C）或单独停止任务都会取消 task.Cts：前者走进程级令牌，后者走停止端点。
                // 排队中的任务会在闸门处被取消，属正常退出路径，不该刷成"下载失败"
                // ErrorMessage 供客户端区分「已取消」与真实失败（前端据此显示任务状态）
                task.ErrorMessage = "任务已取消";
                if (AppEnv.CancellationToken.IsCancellationRequested)
                {
                    Logger.LogWarn($"{task.Id} 已取消（服务器正在退出）");
                }
                else
                {
                    Logger.LogWarn($"{task.Id} 已取消（任务被单独停止）");
                }
            }
            catch (Exception e)
            {
                // 走 Logger 才有全局锁，serve 模式并发任务直接写 Console 会互相插字（P1-17）；
                // 错误消息经路径脱敏后写入任务契约，客户端经 /get-tasks 或事件流可读
                var msg = RedactPaths(Config.DebugLog ? e.ToString( ) : e.Message);
                task.ErrorMessage = msg;
                Logger.LogError($"{task.Id} 下载失败：{msg}");
            }
        }

        byScope.TryRemove(scope, out _);

        task.Status = DownloadStatus.Finished;
        task.TaskFinishTime = DateTimeOffset.Now.ToUnixTimeMilliseconds( );
        if (task.IsSuccessful)
        {
            task.Progress = 1f;
            var elapsedMs = task.TaskFinishTime.Value - task.TaskCreateTime;
            task.DownloadSpeed = elapsedMs > 0 ? task.TotalDownloadedBytes * 1000 / elapsedMs : 0;
        }

        store.MoveToFinished(task);
        // 任务已结束，释放与进程级令牌的链接注册（不取消任何下载，仅释放资源）
        task.Cts.Dispose( );
        store.ReleaseContext(ResourceIdJsonConverter.Format(task.Id));
        await NotifyCallbackAsync(task, envelope.CallBackWebHook);
    }

    // 错误消息路径脱敏：替换绝对路径，避免 /get-tasks 或事件流泄露本机目录结构（improvement-review 草案 C）
    [GeneratedRegex(@"(?<![/:\w])([A-Za-z]:\\|/)([\w./\\-]+)")]
    private static partial Regex AbsolutePathRegex( );

    private static string RedactPaths(string text)
    {
        return AbsolutePathRegex( ).Replace(text, "<redacted-path>");
    }

    // 由资源类型推导内容适用域：专栏 / 直播模式需要按模式提示不生效的内容标志（与 CLI 的 ContentMode 映射一致）
    private static ContentMode ContentModeOf(ResourceId id)
    {
        return id switch
        {
            ResourceId.LiveRoom => ContentMode.Live,
            ResourceId.OpusArticle => ContentMode.Opus,
            _ => ContentMode.Video,
        };
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
            task.AddSavePath);
    }

    // 按任务类型分发执行：直播 / 专栏走独立链路（不经 DownloadPipeline），消息 / 进度仍经总线 +
    // scope（record ToString）路由进事件流，与普通下载一致；LiveTarget 由受理时解析出的房间号直接构造，
    // 不重解析 URL（原始 URL 可能是 b23 短链，LiveInputResolver 认不出）
    private async Task RunDownloadAsync(DownloadTask task, TaskEnvelope envelope)
    {
        var token = task.Cts.Token;
        // 与 CLI 一致：专栏 / 直播模式下输出非活跃内容标志的调试提示（视频模式不提示，避免误导）
        var mode = ContentModeOf(task.Id);
        if (mode != ContentMode.Video)
        {
            foreach (var warn in ContentSelector.DescribeInactive(envelope.Request.Content, mode))
            {
                Logger.LogDebug(warn);
            }
        }

        switch (task.Id)
        {
            case ResourceId.LiveRoom room:
                await LiveDownload.RunAsync(envelope.Request, new LiveTarget(room.RoomId.ToString( )), ResourceIdJsonConverter.Format(task.Id), SinkFor(task), token);
                break;
            case ResourceId.OpusArticle:
                await OpusDownload.RunAsync(envelope.Request, SinkFor(task), token);
                break;
            default:
                await DownloadPipeline.RunAsync(envelope.Request, SinkFor(task), store.GetContext(ResourceIdJsonConverter.Format(task.Id)), token);
                break;
        }
    }

    private static async Task NotifyCallbackAsync(DownloadTask task, string? callBackWebHook)
    {
        if (string.IsNullOrEmpty(callBackWebHook))
        {
            return;
        }

        if (!Uri.TryCreate(callBackWebHook, UriKind.Absolute, out var hookUri) || !SsrfGuard.IsSafeWebHook(hookUri))
        {
            Logger.LogWarn("忽略不安全的 CallBackWebHook（仅允许公网 http/https，拒绝内网/回环地址）");
            return;
        }

        var jsonContent = JsonSerializer.Serialize(task, AppJsonSerializerContext.Default.DownloadTask);
        // 有界重试：瞬态网络失败退避重试，通知语义下最后一次失败仅记日志不抛
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
                // 走专用 WebHookClient：关重定向 + 连接前二次校验私网（§2.3），不使用共享的 AppHttpClient
                using var response = await SsrfGuard.WebHookClient.PostAsync(hookUri, content, AppEnv.CancellationToken);
                return;
            }
            catch (Exception e) when (attempt < MaxCallbackAttempts)
            {
                Logger.LogDebug("回调失败（第 {0} 次）：{1}", attempt, e.Message);
                await Task.Delay(TimeSpan.FromSeconds(2) * attempt, AppEnv.CancellationToken);
            }
            catch (Exception e)
            {
                Logger.LogDebug("回调失败：{0}", e.Message);
                return;
            }
        }
    }

    private const int MaxCallbackAttempts = 3;
}
