namespace BBDown.Serve;

/// <summary>
/// /healthz 响应：进程存活状态与运行中任务数。
/// </summary>
public sealed record HealthStatus(string Status, int Running);
