namespace BBDown.Serve;

/// <summary>
/// /healthz 响应：进程存活状态与运行中任务数。事件流（WebSocket /hubs/tasks）始终启用，无需开关字段。
/// </summary>
public sealed record HealthStatus(string Status, int Running);
