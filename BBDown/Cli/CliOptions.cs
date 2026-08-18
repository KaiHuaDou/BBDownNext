using System.CommandLine;

using BBDown.Core;
using BBDown.Core.Download;

namespace BBDown.Cli;

/// <summary>全部 CLI 选项与别名的静态定义，供根命令注册。
/// 按 README「参数说明」的分组排列，注册顺序即 --help 显示顺序。</summary>
internal static class CliOptions
{
    internal static readonly Argument<string> Url = new("url") { Description = "视频地址 或 av|bv|BV|ep|ss，也可传直播间地址进行录制" };

    // 解析模式
    // 单值枚举：非法值进 parseResult.Errors 报错退出（serve 侧为 JSON 契约，非法值回落 web，见 ServeRequestOptions）
    internal static readonly Option<ApiType> ApiOption = new("--api", ["-a"])
    {
        Description = "使用指定 API 解析通道：web / tv / app / intl，默认 web，忽略大小写",
        DefaultValueFactory = _ => ApiType.Web,
        CustomParser = result =>
        {
            var tokens = result.Tokens;
            var api = ApiTypeUtil.TryParse(tokens.Count == 0 ? null : tokens[^1].Value);
            if (api is null)
            {
                result.AddError("无效的 --api 值（可选 web / tv / app / intl，忽略大小写）");
                return ApiType.Web;
            }

            return api.Value;
        }
    };
    internal static readonly Option<string> Host = new("--host", [])
    {
        Description = """
        指定 BiliPlus host。
        使用 BiliPlus 需要 access_token，无需 cookie。
        解析服务器能够获取你账号的大部分权限，请谨慎使用！
        """,
        DefaultValueFactory = _ => BiliApi.MainHost
    };
    internal static readonly Option<string> EpHost = new("--ep-host", [])
    {
        Description = """
        指定 BiliPlus EP host。
        用于代理 api.bilibili.com/pgc/view/web/season
        大部分解析服务器不支持代理该接口
        """,
        DefaultValueFactory = _ => BiliApi.MainHost
    };
    internal static readonly Option<string> TvHost = new("--tv-host", [])
    {
        Description = "自定义 TV 端接口请求 Host",
        DefaultValueFactory = _ => BiliApi.TvHost
    };
    internal static readonly Option<string> Area = new("--area", []) { Description = "（hk|tw|th）使用 BiliPlus 时指定 BiliPlus area" };

    // 清晰度与编码
    internal static readonly Option<string> EncodingPriority = new("--encoding-priority", ["-e"])
    {
        Description = """
        视频及音频编码的选择优先级，用逗号分隔。
        例：hevc,av1,avc,flac,eac3,m4a
        """
    };
    internal static readonly Option<string> DfnPriority = new("--dfn-priority", ["-q"])
    {
        Description = """
        画质优先级，用逗号分隔。
        例：8K 超高清, 1080P 高码率, HDR 真彩, 杜比视界
        """
    };
    internal static readonly Option<string> AudioQuality = new("--audio-quality", ["-aq"])
    {
        Description = """
        音频音质优先级，用逗号分隔。
        例：杜比全景声, Hi-Res 无损, 192K
        也支持音质 id，例：30250, 30251, 30280
        """
    };
    internal static readonly Option<bool> VideoAscending = new("--video-ascending", ["-va"]) { Description = "视频升序（最小体积优先）" };
    internal static readonly Option<bool> AudioAscending = new("--audio-ascending", ["-aa"]) { Description = "音频升序（最小体积优先）" };
    internal static readonly Option<bool> InteractiveQuality = new("--interactive-quality", ["-iaq"]) { Description = "交互式选择清晰度" };
    internal static readonly Option<bool> HideStreams = new("--hide-streams", ["-hs"]) { Description = "不要显示所有可用音视频流" };
    internal static readonly Option<bool> InfoOnly = new("--info-only", ["-i"]) { Description = "仅解析而不进行下载" };
    internal static readonly Option<bool> ShowAll = new("--all", []) { Description = "展示所有分 P 标题" };

