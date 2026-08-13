using System;

namespace BBDown.Core.Download;

public sealed class DownloadConfig
{
    public bool UseAria2c { get; set; }
    public string Aria2cArgs { get; set; } = string.Empty;
    public bool NoForceHttp { get; set; }
    public bool SingleThread { get; set; }
    // aria2c 可执行文件路径（来自 ToolPaths 快照），避免用进程级可变静态字段
    public string? Aria2cPath { get; set; }
    // 进度采样回调（ratio, bytesDelta）。下载层不认识 serve 的任务模型，只回吐数字
    public Action<double, long>? OnSample { get; set; }
    public string Cookie { get; set; } = string.Empty;
    // 多线程分片大小（字节）
    public long ChunkSize { get; set; } = PartFile.DefaultChunkSize;
}
