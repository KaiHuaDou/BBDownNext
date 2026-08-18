using System.Text.Json.Serialization;

namespace BBDown.Core.Download;

/// <summary>
/// 下载任务的不可变请求：由 CLI 参数解析或 serve 请求构造，
/// 贯穿解析与下载全流程（<see cref="BBDown.Core.Pipeline.DownloadPipeline.RunAsync"/> → <see cref="BBDown.Core.Pipeline.WorkSetup.Build"/> / <see cref="BBDown.Core.Pipeline.VideoInfo.FetchAsync"/> / <see cref="BBDown.Core.Pipeline.PageQueue.RunAsync"/>）。
/// 全部属性为 <c>init</c>：构造后不可变，任何「修正」（冲突消解、serve 覆盖路径/host）都返回新的 <see cref="DownloadRequest"/> 副本（<c>with</c>），
/// 调用方可以信赖「传入后不会被改」。
/// 注意：它不是 serve 的请求契约——serve 端使用的是经过裁剪的请求 DTO，
/// 主机可控字段（路径、外部程序路径、UserAgent、Debug）不会出现在该 DTO 中，从结构上杜绝远程注入。
/// </summary>
public sealed record DownloadRequest
{
    public string Url { get; init; } = default!;
    /// <summary>API 解析通道（web / tv / app / intl），单值选择。</summary>
    public ApiType Api { get; init; } = ApiType.Web;
    /// <summary>下载内容标志集（--get ∪ --with − --without），消费点用 <see cref="ContentSelector.Has"/> 查询。</summary>
    public DownloadContent Content { get; init; } = ContentSelector.DefaultFlags;
    /// <summary>混流方式（--mux / -m），默认 FFmpeg 混流为 MP4。</summary>
    public MuxMode Mux { get; init; } = MuxMode.Mpeg4;
    public string? EncodingPriority { get; init; }
    public string? DfnPriority { get; init; }
    public string? AudioQuality { get; init; }
    /// <summary>命令行上 --encoding-priority 写在 --dfn-priority 之前时为 true；serve 模式无书写顺序，恒为 false。</summary>
    public bool EncodingFirst { get; init; }
    public bool OnlyShowInfo { get; init; }
    public bool ShowAll { get; init; }
    public bool UseAria2c { get; init; }
    /// <summary>交互式选择清晰度（--interactive-quality）。serve 下不可用：无 stdin 可交互。</summary>
    public bool InteractiveQuality { get; init; }
    public bool HideStreams { get; init; }
    public bool SingleThread { get; init; }
    public bool Debug { get; init; }
    public bool NoForceHttp { get; init; }
    public string? DownloadDanmakuFormats { get; init; }
    /// <summary>要下载的评论条数，0 表示不下载评论</summary>
    public int CommentCount { get; init; }
    public string? CommentSort { get; init; }
    public string? CommentFormats { get; init; }
    public bool VideoAscending { get; init; }
    public bool AudioAscending { get; init; }
    public bool AllowPcdn { get; init; }
    public bool AllowPreview { get; init; }
    /// <summary>直播录制清晰度（qn）。未登录时服务端会无视该值直接下发 250。</summary>
    public int LiveQuality { get; init; } = BBDown.Core.Download.LiveQuality.Original;
    public bool NoForceHost { get; init; }
    public bool SaveArchivesToFile { get; init; }
    public bool StopOnError { get; init; }
    public string FilePattern { get; init; } = "";
    public string MultiFilePattern { get; init; } = "";
    /// <summary>手动指定分 P 表达式（--pages）。</summary>
    public string Pages { get; init; } = "";
    /// <summary>逐集确认是否下载（--interactive-pages）。serve 下不可用：无 stdin 可交互。</summary>
    public bool InteractivePages { get; init; }
    public string Lang { get; init; } = "";
    public string UserAgent { get; init; } = "";
    public string Cookie { get; init; } = "";
    public string AccessToken { get; init; } = "";
    public string Aria2cArgs { get; init; } = "";
    /// <summary>外部后处理可执行文件路径；空串不启用。随请求透传而非进程级全局，GUI 并发任务互不覆盖。</summary>
    public string PostProcessPath { get; init; } = "";
    public string WorkDir { get; init; } = "";
    public string FFmpegPath { get; init; } = "";
    public string Mp4boxPath { get; init; } = "";
    public string Aria2cPath { get; init; } = "";
    public string UposHost { get; init; } = "";
    public string DelayPerPage { get; init; } = "0";
    public string Host { get; init; } = BiliApi.MainHost;
    public string EpHost { get; init; } = BiliApi.MainHost;
    public string TvHost { get; init; } = BiliApi.TvHost;
    public string Area { get; init; } = "";
    public string? ConfigFile { get; init; }

    /// <summary>
    /// 返回遮蔽了 Cookie / AccessToken 的副本，用于调试日志，避免凭据明文泄露（P0-3）。
    /// 类型为扁平值对象，<c>with</c> 即浅克隆，等价于原 JSON 深拷贝但无序列化开销。
    /// </summary>
    internal DownloadRequest WithSecretsRedacted( )
    {
        return this with { Cookie = "", AccessToken = "" };
    }
}

[JsonSerializable(typeof(DownloadRequest))]
public partial class DownloadRequestJsonContext : JsonSerializerContext;
