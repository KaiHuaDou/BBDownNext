namespace BBDown.Serve;

/// <summary>
/// serve 启动参数聚合（取代 <c>StartServer</c> 的 8 个散参）
/// 这些值由服务器启动时固定，请求体无法覆盖，避免客户端把写入位置 / API host 指向外部
/// </summary>
internal sealed record ServeConfig(
    string? ListenUrl = null,
    string? WorkDir = null,
    string? ServeToken = null,
    string? Host = null,
    string? EpHost = null,
    string? TvHost = null,
    string? CorsOrigin = null,
    int MaxConcurrent = 0,
    // 凭据：由启动参数统一指定，请求体不含这两个字段；留空回落本机 BBDown.data
    string? Cookie = null,
    string? AccessToken = null,
    // 下载器与工具相关：由启动参数决定，请求体无法覆盖
    bool UseAria2c = false,
    string? FFmpegPath = null,
    string? Mp4boxPath = null,
    string? Aria2cPath = null,
    string? Aria2cArgs = null,
    string? PostProcessPath = null,
    // 进程级全局项
    string? UserAgent = null,
    bool Debug = false,
    // 输出布局：文件命名模式与工作目录同性质，由服务端控制写入位置与产物名
    string? FilePattern = null,
    string? MultiFilePattern = null,
    // 内嵌 WebUI 与 API 同端口托管开关：启用后 BBDown.Serve 在同一端口托管前端（详见 WebUiEndpoints）
    bool EnableWebUi = false);
