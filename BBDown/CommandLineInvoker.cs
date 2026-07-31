using System;
using System.CommandLine;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;


namespace BBDown;

internal static class CommandLineInvoker
{
    private static readonly Argument<string> Url = new("url") { Description = "视频地址 或 av|bv|BV|ep|ss" };
    private static readonly Option<bool> UseTvApi = new("--use-tv-api", ["-tv"]) { Description = "使用TV端解析模式" };
    private static readonly Option<bool> UseAppApi = new("--use-app-api", ["-app"]) { Description = "使用APP端解析模式" };
    private static readonly Option<bool> UseIntlApi = new("--use-intl-api", ["-intl"]) { Description = "使用国际版(东南亚视频)解析模式" };
    private static readonly Option<bool> UseMP4box = new("--use-mp4box", []) { Description = "使用MP4Box来混流" };
    private static readonly Option<string> EncodingPriority = new("--encoding-priority", ["-e"]) { Description = "视频及音频编码的选择优先级, 用逗号分割 例: \"hevc,av1,avc,flac,eac3,m4a\"" };
    private static readonly Option<string> DfnPriority = new("--dfn-priority", ["-q"]) { Description = "画质优先级,用逗号分隔 例: \"8K 超高清, 1080P 高码率, HDR 真彩, 杜比视界\"" };
    private static readonly Option<bool> OnlyShowInfo = new("--only-show-info", ["-info"]) { Description = "仅解析而不进行下载" };
    private static readonly Option<bool> HideStreams = new("--hide-streams", ["-hs"]) { Description = "不要显示所有可用音视频流" };
    private static readonly Option<bool> Interactive = new("--interactive", ["-ia"]) { Description = "交互式选择清晰度" };
    private static readonly Option<bool> ShowAll = new("--show-all", []) { Description = "展示所有分P标题" };
    private static readonly Option<bool> UseAria2c = new("--use-aria2c", ["-aria2"]) { Description = "调用aria2c进行下载(你需要自行准备好二进制可执行文件)" };
    private static readonly Option<string> Aria2cArgs = new("--aria2c-args", []) { Description = "调用aria2c的附加参数(默认参数包含\"-x16 -s16 -j16 -k 5M\", 使用时注意字符串转义)" };
    private static readonly Option<bool> MultiThread = new("--multi-thread", ["-mt"]) { Description = "使用多线程下载(默认开启)" };
    private static readonly Option<string> SelectPage = new("--select-page", ["-p"]) { Description = "选择指定分p或分p范围: (-p 8 或 -p 1,2 或 -p 3-5 或 -p ALL 或 -p LAST 或 -p 3,5,LATEST)" };
    private static readonly Option<bool> SimplyMux = new("--simply-mux", []) { Description = "精简混流，不增加描述、作者等信息" };
    private static readonly Option<bool> AudioOnly = new("--audio-only", []) { Description = "仅下载音频" };
    private static readonly Option<bool> VideoOnly = new("--video-only", []) { Description = "仅下载视频" };
    private static readonly Option<bool> DanmakuOnly = new("--danmaku-only", []) { Description = "仅下载弹幕" };
    private static readonly Option<bool> CoverOnly = new("--cover-only", []) { Description = "仅下载封面" };
    private static readonly Option<bool> SubOnly = new("--sub-only", []) { Description = "仅下载字幕" };
    private static readonly Option<bool> Debug = new("--debug", []) { Description = "输出调试日志" };
    private static readonly Option<bool> SkipMux = new("--skip-mux", []) { Description = "跳过混流步骤" };
    private static readonly Option<bool> SkipSubtitle = new("--skip-subtitle", []) { Description = "跳过字幕下载" };
    private static readonly Option<bool> SkipCover = new("--skip-cover", []) { Description = "跳过封面下载" };
    private static readonly Option<bool> ForceHttp = new("--force-http", []) { Description = "下载音视频时强制使用HTTP协议替换HTTPS(默认开启)" };
    private static readonly Option<bool> DownloadDanmaku = new("--download-danmaku", ["-dd"]) { Description = "下载弹幕" };
    private static readonly Option<string> DownloadDanmakuFormats = new("--download-danmaku-formats", ["-ddf"]) { Description = $"指定需下载的弹幕格式, 用逗号分隔, 可选 {string.Join('/', BBDownDanmakuFormatInfo.AllFormatNames)}, 默认: \"{string.Join(',', BBDownDanmakuFormatInfo.AllFormatNames)}\"" };
    private static readonly Option<bool> SkipAi = new("--skip-ai", []) { Description = "跳过AI字幕下载(默认开启)" };
    private static readonly Option<bool> VideoAscending = new("--video-ascending", []) { Description = "视频升序(最小体积优先)" };
    private static readonly Option<bool> AudioAscending = new("--audio-ascending", []) { Description = "音频升序(最小体积优先)" };
    private static readonly Option<bool> AllowPcdn = new("--allow-pcdn", []) { Description = "不替换PCDN域名, 仅在正常情况与--upos-host均无法下载时使用" };
    private static readonly Option<string> Language = new("--language", []) { Description = "设置混流的音频语言(代码), 如chi, jpn等" };
    private static readonly Option<string> UserAgent = new("--user-agent", ["-ua"]) { Description = "指定user-agent, 否则使用随机user-agent" };
    private static readonly Option<string> Cookie = new("--cookie", ["-c"]) { Description = "设置字符串cookie用以下载网页接口的会员内容" };
    private static readonly Option<string> AccessToken = new("--access-token", ["-token"]) { Description = "设置access_token用以下载TV/APP接口的会员内容" };
    private static readonly Option<string> WorkDir = new("--work-dir", []) { Description = "设置程序的工作目录" };
    private static readonly Option<string> FFmpegPath = new("--ffmpeg-path", []) { Description = "设置ffmpeg的路径" };
    private static readonly Option<string> Mp4boxPath = new("--mp4box-path", []) { Description = "设置mp4box的路径" };
    private static readonly Option<string> Aria2cPath = new("--aria2c-path", []) { Description = "设置aria2c的路径" };
    private static readonly Option<string> UposHost = new("--upos-host", []) { Description = "自定义upos服务器" };
    private static readonly Option<bool> ForceReplaceHost = new("--force-replace-host", []) { Description = "强制替换下载服务器host(默认开启)" };
    private static readonly Option<bool> SaveArchivesToFile = new("--save-archives-to-file", []) { Description = "将下载过的视频记录到本地文件中, 用于后续跳过下载同个视频" };
    private static readonly Option<string> DelayPerPage = new("--delay-per-page", []) { Description = "设置下载合集分P之间的下载间隔时间(单位: 秒, 默认无间隔)" };
    private static readonly Option<string> FilePattern = new("--file-pattern", ["-F"])
    {
        Description = $"使用内置变量自定义单P存储文件名:\r\n\r\n" +
        $"<videoTitle>: 视频主标题\r\n" +
        $"<pageNumber>: 视频分P序号\r\n" +
        $"<pageNumberWithZero>: 视频分P序号(前缀补零)\r\n" +
        $"<pageTitle>: 视频分P标题\r\n" +
        $"<bvid>: 视频BV号\r\n" +
        $"<aid>: 视频aid\r\n" +
        $"<cid>: 视频cid\r\n" +
        $"<dfn>: 视频清晰度\r\n" +
        $"<res>: 视频分辨率\r\n" +
        $"<fps>: 视频帧率\r\n" +
        $"<videoCodecs>: 视频编码\r\n" +
        $"<videoBandwidth>: 视频码率\r\n" +
        $"<audioCodecs>: 音频编码\r\n" +
        $"<audioBandwidth>: 音频码率\r\n" +
        $"<ownerName>: 上传者名称\r\n" +
        $"<ownerMid>: 上传者mid\r\n" +
        $"<publishDate>: 收藏夹/番剧/合集发布时间\r\n" +
        $"<videoDate>: 视频发布时间(分p视频发布时间与<publishDate>相同)\r\n" +
        $"<apiType>: API类型(TV/APP/INTL/WEB)\r\n\r\n" +
        $"默认为: {Program.SinglePageDefaultSavePath}\r\n"
    };
    private static readonly Option<string> MultiFilePattern = new("--multi-file-pattern", ["-M"]) { Description = $"使用内置变量自定义多P存储文件名:\r\n\r\n默认为: {Program.MultiPageDefaultSavePath}\r\n" };
    private static readonly Option<string> Host = new("--host", []) { Description = "指定BiliPlus host(使用BiliPlus需要access_token, 不需要cookie, 解析服务器能够获取你账号的大部分权限!)" };
    private static readonly Option<string> EpHost = new("--ep-host", []) { Description = "指定BiliPlus EP host(用于代理api.bilibili.com/pgc/view/web/season, 大部分解析服务器不支持代理该接口)" };
    private static readonly Option<string> TvHost = new("--tv-host", []) { Description = "自定义tv端接口请求Host(用于代理api.snm0516.aisee.tv)" };
    private static readonly Option<string> Area = new("--area", []) { Description = "(hk|tw|th) 使用BiliPlus时必选, 指定BiliPlus area" };
    private static readonly Option<string> ConfigFile = new("--config-file", []) { Description = "读取指定的BBDown本地配置文件(默认为: BBDown.config)" };//以下仅为兼容旧版本命令行, 不建议使用

