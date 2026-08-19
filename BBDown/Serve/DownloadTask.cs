using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using System.Threading;

using BBDown.Core;

namespace BBDown.Serve;

[JsonConverter(typeof(JsonStringEnumConverter<DownloadStatus>))]
public enum DownloadStatus
{
    Queued,   // 已受理、等待并发额度（仅 --max-concurrent > 0 时出现）
    Running,  // 下载中
    Finished, // 已结束，成败见 IsSuccessful
}

public record DownloadTask(ResourceId Id, string Url, long TaskCreateTime)
{
    public string? Title { get; set; }
    public string? Pic { get; set; }
    public long? VideoPubTime { get; set; }
    public long? TaskFinishTime { get; set; }
    public double Progress { get; set; }
    public double DownloadSpeed { get; set; }
    /// <summary>失败原因（路径已脱敏）；成功或未失败为 null。</summary>
    public string? ErrorMessage { get; set; }
    // 进度字段由 TaskWorker 订阅 ProgressBus 更新（Interlocked 原子读写，多线程采样安全）
    private long totalBytes;
    public long TotalDownloadedBytes
    {
        get => Interlocked.Read(ref totalBytes);
        set => Interlocked.Exchange(ref totalBytes, value);
    }
    public bool IsSuccessful { get; set; }
    public DownloadStatus Status { get; set; }

    // 单任务取消源：与进程级 AppEnv.CancellationToken（关停）Link，故关停会取消全部、单独 Cancel 只影响本任务。
    // 不入 JSON（Cancel 单任务用不到序列化后的对象，且 CancellationTokenSource 无法序列化）。
    [JsonIgnore]
    public CancellationTokenSource Cts { get; } = CancellationTokenSource.CreateLinkedTokenSource(AppEnv.CancellationToken);

    public Collection<string> SavePaths { get; } = [];
}

public record DownloadTaskSnapshot(IReadOnlyList<DownloadTask> Running, IReadOnlyList<DownloadTask> Finished);
