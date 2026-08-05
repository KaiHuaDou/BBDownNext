namespace BBDown.Serve;

/// <summary>
/// serve 启动参数聚合（取代 <c>StartServer</c> 的 8 个散参）。
/// 这些值由服务器启动时固定，请求体无法覆盖，避免客户端把落盘位置 / API host 指向外部。
/// </summary>
internal sealed record ServeConfig(
    string? ListenUrl,
    string? WorkDir,
    string? ServeToken,
    string? Host,
    string? EpHost,
    string? TvHost,
    string? CorsOrigin,
    int MaxConcurrent);
