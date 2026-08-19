namespace BBDown.Core.Download;

public sealed class DownloadConfig
{
    public bool UseAria2c { get; set; }
    public string Aria2cArgs { get; set; } = string.Empty;
    public bool NoForceHttp { get; set; }
    public bool SingleThread { get; set; }
    // aria2c 可执行文件路径（来自 ToolPaths 快照），避免用进程级可变静态字段
    public string? Aria2cPath { get; set; }
    public string Cookie { get; set; } = string.Empty;
    // downloader 并行连接数；FLV 多片段并行时由调用方下调（片段间并行 × 片段内并行合计不超过上限）
    public int ParallelCount { get; set; } = DownloaderAdapter.MaxRangeConcurrency;
}
