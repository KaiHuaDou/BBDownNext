namespace BBDown.Serve;

/// <summary>
/// /healthz 响应：进程存活状态、运行中任务数与事件流是否启用（--no-interactive 为 false）。
/// 事件流开关供前端在无任务时也能判定 WebSocket 通道可用性（订阅探测依赖有运行任务）。
/// </summary>
public sealed record HealthStatus(string Status, int Running, bool Interactive);
