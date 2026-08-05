using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace BBDown;

[JsonConverter(typeof(JsonStringEnumConverter<DownloadStatus>))]
public enum DownloadStatus
{
    Queued,   // 已受理、等待并发额度（仅 --max-concurrent > 0 时出现）
    Running,  // 下载中
    Finished, // 已结束，成败见 IsSuccessful
}

public record DownloadTask(string Aid, string Url, long TaskCreateTime)
{
    public string? Title { get; set; }
    public string? Pic { get; set; }
    public long? VideoPubTime { get; set; }
    public long? TaskFinishTime { get; set; }
    public double Progress { get; set; }
    public double DownloadSpeed { get; set; }
    public double TotalDownloadedBytes { get; set; }
    public bool IsSuccessful { get; set; }
    public DownloadStatus Status { get; set; }

    public Collection<string> SavePaths { get; } = [];

    /// <summary>进度条的采样回调：<paramref name="bytesDelta"/> 是本采样周期新增的字节数。</summary>
    public void ApplySample(double ratio, long bytesDelta)
    {
        Progress = ratio;
        // 一个周期一个字节都没到（卡住或已下完）时保留上一次的速度，不要显示成 0
        if (bytesDelta <= 0)
        {
            return;
        }

        DownloadSpeed = bytesDelta;
        TotalDownloadedBytes += bytesDelta;
    }
}

public record DownloadTaskSnapshot(IReadOnlyList<DownloadTask> Running, IReadOnlyList<DownloadTask> Finished);