    // 下载内容
    // 内容集：string[] 默认 Arity ZeroOrMore，多个 --get/--with/--without 自动累积合并
    internal static readonly Option<string[]> Content = new("--get", ["-g"])
    {
        Description = """
        设置下载内容，内容字符：
          a：音频
          v：视频
          c：独立封面文件
          C：封面嵌入
          d：弹幕
          i：专栏图片
          m：嵌入元数据
          M：YAML front matter（专栏）
          o：评论
          O：全部评论（含楼中楼全部回复）
          S：AI 字幕
          s：字幕
        用 --with 追加、--without 移除
        """,
        DefaultValueFactory = _ => [ContentSelector.Default]
    };
    internal static readonly Option<string[]> WithContent = new("--with", ["-w"])
    {
        Description = "在 --get 基础上追加下载内容"
    };
    internal static readonly Option<string[]> WithoutContent = new("--without", ["-W"])
    {
        Description = "在 --get 与 --with 基础上移除下载内容"
    };
    internal static readonly Option<string> DownloadDanmakuFormats = new("--danmaku-formats", ["-ddf"])
    {
        Description = "指定需下载的弹幕格式，逗号分隔",
        DefaultValueFactory = _ => "xml,ass"
    };
    // 不设 ArgumentArity.ZeroOrOne：url 是位置参数，可选值会让 `bbdown --comments-count BV1xx` 把 BV 号吃成选项的值
    internal static readonly Option<int> CommentsCount = new("--comments-count", ["-cn"])
    {
        Description = "下载评论区前 N 条评论，默认 0（不下载）"
    };
    internal static readonly Option<string> CommentsSort = new("--comments-sort", ["-cs"])
    {
        Description = "评论排序：hot（热度）或 time（最新）",
        DefaultValueFactory = _ => "hot"
    };
    internal static readonly Option<string> CommentsFormats = new("--comments-formats", ["-cf"])
    {
        Description = "指定需导出的评论格式，逗号分隔",
        DefaultValueFactory = _ => "json,txt"
    };
    // 单值枚举：非法值进 parseResult.Errors 报错退出（serve 侧为 JSON 契约，非法值回落 web，见 ServeRequestOptions）
    internal static readonly Option<MuxMode> MuxOption = new("--mux", ["-m"])
    {
        Description = """
        none 不混流
        mpeg4 使用 FFmpeg 混流为 MP4
        mp4box 使用 MP4Box 混流
        mkv 使用 FFmpeg 混流为 Matrosk
        （视频扩展名 .mp4/.mkv / 纯音频扩展名 .m4a/.mka）
        忽略大小写
        """,
        DefaultValueFactory = _ => MuxMode.Mpeg4,
        CustomParser = result =>
        {
            var tokens = result.Tokens;
            var mux = MuxModeUtil.TryParse(tokens.Count == 0 ? null : tokens[^1].Value);
            if (mux is null)
            {
                result.AddError("无效的 --mux 值（可选 none / mpeg4 / mp4box / mkv，忽略大小写）");
                return MuxMode.Mpeg4;
            }

            return mux.Value;
        }
    };
    internal static readonly Option<string> PostProcess = new("--post-process")
    {
        Description = """
        指定外部后处理进程（可执行文件路径）。
        下载完成后带特殊标记的轨道文件会交给该进程处理，成功产物替换原文件参与混流；
        进程不可用或处理失败时静默保留原文件。处理方自行获取所需信息，本程序不感知其语义。
        """
    };
    internal static readonly Option<bool> AllowPreview = new("--allow-preview", ["-P"]) { Description = "允许下载充电专属视频的试看片段，输出文件名带 [试看] 前缀" };
    internal static readonly Option<string> Lang = new("--lang", ["-L"]) { Description = "设置混流的音频语言（代码），如 chi, jpn 等" };

    // 直播录制
    internal static readonly Option<int> LiveQualityOption = new("--live-quality", ["-lq"])
    {
        Description = """
        直播录制清晰度：10000 原画、400 蓝光、250 超清、150 高清、80 流畅；
        未登录时服务端通常只给到 250
        """,
        DefaultValueFactory = _ => BBDown.Core.Download.LiveQuality.Original
    };

