using System;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.Linq;
using System.Threading.Tasks;

using BBDown.Core;
using BBDown.Util;

using static BBDown.Cli.CliOptions;
using static BBDown.Core.Logger;

namespace BBDown.Cli;

internal static class CommandLineInvoker
{
    public static RootCommand GetRootCommand(Func<DownloadRequest, Task<int>> action)
    {
        // 注册顺序即 --help 显示顺序，与 CliOptions 定义的分组顺序保持一致
        var rootCommand = new RootCommand
        {
            Url,
            // 解析模式
            ApiOption,
            Host,
            EpHost,
            TvHost,
            Area,
            // 清晰度与编码
            EncodingPriority,
            DfnPriority,
            VideoAscending,
            AudioAscending,
            InteractiveQuality,
            HideStreams,
            InfoOnly,
            ShowAll,
            // 下载内容
            Content,
            WithContent,
            WithoutContent,
            DownloadDanmakuFormats,
            CommentsCount,
            CommentsSort,
            CommentsFormats,
            SkipMux,
            DrmKey,
            AllowPreview,
            Lang,
            // 直播录制
            LiveQualityOption,
            // 下载方式与性能
            UseAria2c,
            Aria2cArgs,
            SingleThread,
            DelayPerPage,
            UposHost,
            NoForceHost,
            AllowPcdn,
            NoForceHttp,
            // 账号与凭据
            Cookie,
            AccessToken,
            UserAgent,
            // 文件、路径与调试
            FilePattern,
            MultiFilePattern,
            Pages,
            InteractivePages,
            WorkDir,
            FFmpegPath,
            UseMP4box,
            Mp4boxPath,
            Aria2cPath,
            SaveRecords,
            StopOnError,
            ConfigFile,
            Debug
        };

        rootCommand.SetAction(async parseResult =>
        {
            var content = ContentSelector.Resolve(
                parseResult.GetValue(Content) ?? [],
                parseResult.GetValue(WithContent) ?? [],
                parseResult.GetValue(WithoutContent) ?? [],
                commentCountExplicit: IsExplicit(parseResult, "--comments-count"),
                commentSortExplicit: IsExplicit(parseResult, "--comments-sort"),
                commentFormatsExplicit: IsExplicit(parseResult, "--comments-formats"),
                danmakuFormatsExplicit: IsExplicit(parseResult, "--danmaku-formats"),
                out var warnings);
            foreach (var warning in warnings)
            {
                LogWarn(warning);
            }

            var option = new DownloadRequest
            {
                Api = parseResult.GetValue(ApiOption),
                Url = parseResult.GetValue(Url) ?? "",
                Content = content,
                UseMP4box = parseResult.GetValue(UseMP4box)!,
                EncodingPriority = parseResult.GetValue(EncodingPriority) ?? "",
                DfnPriority = parseResult.GetValue(DfnPriority) ?? "",
                EncodingFirst = ResolveEncodingFirst(parseResult),
                OnlyShowInfo = parseResult.GetValue(InfoOnly)!,
                ShowAll = parseResult.GetValue(ShowAll)!,
                UseAria2c = parseResult.GetValue(UseAria2c)!,
                InteractiveQuality = parseResult.GetValue(InteractiveQuality)!,
                HideStreams = parseResult.GetValue(HideStreams)!,
                SingleThread = parseResult.GetValue(SingleThread)!,
                Debug = parseResult.GetValue(Debug)!,
                SkipMux = parseResult.GetValue(SkipMux)!,
                DrmKeys = parseResult.GetValue(DrmKey) ?? [],
                NoForceHttp = parseResult.GetValue(NoForceHttp)!,
                DownloadDanmakuFormats = parseResult.GetValue(DownloadDanmakuFormats) ?? "",
                CommentCount = parseResult.GetValue(CommentsCount),
                CommentSort = parseResult.GetValue(CommentsSort) ?? "",
                CommentFormats = parseResult.GetValue(CommentsFormats) ?? "",
                VideoAscending = parseResult.GetValue(VideoAscending)!,
                AudioAscending = parseResult.GetValue(AudioAscending)!,
                AllowPcdn = parseResult.GetValue(AllowPcdn)!,
                AllowPreview = parseResult.GetValue(AllowPreview)!,
                LiveQuality = parseResult.GetValue(LiveQualityOption),
                FilePattern = parseResult.GetValue(FilePattern) ?? "",
                MultiFilePattern = parseResult.GetValue(MultiFilePattern) ?? "",
                Pages = parseResult.GetValue(Pages) ?? "",
                InteractivePages = parseResult.GetValue(InteractivePages)!,
                Lang = parseResult.GetValue(Lang) ?? "",
                UserAgent = parseResult.GetValue(UserAgent) ?? "",
                Cookie = parseResult.GetValue(Cookie) ?? "",
                AccessToken = parseResult.GetValue(AccessToken) ?? "",
                Aria2cArgs = parseResult.GetValue(Aria2cArgs) ?? "",
                WorkDir = parseResult.GetValue(WorkDir) ?? "",
                FFmpegPath = parseResult.GetValue(FFmpegPath) ?? "",
                Mp4boxPath = parseResult.GetValue(Mp4boxPath) ?? "",
                Aria2cPath = parseResult.GetValue(Aria2cPath) ?? "",
                UposHost = parseResult.GetValue(UposHost) ?? "",
                NoForceHost = parseResult.GetValue(NoForceHost)!,
                SaveArchivesToFile = parseResult.GetValue(SaveRecords)!,
                StopOnError = parseResult.GetValue(StopOnError)!,
                DelayPerPage = parseResult.GetValue(DelayPerPage) ?? "",
                Host = parseResult.GetValue(Host) ?? "",
                EpHost = parseResult.GetValue(EpHost) ?? "",
                TvHost = parseResult.GetValue(TvHost) ?? "",
                Area = parseResult.GetValue(Area) ?? "",
                ConfigFile = parseResult.GetValue(ConfigFile) ?? ""
            };
            return await action(option);
        });

        return rootCommand;
    }

    // 判断选项是否由命令行显式给出（而非默认值）：评论 / 弹幕配套选项未给对应内容字符时要警告
    private static bool IsExplicit(ParseResult parseResult, string name)
    {
        return parseResult.CommandResult.Children.OfType<OptionResult>( ).Any(o => !o.Implicit && o.Option.Name == name);
    }

    /// <summary>
    /// 用户同时指定编码与清晰度优先级时，以命令行书写的先后为准。
    /// </summary>
    /// <remarks>
    /// 只能按 token 字面量匹配：<c>Token.Symbol</c> 在 System.CommandLine 中是 internal 的。
    /// </remarks>
    private static bool ResolveEncodingFirst(ParseResult parseResult)
    {
        int encodingIndex = -1, dfnIndex = -1;
        for (var i = 0; i < parseResult.Tokens.Count; i++)
        {
            var token = parseResult.Tokens[i];
            if (token.Type != TokenType.Option)
            {
                continue;
            }

            if (encodingIndex < 0 && Matches(EncodingPriority, token.Value))
            {
                encodingIndex = i;
            }
            else if (dfnIndex < 0 && Matches(DfnPriority, token.Value))
            {
                dfnIndex = i;
            }
        }

        return encodingIndex >= 0 && dfnIndex >= 0 && encodingIndex < dfnIndex;

        static bool Matches(Option option, string value)
            => value == option.Name || option.Aliases.Contains(value);
    }
}
