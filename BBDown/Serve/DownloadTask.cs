using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading;

using BBDown.Core;

namespace BBDown.Serve;

[JsonConverter(typeof(JsonStringEnumConverter<DownloadStatus>))]
public enum DownloadStatus
{
    Pending,  // 已受理、等待手动 start（enqueue 提交，不自动执行）
    Queued,   // 已提交执行、等待并发额度（仅 --max-concurrent > 0 时出现）
    Running,  // 下载中
    Finished, // 已结束，成败见 IsSuccessful
}

public record DownloadTask(ResourceId Id, string Url, long TaskCreateTime)
{
    public string? Title { get; set; }
    public string? Pic { get; set; }
    public long? VideoPubTime { get; set; }
    public long? TaskFinishTime { get; set; }
    public double Progress
    {
        get => Volatile.Read(ref progress);
        set => Volatile.Write(ref progress, value);
    }

    public double DownloadSpeed
    {
        get => Volatile.Read(ref downloadSpeed);
        set => Volatile.Write(ref downloadSpeed, value);
    }

    /// <summary>失败原因（路径已脱敏）；成功或未失败为 null。</summary>
    public string? ErrorMessage { get; set; }
    /// <summary>任务是否被取消（用户停止 / 服务器退出）；与真实失败区分，供客户端直接判定而不必解析文案。</summary>
    public bool IsCancelled { get; set; }
    // 进度字段由 TaskWorker 订阅 ProgressBus 更新（原子读写，多线程采样安全）
    private double progress;
    private double downloadSpeed;
    private long totalBytes;
    public long TotalDownloadedBytes
    {
        get => Interlocked.Read(ref totalBytes);
        set => Interlocked.Exchange(ref totalBytes, value);
    }
    public bool IsSuccessful { get; set; }
    public DownloadStatus Status { get; set; }

    /// <summary>任务作用域（ResourceId 规范串）：总线消息路由与事件流订阅的匹配键，随构造一次定型，替代各处重复 Format。</summary>
    [JsonIgnore]
    public string Scope { get; } = ResourceIdJsonConverter.Format(Id);

    // 单任务取消源：与进程级 AppEnv.CancellationToken（关停）Link，故关停会取消全部、单独 Cancel 只影响本任务。
    // 不入 JSON（Cancel 单任务用不到序列化后的对象，且 CancellationTokenSource 无法序列化）。
    [JsonIgnore]
    public CancellationTokenSource Cts { get; } = CancellationTokenSource.CreateLinkedTokenSource(AppEnv.CancellationToken);

    // 取消与释放的竞态防护：HTTP 停止端点（Cancel）与执行线程收尾（Dispose）并发触达 Cts，
    // 先 Dispose 后 Cancel 会抛 ObjectDisposedException，统一经同一把锁串行化
    private readonly Lock ctsGate = new( );
    private bool ctsDisposed;

    /// <summary>请求取消本任务；已收尾（取消源已释放）时为无害空操作。</summary>
    public void Cancel( )
    {
        lock (ctsGate)
        {
            if (!ctsDisposed)
            {
                Cts.Cancel( );
            }
        }
    }

    /// <summary>释放取消源；收尾与受理回滚路径调用，重复释放为空操作。</summary>
    public void DisposeCts( )
    {
        lock (ctsGate)
        {
            if (ctsDisposed)
            {
                return;
            }

            ctsDisposed = true;
            Cts.Dispose( );
        }
    }

    // SavePaths 由工作线程在保存文件时写入，HTTP 线程在序列化时读取，须加锁并快照，避免枚举被并发修改
    private readonly Lock savePathsGate = new( );
    private readonly List<string> savePaths = [];
    public IReadOnlyList<string> SavePaths
    {
        get
        {
            lock (savePathsGate)
            {
                return [.. savePaths];
            }
        }
    }

    public void AddSavePath(string path)
    {
        lock (savePathsGate)
        {
            savePaths.Add(path);
        }
    }
}

public record DownloadTaskSnapshot(IReadOnlyList<DownloadTask> Running, IReadOnlyList<DownloadTask> Finished);
