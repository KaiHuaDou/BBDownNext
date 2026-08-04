using System;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.Threading.Tasks;

using BBDown.Core;

namespace BBDown;

internal static class CommandLineInvoker
{
    private static readonly Argument<string> Url = new("url") { Description = "视频地址 或 av|bv|BV|ep|ss" };
    private static readonly Option<bool> UseTvApi = new("--tv-api", ["-tv"]) { Description = "使用 TV 端解析模式" };
    private static readonly Option<bool> UseAppApi = new("--app-api", ["-app"]) { Description = "使用 APP 端解析模式" };
    private static readonly Option<bool> UseIntlApi = new("--intl-api", ["-intl"]) { Description = "使用国际版（东南亚视频）解析模式" };
    private static readonly Option<bool> UseMP4box = new("--mp4box", []) { Description = "使用 MP4Box 来混流" };
    private static readonly Option<string> EncodingPriority = new("--encoding-priority", ["-e"])
    {
        Description = """
        视频及音频编码的选择优先级，用逗号分隔。
        例：hevc,av1,avc,flac,eac3,m4a
        """
    };
    private static readonly Option<string> DfnPriority = new("--dfn-priority", ["-q"])
    {
        Description = """
        画质优先级，用逗号分隔。
        例：8K 超高清, 1080P 高码率, HDR 真彩, 杜比视界
        """
    };
    private static readonly Option<bool> OnlyShowInfo = new("--show-info", ["-info"]) { Description = "仅解析而不进行下载" };
    private static readonly Option<bool> HideStreams = new("--hide-streams", ["-hs"]) { Description = "不要显示所有可用音视频流" };
    private static readonly Option<bool> Interactive = new("--interactive", ["-ia"]) { Description = "交互式选择清晰度" };
    private static readonly Option<bool> ShowAll = new("--all", []) { Description = "展示所有分 P 标题" };
    private static readonly Option<bool> UseAria2c = new("--aria2c", ["-aria2"]) { Description = "调用 aria2c 进行下载（你需要自行准备可执行文件）" };
    private static readonly Option<string> Aria2cArgs = new("--aria2c-args", [])
    {
        Description = """
        调用 aria2c 的附加参数（含空格的参数用引号包裹）。
        默认参数包含 "-x16 -s16 -j16 -k 5M"
        """
    };
    private static readonly Option<bool> SingleThread = new("--single-thread", ["-st"])
    {
        Description = "使用单线程下载，用于不支持 Range 的服务器。"
    };
    private static readonly Option<string> SelectPage = new("--select-page", ["-p"])
    {
        Description = """
        选择分 P。
          all              全部
          8                单集
          1,2,5            逗号列表
          3-5              闭区间（含两端：3,4,5）；3-3 仅第 3 集
          16-              开区间，到末集
          -22              开区间，从首集到 22
          1,6-10,15-latest 混合写法
          latest / new     最后一集（最新一集）
          last / LAST      倒数第二集
        关键字大小写不敏感；越界数字自动夹紧到有效边界；非法项忽略并提醒。
        """
    };
    private static readonly Option<bool> NoMetadata = new("--no-metadata", []) { Description = "精简混流，不增加描述、作者等信息" };
    private static readonly Option<bool> AudioOnly = new("--audio-only", ["-a"]) { Description = "仅下载音频" };
    private static readonly Option<bool> VideoOnly = new("--video-only", ["-v"]) { Description = "仅下载视频" };
    private static readonly Option<bool> DanmakuOnly = new("--danmaku-only", ["-d"]) { Description = "仅下载弹幕" };
    private static readonly Option<bool> CoverOnly = new("--cover-only", ["-c"]) { Description = "仅下载封面" };
    private static readonly Option<bool> SubOnly = new("--sub-only", ["-s"]) { Description = "仅下载字幕" };
    private static readonly Option<bool> Debug = new("--debug", []) { Description = "输出调试日志" };
    private static readonly Option<bool> SkipMux = new("--skip-mux", []) { Description = "跳过混流步骤" };
    private static readonly Option<bool> NoSub = new("--no-sub", []) { Description = "跳过字幕下载" };
    private static readonly Option<bool> NoCover = new("--no-cover", []) { Description = "跳过封面下载" };
    private static readonly Option<bool> NoForceHttp = new("--no-force-http", []) { Description = "下载音视频时避免降级为 HTTP" };
    private static readonly Option<bool> DownloadDanmaku = new("--danmaku", ["-dd"]) { Description = "下载弹幕" };
    private static readonly Option<string> DownloadDanmakuFormats = new("--danmaku-formats", ["-ddf"])
    {
        Description = "指定需下载的弹幕格式，逗号分隔",
        DefaultValueFactory = _ => "xml,ass"
    };
    private static readonly Option<bool> AllowAi = new("--allow-ai", []) { Description = "下载 AI 字幕" };
    private static readonly Option<bool> VideoAscending = new("--video-ascending", []) { Description = "视频升序（最小体积优先）" };
    private static readonly Option<bool> AudioAscending = new("--audio-ascending", []) { Description = "音频升序（最小体积优先）" };
    private static readonly Option<bool> AllowPcdn = new("--allow-pcdn", []) { Description = "不替换 PCDN 域名，仅在正常情况与 --upos-host 均无法下载时使用" };
    private static readonly Option<string> Lang = new("--lang", []) { Description = "设置混流的音频语言（代码），如 chi, jpn 等" };
    private static readonly Option<string> UserAgent = new("--user-agent", ["-ua"]) { Description = "指定 user-agent，否则使用随机 user-agent" };
    private static readonly Option<string> Cookie = new("--cookie", []) { Description = "设置字符串 cookie 用以下载网页接口的会员内容" };
    private static readonly Option<string> AccessToken = new("--access-token", ["-token"]) { Description = "设置 access_token 用以下载 TV/APP 接口的会员内容" };
    private static readonly Option<string> WorkDir = new("--work-dir", []) { Description = "设置程序的工作目录" };
    private static readonly Option<string> FFmpegPath = new("--ffmpeg-path", []) { Description = "设置 FFmpeg 的路径" };
    private static readonly Option<string> Mp4boxPath = new("--mp4box-path", []) { Description = "设置 MP4Box 的路径" };
    private static readonly Option<string> Aria2cPath = new("--aria2c-path", []) { Description = "设置 aria2c 的路径" };
    private static readonly Option<string> UposHost = new("--upos-host", []) { Description = "自定义 upos 服务器" };
    private static readonly Option<bool> NoForceHost = new("--no-force-host", []) { Description = "不强制替换下载服务器 host" };
    private static readonly Option<bool> SaveRecords = new("--save-records", []) { Description = "将下载过的视频记录到本地文件中，用于后续跳过下载同个视频" };
    private static readonly Option<bool> StopOnError = new("--stop-on-error", [])
    {
        Description = """
        遇到分 P 下载失败时立即停止，而不是继续下载其余分 P。
        默认继续，并在末尾汇总失败的分 P 后非零退出。
        """
    };
    private static readonly Option<string> DelayPerPage = new("--delay-per-page", []) { Description = "设置下载合集分 P 之间的下载间隔时间（单位：秒）", DefaultValueFactory = _ => "0" };
    private static readonly Option<string> FilePattern = new("--file-pattern", ["-F"])
    {
        Description = $"""
        使用内置变量自定义单 P 存储文件名：

        <videoTitle>：视频主标题
        <pageNumber>：视频分 P 序号
        <pageNumberWithZero>：视频分 P 序号（前缀补零）
        <pageTitle>：视频分 P 标题
        <bvid>：视频 BV 号
        <aid>：视频 aid
        <cid>：视频 cid
        <dfn>：视频清晰度
        <res>：视频分辨率
        <fps>：视频帧率
        <videoCodecs>：视频编码
        <videoBandwidth>：视频码率
        <audioCodecs>：音频编码
        <audioBandwidth>：音频码率
        <ownerName>：上传者名称
        <ownerMid>：上传者 mid
        <publishDate>：收藏夹/番剧/合集发布时间
        <videoDate>：视频发布时间（分 P 视频发布时间与 <publishDate> 相同）
        <apiType>：API 类型（TV/APP/INTL/WEB）

        默认为：{Program.SinglePageDefaultSavePath}
        """
    };
    private static readonly Option<string> MultiFilePattern = new("--multi-file-pattern", ["-M"])
    {
        Description = $"""
        使用内置变量自定义多 P 存储文件名：

        默认为：{Program.MultiPageDefaultSavePath}
        """
    };
    private static readonly Option<string> Host = new("--host", [])
    {
        Description = """
        指定 BiliPlus host。
        使用 BiliPlus 需要 access_token，无需 cookie。
        解析服务器能够获取你账号的大部分权限，请谨慎使用！
        """,
        DefaultValueFactory = _ => BiliApi.MainHost
    };
    private static readonly Option<string> EpHost = new("--ep-host", [])
    {
        Description = """
        指定 BiliPlus EP host。
        用于代理 api.bilibili.com/pgc/view/web/season
        大部分解析服务器不支持代理该接口
        """,
        DefaultValueFactory = _ => BiliApi.MainHost
    };
    private static readonly Option<string> TvHost = new("--tv-host", [])
    {
        Description = "自定义 TV 端接口请求 Host",
        DefaultValueFactory = _ => BiliApi.TvHost
    };
    private static readonly Option<string> Area = new("--area", []) { Description = "（hk|tw|th）使用 BiliPlus 时指定 BiliPlus area" };
    private static readonly Option<string> ConfigFile = new("--config", [])
    {
        Description = "读取指定的 BBDown 本地配置文件",
        DefaultValueFactory = _ => "BBDown.config"
    };

