namespace BBDown.Core.Download;

/// <summary>
/// 当前运行解析出的外部工具路径不可变快照
/// 由 WorkSetup.ResolveToolPaths 一次性解析，作为显式参数向下透传
/// 避免 serve 并发任务写共享状态互相踩踏
/// </summary>
public readonly record struct ToolPaths(string Ffmpeg, string Mp4box, string? Aria2c);
