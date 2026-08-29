using System.Collections.Generic;
using System.Collections.ObjectModel;
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

    // 单任务取消源：与进程级 AppEnv.CancellationToken（关停）Link，故关停会取消全部、单独 Cancel 只影响本任务。
    // 不入 JSON（Cancel 单任务用不到序列化后的对象，且 CancellationTokenSource 无法序列化）。
    [JsonIgnore]
    public CancellationTokenSource Cts { get; } = CancellationTokenSource.CreateLinkedTokenSource(AppEnv.CancellationToken);

    // SavePaths 由工作线程在保存文件时写入，HTTP 线程在序列化时读取，须加锁并快照，避免枚举被并发修改
    private readonly Lock savePathsGate = new( );
    private readonly List<string> savePaths = [ ];
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
