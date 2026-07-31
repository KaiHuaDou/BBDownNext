using System.Text.Json.Serialization;

using BBDown.Core;

namespace BBDown;

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
    public bool MultiThread { get; set; } = true;
    public bool SingleThread { get; set; }
    public bool SimplyMux { get; set; }
    public bool VideoOnly { get; set; }
    public bool AudioOnly { get; set; }
    public bool DanmakuOnly { get; set; }
    public bool CoverOnly { get; set; }
    public bool SubOnly { get; set; }
    public bool Debug { get; set; }
    public bool SkipMux { get; set; }
    public bool SkipSubtitle { get; set; }
    public bool SkipCover { get; set; }
    public bool ForceHttp { get; set; } = true;
    public bool DownloadDanmaku { get; set; }
    public string? DownloadDanmakuFormats { get; set; }
    public bool SkipAi { get; set; } = true;
    public bool VideoAscending { get; set; }
    public bool AudioAscending { get; set; }
    public bool AllowPcdn { get; set; }
    public bool ForceReplaceHost { get; set; } = true;
    public bool SaveArchivesToFile { get; set; }
    public string FilePattern { get; set; } = "";
    public string MultiFilePattern { get; set; } = "";
    public string SelectPage { get; set; } = "";
    public string Language { get; set; } = "";
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
}

[JsonSerializable(typeof(MyOption))]
[JsonSerializable(typeof(ServeRequestOptions))]
internal sealed partial class MyOptionJsonContext : JsonSerializerContext;
