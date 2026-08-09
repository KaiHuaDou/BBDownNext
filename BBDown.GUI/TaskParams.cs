using System;
using System.Collections.Generic;

namespace BBDown.GUI;

/// <summary>面板选项快照，不可变；既是子进程参数源，也是配置持久化 DTO。</summary>
public sealed record TaskParams
{
    /// <summary>下载内容字符集，顺序固定为 a v c C d i m M o O S s，默认对齐 CLI 的 avmsCiM。</summary>
    public string Content { get; init; } = CliArgsBuilder.DefaultContent;

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

    // 常用输入选项，空串表示未设置（走 CLI 内置默认）
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
    public string DrmKey { get; init; } = "";   // 多个密钥以空白分隔，CLI 支持多次传入 --drm-key
    public string Host { get; init; } = "";
    public string EpHost { get; init; } = "";
    public string TvHost { get; init; } = "";
    public string Area { get; init; } = "";
    public string UposHost { get; init; } = "";
}

/// <summary>把 TaskParams 序列化为 BBDown.exe 命令行参数，顺序固定：内容 → 布尔 → 输入 → url。</summary>
public static class CliArgsBuilder
{
    /// <summary>CLI 内容默认值。</summary>
    public const string DefaultContent = "avmsCiM";

    public static string[] Build(TaskParams options, string url)
    {
        List<string> args = [];
        AddContent(args, options.Content);
        AddBooleanOptions(args, options);
        AddInputOptions(args, options);
        args.Add(url);
        return [.. args];
    }

    private static void AddContent(List<string> args, string content)
    {
        // 全空也显式传 --get ""（CLI 解析为空内容集），否则 CLI 回落默认 avmsCiM，
        // 用户清空所有复选框反而全量下载
        args.Add("--get");
        args.Add(content);
    }

    private static void AddBooleanOptions(List<string> args, TaskParams options)
    {
        AddFlag(args, "--aria2c", options.UseAria2c);
        AddFlag(args, "--single-thread", options.SingleThread);
        AddFlag(args, "--info-only", options.InfoOnly);
        AddFlag(args, "--all", options.ShowAll);
        AddFlag(args, "--allow-preview", options.AllowPreview);
        AddFlag(args, "--save-records", options.SaveRecords);
        AddFlag(args, "--stop-on-error", options.StopOnError);
        AddFlag(args, "--debug", options.Debug);
        AddFlag(args, "--video-ascending", options.VideoAscending);
        AddFlag(args, "--audio-ascending", options.AudioAscending);
        AddFlag(args, "--allow-pcdn", options.AllowPcdn);
        AddFlag(args, "--no-force-host", options.NoForceHost);
        AddFlag(args, "--no-force-http", options.NoForceHttp);
    }

    private static void AddInputOptions(List<string> args, TaskParams options)
    {
        AddOption(args, "--mux", options.Mux, "mpeg4");
        AddOption(args, "--encoding-priority", options.EncodingPriority, "");
        AddOption(args, "--dfn-priority", options.DfnPriority, "");
        AddOption(args, "--pages", options.Pages, "");
        AddOption(args, "--danmaku-formats", options.DanmakuFormats, "xml,ass");
        AddOption(args, "--comments-count", options.CommentsCount, "0");
        AddOption(args, "--comments-sort", options.CommentsSort, "hot");
        AddOption(args, "--comments-formats", options.CommentsFormats, "json,txt");
        AddOption(args, "--lang", options.Lang, "");
        AddOption(args, "--cookie", options.Cookie, "");
        AddOption(args, "--access-token", options.AccessToken, "");
        AddOption(args, "--user-agent", options.UserAgent, "");
        AddOption(args, "--work-dir", options.WorkDir, "");
        AddOption(args, "--ffmpeg-path", options.FFmpegPath, "");
        AddOption(args, "--mp4box-path", options.Mp4boxPath, "");
        AddOption(args, "--aria2c-path", options.Aria2cPath, "");
        AddOption(args, "--aria2c-args", options.Aria2cArgs, "");
        AddOption(args, "--delay-per-page", options.DelayPerPage, "0");
        AddOption(args, "--live-quality", options.LiveQuality, "10000");
        AddOption(args, "--api", options.Api, "web");
        AddOption(args, "--file-pattern", options.FilePattern, "");
        AddOption(args, "--multi-file-pattern", options.MultiFilePattern, "");

        foreach (var key in options.DrmKey.Split((char[]?) null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            args.Add("--drm-key");
            args.Add(key);
        }

        AddOption(args, "--host", options.Host, "");
        AddOption(args, "--ep-host", options.EpHost, "");
        AddOption(args, "--tv-host", options.TvHost, "");
        AddOption(args, "--area", options.Area, "");
        AddOption(args, "--upos-host", options.UposHost, "");
    }

    private static void AddFlag(List<string> args, string name, bool value)
    {
        if (value)
        {
            args.Add(name);
        }
    }

    private static void AddOption(List<string> args, string name, string value, string defaultValue)
    {
        if (value.Length > 0 && value != defaultValue)
        {
            args.Add(name);
            args.Add(value);
        }
    }
}
