using System.Text.Json;
using System.Text.Json.Serialization;

using BBDown.Core;

namespace BBDown;

/// <summary>
/// 命令行选项绑定 + 运行时下载配置 + serve 模式请求契约 三者共用的单一模型。
/// 注意：<see cref="ServeRequestOptions"/> 直接继承此类，因此每一个 CLI 选项都会自动成为 serve API 的可注入字段；
/// 所有主机可控字段（FFmpegPath/WorkDir/FilePattern/...）必须在 <see cref="BBDownApiServer.OverrideHostControlledOptions"/> 中被清零，
/// 新增选项时务必确认它不会成为意外的安全注入点。这是已知的过度耦合点，未来应拆为「CLI 绑定」「下载配置」「serve 请求 DTO」三套独立模型。
/// </summary>
internal class MyOption
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
    public bool NoForceHttp { get; set; }
    public bool DownloadDanmaku { get; set; }
    public string? DownloadDanmakuFormats { get; set; }
    public bool AllowAi { get; set; }
    public bool VideoAscending { get; set; }
    public bool AudioAscending { get; set; }
    public bool AllowPcdn { get; set; }
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
    internal MyOption WithSecretsRedacted( )
    {
        var clone = JsonSerializer.Deserialize(
            JsonSerializer.Serialize(this, MyOptionJsonContext.Default.MyOption),
            MyOptionJsonContext.Default.MyOption)!;
        clone.Cookie = "";
        clone.AccessToken = "";
        return clone;
    }
}

[JsonSerializable(typeof(MyOption))]
[JsonSerializable(typeof(ServeRequestOptions))]
internal sealed partial class MyOptionJsonContext : JsonSerializerContext;
