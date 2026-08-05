using System.Text.Json.Serialization;

using BBDown.Core;
using BBDown.Serve;

namespace BBDown;

/// <summary>
/// 下载任务的不可变请求：由命令行解析（<see cref="Cli.CommandLineInvoker"/>）或 serve 请求（<see cref="ServeRequestOptions"/>）构造，
/// 贯穿解析与下载全流程（<see cref="Pipeline.DownloadPipeline.RunAsync"/> → <see cref="Pipeline.WorkSetup.Build"/> / <see cref="Pipeline.VideoInfo.FetchAsync"/> / <see cref="Pipeline.PageQueue.RunAsync"/>）。
/// 全部属性为 <c>init</c>：构造后不可变，任何「修正」（冲突消解、serve 覆盖路径/host）都返回新的 <see cref="DownloadRequest"/> 副本（<c>with</c>），
/// 调用方可以信赖「传入后不会被改」。
/// 注意：它不是 serve 的请求契约——serve 端使用的是经过裁剪的 <see cref="ServeRequestOptions"/>，
/// 主机可控字段（路径、外部程序路径、UserAgent、Debug）不会出现在该 DTO 中，从结构上杜绝远程注入。
/// </summary>
internal sealed record DownloadRequest
{
    public string Url { get; init; } = default!;
    public bool UseTvApi { get; init; }
    public bool UseAppApi { get; init; }
    public bool UseIntlApi { get; init; }
    public bool UseMP4box { get; init; }
    public string? EncodingPriority { get; init; }
    public string? DfnPriority { get; init; }
    /// <summary>命令行上 --encoding-priority 写在 --dfn-priority 之前时为 true；serve 模式无书写顺序，恒为 false。</summary>
    public bool EncodingFirst { get; init; }
    public bool OnlyShowInfo { get; init; }
    public bool ShowAll { get; init; }
    public bool UseAria2c { get; init; }
    public bool Interactive { get; init; }
    public bool HideStreams { get; init; }
    public bool SingleThread { get; init; }
    public bool NoMetadata { get; init; }
    public bool VideoOnly { get; init; }
    public bool AudioOnly { get; init; }
    public bool DanmakuOnly { get; init; }
    public bool CoverOnly { get; init; }
    public bool SubOnly { get; init; }
    public bool Debug { get; init; }
    public bool SkipMux { get; init; }
    public bool NoSub { get; init; }
    public bool NoCover { get; init; }
    /// <summary>专栏导出时不下载图片，Markdown 中保留远程图片链接</summary>
    public bool NoImages { get; init; }
    public bool NoForceHttp { get; init; }
    public bool DownloadDanmaku { get; init; }
    public string? DownloadDanmakuFormats { get; init; }
    /// <summary>要下载的评论条数，0 表示不下载评论</summary>
    public int CommentCount { get; init; }
    public string? CommentSort { get; init; }
    public string? CommentFormats { get; init; }
    /// <summary>逐条抓取楼中楼全部回复，而非只保留接口内联的前几条</summary>
    public bool FullComment { get; init; }
    public bool AllowAi { get; init; }
    public bool VideoAscending { get; init; }
    public bool AudioAscending { get; init; }
    public bool AllowPcdn { get; init; }
    public bool AllowPreview { get; init; }
    /// <summary>直播录制清晰度（qn）。未登录时服务端会无视该值直接下发 250。</summary>
    public int LiveQuality { get; init; } = Core.Live.LiveQuality.Original;
    public bool NoForceHost { get; init; }
    public bool SaveArchivesToFile { get; init; }
    public bool StopOnError { get; init; }
    public string FilePattern { get; init; } = "";
    public string MultiFilePattern { get; init; } = "";
    public string SelectPage { get; init; } = "";
    public string Lang { get; init; } = "";
    public string UserAgent { get; init; } = "";
    public string Cookie { get; init; } = "";
    public string AccessToken { get; init; } = "";
    public string Aria2cArgs { get; init; } = "";
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
[JsonSerializable(typeof(ServeRequestOptions))]
internal sealed partial class DownloadRequestJsonContext : JsonSerializerContext;
