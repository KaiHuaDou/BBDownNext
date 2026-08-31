using System.Text.Json;
using System.Text.Json.Serialization;

using BBDown.Core;
using BBDown.Core.Download;
using BBDown.Serve.Tasks;

namespace BBDown.Serve;

/// <summary>
/// serve 模式的任务请求契约（<c>POST /api/v1/tasks</c> 的 JSON 请求体）。
/// 它是 <see cref="DownloadRequest"/> 的「受控子集」：只包含客户端允许提交的字段，
/// 主动排除主机可控字段（FFmpegPath / Mp4boxPath / Aria2cPath / Aria2cArgs / WorkDir / FilePattern / MultiFilePattern / Host / EpHost / TvHost）、
/// 进程级全局字段（Debug / UserAgent）与本地配置文件（ConfigFile）——
/// 这样新增一个下载选项时不会自动变成 serve 的可注入点，也不必再维护一份「清零列表」。
/// 交互式选项（InteractivePages / InteractiveQuality）经 WebSocket 事件流送达客户端应答，随任务提交。
/// 其中 Host/EpHost/TvHost 因「请求不带 cookie 时回落本机 SESSDATA、host 又由请求体控制」会形成凭据外泄链（P0-1），
/// 已整体移出请求契约，改为 serve 启动参数（--host/--ep-host/--tv-host）固定，详见 <see cref="TaskStore.ApplyServeHost"/>。
/// </summary>
internal sealed class ServeRequestOptions
{
    public string Url { get; set; } = default!;
    /// <summary>API 解析通道（web / tv / app / intl，忽略大小写），缺省回落 web。</summary>
    [JsonConverter(typeof(ApiTypeJsonConverter))]
    public ApiType Api { get; set; } = ApiType.Web;
    /// <summary>下载内容字符集（如 "avmsCiM"），非法字符忽略，缺省回落默认内容集。</summary>
    [JsonConverter(typeof(DownloadContentJsonConverter))]
    public DownloadContent Content { get; set; } = ContentSelector.DefaultFlags;
    /// <summary>混流方式（none / mpeg4 / mp4box / mkv），缺省回落 mpeg4。</summary>
    [JsonConverter(typeof(MuxModeJsonConverter))]
    public MuxMode Mux { get; set; } = MuxMode.Mpeg4;
    public string? EncodingPriority { get; set; }
    public string? DfnPriority { get; set; }
    public string? AudioQuality { get; set; }
    public bool EncodingFirst { get; set; }
    public bool OnlyShowInfo { get; set; }
    public bool ShowAll { get; set; }
    public bool UseAria2c { get; set; }
    public bool HideStreams { get; set; }
    public bool SingleThread { get; set; }
    public bool NoForceHttp { get; set; }
    public string? DownloadDanmakuFormats { get; set; }
    public int CommentCount { get; set; }
    public string? CommentSort { get; set; }
    public string? CommentFormats { get; set; }
    public bool VideoAscending { get; set; }
    public bool AudioAscending { get; set; }
    public bool AllowPcdn { get; set; }
    public bool AllowPreview { get; set; }
    public bool NoForceHost { get; set; }
    public bool SaveArchivesToFile { get; set; }
    public bool StopOnError { get; set; }
    /// <summary>交互式逐集确认（--interactive-pages）。经 AskBus 发布选项请求，由 WebSocket 事件流送达客户端应答；无订阅者时回落非交互。</summary>
    public bool InteractivePages { get; set; }
    /// <summary>交互式选择清晰度（--interactive-quality）。同上，依赖事件流（始终开启）。</summary>
    public bool InteractiveQuality { get; set; }
    /// <summary>直播录制清晰度（qn），缺省回落原画。其它选项受控于服务端固定 host，本项随任务变化无注入风险。</summary>
    public int LiveQuality { get; set; } = BBDown.Core.Download.LiveQuality.Original;
    public string Pages { get; set; } = "";
    public string Lang { get; set; } = "";
    public string Cookie { get; set; } = "";
    public string AccessToken { get; set; } = "";
    public string UposHost { get; set; } = "";
    public string DelayPerPage { get; set; } = "0";
    /// <summary>每个下载项的额外重试次数，缺省回落 3。</summary>
    public int MaxRetry { get; set; } = 3;
    public string Area { get; set; } = "";

    /// <summary>任务完成回调地址（仅允许公网 http/https，由服务端做 SSRF 校验）。</summary>
    public string? CallBackWebHook { get; set; }

    /// <summary>
    /// 将受控请求转换为完整下载配置。主机可控字段不在本 DTO 中，转换时显式回落为
    /// <see cref="DownloadRequest"/> 的安全默认值（空路径、官方 host）——它们本就不在请求体里，
    /// 但 record 经 STJ 反序列化时字段初始化器被跳过（改用生成构造器，字符串参数默认 null），
    /// 故在此用 <c>with</c> 兜底，结构上杜绝远程注入。
    /// </summary>
    internal DownloadRequest ToDownloadRequest( )
    {
        var r = JsonSerializer.Deserialize(
                JsonSerializer.Serialize(this, ServeRequestOptionsJsonContext.Default.ServeRequestOptions),
                ServeRequestOptionsJsonContext.Default.DownloadRequest)!;
        return r with
        {
            // 主机可控字段不在请求契约中，回落为安全默认值（空路径 / 官方 host）
            FFmpegPath = "",
            Mp4boxPath = "",
            Aria2cPath = "",
            Aria2cArgs = "",
            PostProcessPath = "",
            WorkDir = "",
            FilePattern = "",
            MultiFilePattern = "",
            UserAgent = "",
            Host = BiliApi.MainHost,
            EpHost = BiliApi.MainHost,
            TvHost = BiliApi.TvHost,
        };
    }
}
