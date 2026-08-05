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
        var rootCommand = new RootCommand
        {
            Url,
            ApiOption,
            Content,
            WithContent,
            WithoutContent,
            UseMP4box,
            EncodingPriority,
            DfnPriority,
            InfoOnly,
            ShowAll,
            UseAria2c,
            Interactive,
            HideStreams,
            SingleThread,
            Debug,
            SkipMux,
            NoForceHttp,
            DownloadDanmakuFormats,
            CommentsCount,
            CommentsSort,
            CommentsFormats,
            VideoAscending,
            AudioAscending,
            AllowPcdn,
            AllowPreview,
            LiveQualityOption,
            FilePattern,
            MultiFilePattern,
            SelectPage,
            Lang,
            UserAgent,
            Cookie,
            AccessToken,
            Aria2cArgs,
            WorkDir,
            FFmpegPath,
            Mp4boxPath,
            Aria2cPath,
            UposHost,
            NoForceHost,
            SaveRecords,
            StopOnError,
            DelayPerPage,
            Host,
            EpHost,
            TvHost,
            Area,
            ConfigFile
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
                Interactive = parseResult.GetValue(Interactive)!,
                HideStreams = parseResult.GetValue(HideStreams)!,
                SingleThread = parseResult.GetValue(SingleThread)!,
                Debug = parseResult.GetValue(Debug)!,
                SkipMux = parseResult.GetValue(SkipMux)!,
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
                SelectPage = parseResult.GetValue(SelectPage) ?? "",
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

    /// <summary>
    /// 专栏导出子命令。与视频下载共用 <see cref="DownloadRequest"/>，但只暴露专栏用得上的选项：
    /// 画质、编码、混流、分 P 之类的参数对 Markdown 毫无意义，塞进帮助文本只会误导。
    /// 内容集沿用与根命令相同的默认 avmsCi（专栏下仅 i 生效，M 需显式给出）。
    /// </summary>
    public static Command GetOpusCommand(Func<DownloadRequest, Task<int>> action)
    {
        Command command = new("opus", "下载专栏 / 图文动态并导出为 Markdown")
        {
            OpusInput,
            Content,
            WithContent,
            WithoutContent,
            WorkDir,
            Cookie,
            UserAgent,
            Debug
        };

        command.SetAction(async parseResult => await action(new DownloadRequest
        {
            Url = parseResult.GetValue(OpusInput) ?? "",
            Content = ContentSelector.Resolve(
                parseResult.GetValue(Content) ?? [],
                parseResult.GetValue(WithContent) ?? [],
                parseResult.GetValue(WithoutContent) ?? [],
                false, false, false, false, out _),
            WorkDir = parseResult.GetValue(WorkDir) ?? "",
            Cookie = parseResult.GetValue(Cookie) ?? "",
            UserAgent = parseResult.GetValue(UserAgent) ?? "",
            Debug = parseResult.GetValue(Debug)!
        }));

        return command;
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