    public static RootCommand GetRootCommand(Func<DownloadOptions, Task<int>> action)
    {
        var rootCommand = new RootCommand
        {
            Url,
            UseTvApi,
            UseAppApi,
            UseIntlApi,
            UseMP4box,
            EncodingPriority,
            DfnPriority,
            OnlyShowInfo,
            ShowAll,
            UseAria2c,
            Interactive,
            HideStreams,
            SingleThread,
            VideoOnly,
            AudioOnly,
            DanmakuOnly,
            SubOnly,
            CoverOnly,
            Debug,
            SkipMux,
            NoMetadata,
            NoSub,
            NoCover,
            NoForceHttp,
            DownloadDanmaku,
            DownloadDanmakuFormats,
            AllowAi,
            VideoAscending,
            AudioAscending,
            AllowPcdn,
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
            var option = new DownloadOptions
            {
                UseTvApi = parseResult.GetValue(UseTvApi)!,
                Url = parseResult.GetValue(Url) ?? "",
                UseAppApi = parseResult.GetValue(UseAppApi)!,
                UseIntlApi = parseResult.GetValue(UseIntlApi)!,
                UseMP4box = parseResult.GetValue(UseMP4box)!,
                EncodingPriority = parseResult.GetValue(EncodingPriority) ?? "",
                DfnPriority = parseResult.GetValue(DfnPriority) ?? "",
                EncodingFirst = ResolveEncodingFirst(parseResult),
                OnlyShowInfo = parseResult.GetValue(OnlyShowInfo)!,
                ShowAll = parseResult.GetValue(ShowAll)!,
                UseAria2c = parseResult.GetValue(UseAria2c)!,
                Interactive = parseResult.GetValue(Interactive)!,
                HideStreams = parseResult.GetValue(HideStreams)!,
                SingleThread = parseResult.GetValue(SingleThread)!,
                NoMetadata = parseResult.GetValue(NoMetadata)!,
                VideoOnly = parseResult.GetValue(VideoOnly)!,
                AudioOnly = parseResult.GetValue(AudioOnly)!,
                DanmakuOnly = parseResult.GetValue(DanmakuOnly)!,
                CoverOnly = parseResult.GetValue(CoverOnly)!,
                SubOnly = parseResult.GetValue(SubOnly)!,
                Debug = parseResult.GetValue(Debug)!,
                SkipMux = parseResult.GetValue(SkipMux)!,
                NoSub = parseResult.GetValue(NoSub)!,
                NoCover = parseResult.GetValue(NoCover)!,
                NoForceHttp = parseResult.GetValue(NoForceHttp)!,
                DownloadDanmaku = parseResult.GetValue(DownloadDanmaku)!,
                DownloadDanmakuFormats = parseResult.GetValue(DownloadDanmakuFormats) ?? "",
                AllowAi = parseResult.GetValue(AllowAi)!,
                VideoAscending = parseResult.GetValue(VideoAscending)!,
                AudioAscending = parseResult.GetValue(AudioAscending)!,
                AllowPcdn = parseResult.GetValue(AllowPcdn)!,
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