    // 下载方式与性能
    internal static readonly Option<bool> UseAria2c = new("--aria2c", ["-aria2"]) { Description = "调用 aria2c 进行下载（你需要自行准备可执行文件）" };
    internal static readonly Option<string> Aria2cArgs = new("--aria2c-args", [])
    {
        Description = """
        调用 aria2c 的附加参数（含空格的参数用引号包裹）。
        默认参数包含 "-x16 -s16 -j16 -k 5M"
        """
    };
    internal static readonly Option<bool> SingleThread = new("--single-thread", ["-st"])
    {
        Description = "使用单线程下载，用于不支持 Range 的服务器。"
    };
    internal static readonly Option<string> DelayPerPage = new("--delay-per-page", []) { Description = "设置下载合集分 P 之间的下载间隔时间（单位：秒）", DefaultValueFactory = _ => "0" };
    internal static readonly Option<string> UposHost = new("--upos-host", []) { Description = "自定义 upos 服务器" };
    internal static readonly Option<bool> NoForceHost = new("--no-force-host", []) { Description = "不强制替换下载服务器 host" };
    internal static readonly Option<bool> AllowPcdn = new("--allow-pcdn", []) { Description = "不替换 PCDN 域名，仅在正常情况与 --upos-host 均无法下载时使用" };
    internal static readonly Option<bool> NoForceHttp = new("--no-force-http", []) { Description = "下载音视频时避免降级为 HTTP" };

    // 账号与凭据
    internal static readonly Option<string> Cookie = new("--cookie", ["-C"]) { Description = "设置字符串 cookie 用以下载网页接口的会员内容" };
    internal static readonly Option<string> AccessToken = new("--access-token", ["-token"]) { Description = "设置 access_token 用以下载 TV/APP 接口的会员内容" };
    internal static readonly Option<string> UserAgent = new("--user-agent", ["-ua"]) { Description = "指定 user-agent，否则使用随机 user-agent" };

    // 文件、路径与调试
    internal static readonly Option<string> FilePattern = new("--file-pattern", ["-F"])
    {
        Description = """
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
        """,
        DefaultValueFactory = _ => SavePath.SinglePageDefaultSavePath
    };
    internal static readonly Option<string> MultiFilePattern = new("--multi-file-pattern", ["-M"])
    {
        Description = "使用内置变量自定义多 P 存储文件名：",
        DefaultValueFactory = _ => SavePath.MultiPageDefaultSavePath
    };
    internal static readonly Option<string> Pages = new("--pages", ["-p"])
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
    internal static readonly Option<bool> InteractivePages = new("--interactive-pages", ["-iap"]) { Description = "逐集确认是否下载：[y] 要，[n] 不要，[a] 剩余全部要，[q] 剩余全部不要，回车=不要" };
    internal static readonly Option<string> WorkDir = new("--work-dir", ["-cwd"]) { Description = "设置程序的工作目录" };
    internal static readonly Option<string> FFmpegPath = new("--ffmpeg-path", []) { Description = "设置 FFmpeg 的路径" };
    internal static readonly Option<string> Mp4boxPath = new("--mp4box-path", []) { Description = "设置 MP4Box 的路径" };
    internal static readonly Option<string> Aria2cPath = new("--aria2c-path", []) { Description = "设置 aria2c 的路径" };
    internal static readonly Option<bool> SaveRecords = new("--save-records", []) { Description = "将下载过的视频记录到本地文件中，用于后续跳过下载同个视频" };
    internal static readonly Option<bool> StopOnError = new("--stop-on-error", [])
    {
        Description = """
        遇到分 P 下载失败时立即停止，而不是继续下载其余分 P。
        默认继续，并在末尾汇总失败的分 P 后非零退出。
        """
    };
    internal static readonly Option<string> ConfigFile = new("--config", ["-c"])
    {
        // 不设默认值：未显式指定时 ConfigParser 回退到程序目录下的 BBDown.config（README 约定），
        // 设了默认值会让该回退成为死分支，实际按进程 cwd 查找
        Description = "读取指定的 BBDown 本地配置文件"
    };
    internal static readonly Option<bool> Debug = new("--debug", ["-D"]) { Description = "输出调试日志" };
}
