namespace BBDown;

/// <summary>
/// 本次运行解析出的外部工具路径不可变快照。
/// 取代原先 Muxer.ffmpeg/mp4box、BBDownAria2c.aria2c 等进程级可变静态字段，
/// 避免 serve 并发任务互相踩踏（详见 docs/refactor-plan.md Phase 1）。
/// 由 WorkSetup.ResolveToolPaths 一次性解析，作为显式参数向下透传，不再落入全局可变状态。
/// </summary>
internal readonly record struct ToolPaths(string Ffmpeg, string Mp4box, string? Aria2c);