    public static RootCommand GetRootCommand(Func<MyOption, Task> action)
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
            MultiThread,
            VideoOnly,
            AudioOnly,
            DanmakuOnly,
            SubOnly,
            CoverOnly,
            Debug,
            SkipMux,
            SkipSubtitle,
            SkipCover,
            ForceHttp,
            DownloadDanmaku,
            DownloadDanmakuFormats,
            SkipAi,
            VideoAscending,
            AudioAscending,
            AllowPcdn,
            FilePattern,
            MultiFilePattern,
            SelectPage,
            Language,
            UserAgent,
            Cookie,
            AccessToken,
            Aria2cArgs,
            WorkDir,
            FFmpegPath,
            Mp4boxPath,
            Aria2cPath,
            UposHost,
            ForceReplaceHost,
            SaveArchivesToFile,
            DelayPerPage,
            Host,
            EpHost,
            TvHost,
            Area,
            ConfigFile
        };

        rootCommand.SetAction(async parseResult =>
        {
            var option = new MyOption
            {
                UseTvApi = parseResult.GetValue(UseTvApi)!,
                Url = parseResult.GetValue(Url) ?? "",
                UseAppApi = parseResult.GetValue(UseAppApi)!,
                UseIntlApi = parseResult.GetValue(UseIntlApi)!,
                UseMP4box = parseResult.GetValue(UseMP4box)!,
                EncodingPriority = parseResult.GetValue(EncodingPriority) ?? "",
                DfnPriority = parseResult.GetValue(DfnPriority) ?? "",
                OnlyShowInfo = parseResult.GetValue(OnlyShowInfo)!,
                ShowAll = parseResult.GetValue(ShowAll)!,
                UseAria2c = parseResult.GetValue(UseAria2c)!,
                Interactive = parseResult.GetValue(Interactive)!,
                HideStreams = parseResult.GetValue(HideStreams)!,
                MultiThread = parseResult.GetValue(MultiThread)!,
                SimplyMux = parseResult.GetValue(SimplyMux)!,
                VideoOnly = parseResult.GetValue(VideoOnly)!,
                AudioOnly = parseResult.GetValue(AudioOnly)!,
                DanmakuOnly = parseResult.GetValue(DanmakuOnly)!,
                CoverOnly = parseResult.GetValue(CoverOnly)!,
                SubOnly = parseResult.GetValue(SubOnly)!,
                Debug = parseResult.GetValue(Debug)!,
                SkipMux = parseResult.GetValue(SkipMux)!,
                SkipSubtitle = parseResult.GetValue(SkipSubtitle)!,
                SkipCover = parseResult.GetValue(SkipCover)!,
                ForceHttp = parseResult.GetValue(ForceHttp)!,
                DownloadDanmaku = parseResult.GetValue(DownloadDanmaku)!,
                DownloadDanmakuFormats = parseResult.GetValue(DownloadDanmakuFormats) ?? "",
                SkipAi = parseResult.GetValue(SkipAi)!,
                VideoAscending = parseResult.GetValue(VideoAscending)!,
                AudioAscending = parseResult.GetValue(AudioAscending)!,
                AllowPcdn = parseResult.GetValue(AllowPcdn)!,
                FilePattern = parseResult.GetValue(FilePattern) ?? "",
                MultiFilePattern = parseResult.GetValue(MultiFilePattern) ?? "",
                SelectPage = parseResult.GetValue(SelectPage) ?? "",
                Language = parseResult.GetValue(Language) ?? "",
                UserAgent = parseResult.GetValue(UserAgent) ?? "",
                Cookie = parseResult.GetValue(Cookie) ?? "",
                AccessToken = parseResult.GetValue(AccessToken) ?? "",
                Aria2cArgs = parseResult.GetValue(Aria2cArgs) ?? "",
                WorkDir = parseResult.GetValue(WorkDir) ?? "",
                FFmpegPath = parseResult.GetValue(FFmpegPath) ?? "",
                Mp4boxPath = parseResult.GetValue(Mp4boxPath) ?? "",
                Aria2cPath = parseResult.GetValue(Aria2cPath) ?? "",
                UposHost = parseResult.GetValue(UposHost) ?? "",
                ForceReplaceHost = parseResult.GetValue(ForceReplaceHost)!,
                SaveArchivesToFile = parseResult.GetValue(SaveArchivesToFile)!,
                DelayPerPage = parseResult.GetValue(DelayPerPage) ?? "",
                Host = parseResult.GetValue(Host) ?? "",
                EpHost = parseResult.GetValue(EpHost) ?? "",
                TvHost = parseResult.GetValue(TvHost) ?? "",
                Area = parseResult.GetValue(Area) ?? "",
                ConfigFile = parseResult.GetValue(ConfigFile) ?? ""
            };
            await action(option);
        });

        return rootCommand;
    }
}