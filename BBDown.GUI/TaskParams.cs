using System;

using BBDown.Core;
using BBDown.Core.Download;

namespace BBDown.GUI;

/// <summary>面板选项快照，不可变；既是下载任务参数源，也是配置持久化 DTO。</summary>
public sealed record TaskParams
{
    /// <summary>下载内容字符集，顺序固定为 a v c C d i m M o O S s，默认对齐 CLI 的 avmsCiM。</summary>
    public string Content { get; init; } = "avmsCiM";

    // 常用布尔选项
    public bool UseAria2c { get; init; }
    public bool SingleThread { get; init; }
    public bool InfoOnly { get; init; }
    public bool ShowAll { get; init; }
    public bool AllowPreview { get; init; }
    public bool SaveRecords { get; init; }
    public bool StopOnError { get; init; }
    public bool Debug { get; init; }
    public bool VideoAscending { get; init; }
    public bool AudioAscending { get; init; }

    // 常用输入选项，空串表示未设置（走 Core 默认值）
    public string Mux { get; init; } = "mpeg4";
    public string EncodingPriority { get; init; } = "";
    public string DfnPriority { get; init; } = "";
    public string Pages { get; init; } = "";
    public string DanmakuFormats { get; init; } = "xml,ass";
    public string CommentsCount { get; init; } = "0";
    public string CommentsSort { get; init; } = "hot";
    public string CommentsFormats { get; init; } = "json,txt";
    public string Lang { get; init; } = "";
    public string Cookie { get; init; } = "";
    public string AccessToken { get; init; } = "";
    public string UserAgent { get; init; } = "";
    public string WorkDir { get; init; } = "";
    public string FFmpegPath { get; init; } = "";
    public string Mp4boxPath { get; init; } = "";
    public string Aria2cPath { get; init; } = "";
    public string Aria2cArgs { get; init; } = "";
    public string DelayPerPage { get; init; } = "0";
    public string LiveQuality { get; init; } = "10000";
    public string Api { get; init; } = "web";
    public string FilePattern { get; init; } = "";
    public string MultiFilePattern { get; init; } = "";

    // 高级选项
    public bool AllowPcdn { get; init; }
    public bool NoForceHost { get; init; }
    public bool NoForceHttp { get; init; }
    public string Host { get; init; } = "";
    public string EpHost { get; init; } = "";
    public string TvHost { get; init; } = "";
    public string Area { get; init; } = "";
    public string UposHost { get; init; } = "";
}

/// <summary>把面板选项转换为 Core 的下载请求。数值/枚举字段解析失败时回落安全默认值。</summary>
public static class TaskParamsMapper
{
    public static DownloadRequest ToDownloadRequest(this TaskParams options, string url)
    {
        return new DownloadRequest
        {
            Url = url,
            Api = ApiTypeUtil.TryParse(options.Api) ?? ApiType.Web,
            Content = ContentSelector.FromNormalizedString(options.Content),
            Mux = MuxModeUtil.TryParse(options.Mux) ?? MuxMode.Mpeg4,
            EncodingPriority = NullIfEmpty(options.EncodingPriority),
            DfnPriority = NullIfEmpty(options.DfnPriority),
            EncodingFirst = false,
            OnlyShowInfo = options.InfoOnly,
            ShowAll = options.ShowAll,
            UseAria2c = options.UseAria2c,
            HideStreams = false,
            SingleThread = options.SingleThread,
            Debug = options.Debug,
            NoForceHttp = options.NoForceHttp,
            DownloadDanmakuFormats = options.DanmakuFormats,
            CommentCount = int.TryParse(options.CommentsCount, out var count) ? count : 0,
            CommentSort = options.CommentsSort,
            CommentFormats = options.CommentsFormats,
            VideoAscending = options.VideoAscending,
            AudioAscending = options.AudioAscending,
            AllowPcdn = options.AllowPcdn,
            AllowPreview = options.AllowPreview,
            LiveQuality = int.TryParse(options.LiveQuality, out var quality) ? quality : LiveQuality.Original,
            NoForceHost = options.NoForceHost,
            SaveArchivesToFile = options.SaveRecords,
            StopOnError = options.StopOnError,
            FilePattern = options.FilePattern,
            MultiFilePattern = options.MultiFilePattern,
            Pages = options.Pages,
            Lang = options.Lang,
            UserAgent = options.UserAgent,
            Cookie = options.Cookie,
            AccessToken = options.AccessToken,
            Aria2cArgs = options.Aria2cArgs,
            WorkDir = options.WorkDir,
            FFmpegPath = options.FFmpegPath,
            Mp4boxPath = options.Mp4boxPath,
            Aria2cPath = options.Aria2cPath,
            UposHost = options.UposHost,
            DelayPerPage = options.DelayPerPage,
            Host = options.Host,
            EpHost = options.EpHost,
            TvHost = options.TvHost,
            Area = options.Area,
        };
    }

    private static string? NullIfEmpty(string value)
    {
        return value.Length == 0 ? null : value;
    }
}
