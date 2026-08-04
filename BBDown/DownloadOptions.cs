using System.Text.Json;
using System.Text.Json.Serialization;

using BBDown.Core;
using BBDown.Serve;

namespace BBDown;

/// <summary>
/// 下载任务的运行时配置：由命令行解析（<see cref="Cli.CommandLineInvoker"/>）或 serve 请求（<see cref="ServeRequestOptions"/>）构造，
/// 贯穿解析与下载全流程（<see cref="Pipeline.DownloadPipeline.RunAsync"/> → <see cref="Pipeline.WorkSetup.Build"/> / <see cref="Pipeline.VideoInfo.FetchAsync"/> / <see cref="Pipeline.PageQueue.RunAsync"/>）。
/// 注意：它不是 serve 的请求契约——serve 端使用的是经过裁剪的 <see cref="ServeRequestOptions"/>，
/// 主机可控字段（路径、外部程序路径、UserAgent、Debug）不会出现在该 DTO 中，从结构上杜绝远程注入。
/// </summary>
internal class DownloadOptions
{
    public string Url { get; set; } = default!;
    public bool UseTvApi { get; set; }
    public bool UseAppApi { get; set; }
    public bool UseIntlApi { get; set; }
    public bool UseMP4box { get; set; }
    public string? EncodingPriority { get; set; }
    public string? DfnPriority { get; set; }
    /// <summary>命令行上 --encoding-priority 写在 --dfn-priority 之前时为 true；serve 模式无书写顺序，恒为 false。</summary>
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
    public bool Debug { get; set; }
    public bool SkipMux { get; set; }
    public bool NoSub { get; set; }
    public bool NoCover { get; set; }
    /// <summary>专栏导出时不下载图片，Markdown 中保留远程图片链接</summary>
    public bool NoImages { get; set; }
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
    public string FilePattern { get; set; } = "";
    public string MultiFilePattern { get; set; } = "";
    public string SelectPage { get; set; } = "";
    public string Lang { get; set; } = "";
    public string UserAgent { get; set; } = "";
    public string Cookie { get; set; } = "";
    public string AccessToken { get; set; } = "";
    public string Aria2cArgs { get; set; } = "";
    public string WorkDir { get; set; } = "";
    public string FFmpegPath { get; set; } = "";
    public string Mp4boxPath { get; set; } = "";
    public string Aria2cPath { get; set; } = "";
    public string UposHost { get; set; } = "";
    public string DelayPerPage { get; set; } = "0";
    public string Host { get; set; } = BiliApi.MainHost;
    public string EpHost { get; set; } = BiliApi.MainHost;
    public string TvHost { get; set; } = BiliApi.TvHost;
    public string Area { get; set; } = "";
    public string? ConfigFile { get; set; }

    /// <summary>
    /// 返回遮蔽了 Cookie / AccessToken 的副本，用于调试日志，避免凭据明文泄露（P0-3）
    /// </summary>
    internal DownloadOptions WithSecretsRedacted( )
    {
        var clone = JsonSerializer.Deserialize(
            JsonSerializer.Serialize(this, DownloadOptionsJsonContext.Default.DownloadOptions),
            DownloadOptionsJsonContext.Default.DownloadOptions)!;
        clone.Cookie = "";
        clone.AccessToken = "";
        return clone;
    }
}

[JsonSerializable(typeof(DownloadOptions))]
[JsonSerializable(typeof(ServeRequestOptions))]
internal sealed partial class DownloadOptionsJsonContext : JsonSerializerContext;
