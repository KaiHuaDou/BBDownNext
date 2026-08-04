using System.Text.Json;

using BBDown.Core;

namespace BBDown.Serve;

/// <summary>
/// serve 模式的任务请求契约（<c>/add-task</c> 的 JSON 请求体）。
/// 它是 <see cref="DownloadOptions"/> 的「受控子集」：只包含客户端允许提交的字段，
/// 主动排除主机可控字段（FFmpegPath / Mp4boxPath / Aria2cPath / Aria2cArgs / WorkDir / FilePattern / MultiFilePattern / Host / EpHost / TvHost）、
/// 进程级全局字段（Debug / UserAgent）与本地配置文件（ConfigFile）。
/// 这样新增一个下载选项时不会自动变成 serve 的可注入点，也不必再维护一份「清零列表」。
/// 其中 Host/EpHost/TvHost 因「请求不带 cookie 时回落本机 SESSDATA、host 又由请求体控制」会形成凭据外泄链（P0-1），
/// 已整体移出请求契约，改为 serve 启动参数（--host/--ep-host/--tv-host）固定，详见 <see cref="BBDownApiServer.ApplyServeHost"/>。
/// </summary>
internal sealed class ServeRequestOptions
{
    public string Url { get; set; } = default!;
    public bool UseTvApi { get; set; }
    public bool UseAppApi { get; set; }
    public bool UseIntlApi { get; set; }
    public bool UseMP4box { get; set; }
    public string? EncodingPriority { get; set; }
    public string? DfnPriority { get; set; }
    public bool EncodingFirst { get; set; }
    public bool OnlyShowInfo { get; set; }
    public bool ShowAll { get; set; }
    public bool UseAria2c { get; set; }
    public bool Interactive { get; set; }
    public bool HideStreams { get; set; }
    public bool SingleThread { get; set; }
    public bool NoMetadata { get; set; }
    public bool VideoOnly { get; set; }
    public bool AudioOnly { get; set; }
    public bool DanmakuOnly { get; set; }
    public bool CoverOnly { get; set; }
    public bool SubOnly { get; set; }
    public bool SkipMux { get; set; }
    public bool NoSub { get; set; }
    public bool NoCover { get; set; }
    public bool NoForceHttp { get; set; }
    public bool DownloadDanmaku { get; set; }
    public string? DownloadDanmakuFormats { get; set; }
    public bool AllowAi { get; set; }
    public bool VideoAscending { get; set; }
    public bool AudioAscending { get; set; }
    public bool AllowPcdn { get; set; }
    public bool AllowPreview { get; set; }
    public bool NoForceHost { get; set; }
    public bool SaveArchivesToFile { get; set; }
    public bool StopOnError { get; set; }
    public string SelectPage { get; set; } = "";
    public string Lang { get; set; } = "";
    public string Cookie { get; set; } = "";
    public string AccessToken { get; set; } = "";
    public string UposHost { get; set; } = "";
    public string DelayPerPage { get; set; } = "0";
    public string Area { get; set; } = "";

    /// <summary>任务完成回调地址（仅允许公网 http/https，由服务端做 SSRF 校验）。</summary>
    public string? CallBackWebHook { get; set; }

    /// <summary>
    /// 将受控请求转换为完整下载配置。主机可控字段不在本 DTO 中，转换后回落为
    /// <see cref="DownloadOptions"/> 的安全默认值（空路径、进程级配置由服务端决定）。
    /// </summary>
    internal DownloadOptions ToDownloadOptions( )
    {
        return JsonSerializer.Deserialize(
                JsonSerializer.Serialize(this, DownloadOptionsJsonContext.Default.ServeRequestOptions),
                DownloadOptionsJsonContext.Default.DownloadOptions)!;
    }
}
