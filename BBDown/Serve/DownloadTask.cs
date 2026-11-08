using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Serialization;
using System.Threading;

using BBDown.Core;

namespace BBDown.Serve;

[JsonConverter(typeof(JsonStringEnumConverter<DownloadStatus>))]
internal enum DownloadStatus
{
    Pending,  // 已受理、等待手动 start（enqueue 提交，不自动执行）
    Queued,   // 已提交执行、等待并发额度（仅 --max-concurrent > 0 时出现）
    Running,
    Finished, // 已结束，成败见 IsSuccessful
}

internal record DownloadTask(ResourceId Id, string Url, long TaskCreateTime)
{
    private const int StatusMask = 0x1F;
    private const int SuccessBit = 1 << 30;
    private const int CancelledBit = 1 << 29;

    // 阶段与结局同存一个原子整数：单次读即可拿到一致的一组标志，收尾经 Finish 一次写入
    private int state;

    public DownloadTask(ResourceId id, string url, long taskCreateTime, DownloadStatus initial) : this(id, url, taskCreateTime)
    {
        state = (int) initial;
    }

    public string? Title { get; set; }
    public string? Pic { get; set; }
    public long? VideoPubTime { get; set; }
    public long? TaskFinishTime { get; set; }
    public double Progress
    {
        get => Volatile.Read(ref field);
        set => Volatile.Write(ref field, value);
    }

    public double DownloadSpeed
    {
        get => Volatile.Read(ref field);
        set => Volatile.Write(ref field, value);
    }

    /// <summary>失败原因（路径已脱敏）；成功或未失败为 null。</summary>
    public string? ErrorMessage { get; set; }

    // 进度字段由 TaskWorker 订阅 ProgressBus 更新（原子读写，多线程采样安全）

    public long TotalDownloadedBytes
    {
        get => Interlocked.Read(ref field);
        set => Interlocked.Exchange(ref field, value);
    }

    public DownloadStatus Status => (DownloadStatus) (Volatile.Read(ref state) & StatusMask);

    /// <summary>任务是否被取消（用户停止 / 服务器退出）；与真实失败区分，供客户端直接判定而不必解析文案。</summary>
    public bool IsCancelled => (Volatile.Read(ref state) & CancelledBit) != 0;

    public bool IsSuccessful => (Volatile.Read(ref state) & SuccessBit) != 0;

    /// <summary>
    /// 推进阶段。只接受 Pending→Queued、Queued→Pending（执行队列写满回滚）与 Queued→Running；
    /// 其余组合说明调用方顺序有误，直接抛出而不是留下一个说不清来源的状态
    /// </summary>
    public void MoveTo(DownloadStatus next)
    {
        while (true)
        {
            var current = Volatile.Read(ref state);
            var from = (DownloadStatus) (current & StatusMask);
            if (!CanMove(from, next))
            {
                throw new InvalidOperationException($"{Id} 不能从 {from} 转到 {next}");
            }

            if (Interlocked.CompareExchange(ref state, (current & ~StatusMask) | (int) next, current) == current)
            {
                return;
            }
        }
    }

    /// <summary>收尾：一次写入结束状态、成败与取消标记，任务结束判定面（查询 / 归档 / 回调）总会看到一致的一组。</summary>
    public void Finish(bool isSuccessful, bool isCancelled = false)
    {
        var current = Volatile.Read(ref state);
        var status = (DownloadStatus) (current & StatusMask);
        if (status is not (DownloadStatus.Queued or DownloadStatus.Running))
        {
            throw new InvalidOperationException($"{Id} 处于 {status}，不能收尾");
        }

        Volatile.Write(ref state, (int) DownloadStatus.Finished | (isSuccessful ? SuccessBit : 0) | (isCancelled ? CancelledBit : 0));
    }

    private static bool CanMove(DownloadStatus from, DownloadStatus to)
    {
        return (from, to) switch
        {
            (DownloadStatus.Pending, DownloadStatus.Queued) => true,
            (DownloadStatus.Queued, DownloadStatus.Pending) => true,
            (DownloadStatus.Queued, DownloadStatus.Running) => true,
            _ => false,
        };
    }

    /// <summary>任务作用域（ResourceId 规范串）：总线消息路由与事件流订阅的匹配键，随构造一次定型，替代各处重复 Format。</summary>
    [JsonIgnore]
    public string Scope { get; } = ResourceIdJsonConverter.Format(Id);

    // 单任务取消源：与进程级 AppEnv.CancellationToken（关停）Link，故关停会取消全部、单独 Cancel 只影响本任务
    // 不入 JSON（Cancel 单任务用不到序列化后的对象，且 CancellationTokenSource 无法序列化）
    [JsonIgnore]
    public CancellationTokenSource Cts { get; } = CancellationTokenSource.CreateLinkedTokenSource(AppEnv.CancellationToken);

    // 取消与释放的竞态防护：HTTP 停止端点（Cancel）与执行线程收尾（Dispose）并发触达 Cts
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
    // 只存文件名：任务帧经 WebSocket 与回调外发，绝对路径会泄露本机目录布局
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
            savePaths.Add(Path.GetFileName(path));
        }
    }
}

internal record DownloadTaskSnapshot(IReadOnlyList<DownloadTask> Running, IReadOnlyList<DownloadTask> Finished);
