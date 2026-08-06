# BBDown 架构说明

本文档描述 BBDown 的项目结构、模块职责、核心流程与技术设计，供二次开发、排错与贡献代码参考。所有描述均基于当前源码，涉及的具体类/文件以仓库实际为准。

---

## 1. 概览

BBDown 是一个基于 **.NET 9** 的哔哩哔哩视频下载 / 解析命令行工具，定位为单文件可执行体（`dotnet publish -p:PublishAot=true` 可产出 AOT 原生二进制）。整体被拆为两块：

- **`BBDown`**：入口可执行项目（SDK `Microsoft.NET.Sdk.Web`），负责命令行解析、登录、服务器模式、下载编排与混流。
- **`BBDown.Core`**：核心类库（`IsAotCompatible=true`），负责接口调用、解析、字幕、弹幕、BV 转换、HTTP 等可复用能力。

代码层面强制 **nullable enable**、**`TreatWarningsAsErrors=true`**、**集中式包版本管理**（`Directory.Packages.props`），并以 `System.Text.Json` **源生成器**（`JsonSerializerContext`）替代运行时反射，保证 AOT 裁剪安全。

---

## 2. 项目结构

```
BBDown/
├── BBDown/                 # 入口可执行项目 (Sdk.Web, PackAsTool)，命名空间 BBDown（根契约层 + 入口）
│   ├── Program.cs          # Main、子命令装配、serve 启动、全局取消、RunApp
│   ├── AppEnv.cs           # 进程级环境：AppDir / CancellationToken / Cancel()（切断底层对 Program 的反向依赖）
│   ├── DownloadRequest.cs  # 不可变运行时配置 record (含 WithSecretsRedacted)
│   ├── DownloadSession.cs  # 分 P 生命周期恒定入参 record
│   ├── WorkContext.cs      # 工作上下文 record
│   ├── PageContext.cs      # 分 P 上下文 record
│   ├── PageOutcome.cs      # 分 P 落盘结果 record（Media/Mux 共用，解除循环依赖）
│   ├── PipelineSink.cs     # 下载链路进度回吐回调（Meta / Saved / Sample，取代透传 serve 的 DownloadTask）
│   ├── ToolPaths.cs        # 外部工具路径不可变快照（ffmpeg / mp4box / aria2c）
│   ├── ChargedPreviewException.cs # 充电专属试看中止异常
│   ├── DanmakuFormat.cs    # 弹幕格式信息
│   ├── CommentFormat.cs    # 评论格式信息（与弹幕各自独立，解析逻辑不共用）
│   │
│   ├── Cli/                # 命名空间 BBDown.Cli — 命令行解析
│   │   ├── CommandLineInvoker.cs  # 全部 CLI 选项与别名 (GetRootCommand)
│   │   └── ConfigParser.cs        # 配置文件解析 (仅补齐命令行未指定项)
│   │
│   ├── Pipeline/           # 命名空间 BBDown.Pipeline — 下载编排主干（CLI 与 serve 共用）
│   │   ├── DownloadPipeline.cs     # RunAsync 三段下载主干（原 Program.RunDownloadAsync）
│   │   ├── WorkSetup.cs            # 进程级初始化 → WorkContext (Build)
│   │   ├── VideoInfo.cs            # FetchAsync 解析视频信息（标题/分P/封面/账号探测）
│   │   ├── PageQueue.cs            # 逐分 P 编排 (RunAsync)
│   │   ├── PageSelect.cs           # 分 P 选择/范围
│   │   ├── InputResolver.cs        # URL/编号 → 内部 avid 解析
│   │   ├── LiveDownload.cs         # 直播录制编排 (RunAsync)：独立链路，不走 WorkContext
│   │   └── OpusDownload.cs         # 专栏导出入口 (RunAsync)：在 RunApp 内于 WorkSetup.Build 之前分流，不经混流
│   │
│   ├── Live/               # 命名空间 BBDown.Live — 直播录制（独立链路，不依赖 WorkContext）
│   │   ├── LiveFileNaming.cs       # 分段/产物文件名（主播名-标题-时间戳）
│   │   ├── LiveMuxer.cs            # 分段 FLV → mp4 合并（avc/hevc bitstream filter 分派、+genpts）
│   │   ├── LiveProgress.cs         # 录制进度回吐
│   │   ├── LiveRecorder.cs        # 录制状态机（断流退避重连 / CDN failover / 编码锁定）
│   │   ├── LiveSegmentWriter.cs    # 单段 FLV 落盘
│   │   └── LiveSignal.cs           # Ctrl+Break 停录合并 / Ctrl+C 中断信号
│   │
│   ├── Media/              # 命名空间 BBDown.Media — 单分 P 下载与封装
│   │   ├── PageDownload.cs         # 单分 P 下载入口，分派 DASH/FLV (RunAsync / DispatchAsync)
│   │   ├── DashDownload.cs         # DASH 轨下载 (RunAsync)
│   │   ├── FlvDownload.cs          # FLV 分段下载与合并 (RunAsync)
│   │   ├── PageAssets.cs           # 封面/字幕准备、弹幕下载（`PrepareAsync` 现收窄接收 `DownloadSession`）
│   │   ├── CommentDownload.cs       # 评论区导出（按 --comment-formats 落盘 json/txt，挂 PageQueue）
│   │   └── TrackSelect.cs          # 轨道排序、信息打印、交互选轨
│   │
│   ├── Mux/                # 命名空间 BBDown.Mux — 混流与收尾
│   │   ├── MuxFinish.cs            # 混流收尾、跳过已存在、清理临时文件 (DASH/FLV 共用)
│   │   ├── Muxer.cs                # FFmpeg/MP4Box 混流、FLV 合并（混流入参统一为不可变 `MuxRequest` record）
│   │   └── ChapterMeta.cs          # 章节元数据
│   │
│   ├── Download/           # 命名空间 BBDown.Download — 下载传输层
│   │   ├── DownloadUtil.cs         # 唯一下载入口 (续传、CDN 策略)
│   │   ├── PartFile.cs             # 断点续传状态 (.bbdown.part/.bbdown.json)
│   │   ├── CdnHost.cs              # CDN host 策略
│   │   └── BBDownAria2c.cs         # Aria2c 下载
│   │
│   ├── Auth/               # 命名空间 BBDown.Auth — 登录与凭据
│   │   ├── Login.cs                # WEB/TV/APP 扫码登录与 refresh_token 续期
│   │   ├── Account.cs              # 账号探测
│   │   ├── AccountInfo.cs          # 账号信息
│   │   └── CredentialStore.cs      # 单一 JSON 凭据读写 (源生成器 AOT 安全)
│   │
│   ├── Serve/              # 命名空间 BBDown.Serve — serve 模式
│   │   ├── BBDownApiServer.cs      # ASP.NET Minimal API 任务增删查（Run / RunAsync / StartForTestAsync）
│   │   ├── DownloadTask.cs         # serve 任务状态/快照 record（DownloadStatus / DownloadTask / DownloadTaskSnapshot）
│   │   └── ServeRequestOptions.cs  # serve 请求受控子集 + CallBackWebHook
│   │
│   └── Util/               # 命名空间 BBDown.Util — 通用工具
│       ├── BBDownUtil.cs          # 通用工具 (Utils)
│       ├── ProgressBar.cs         # 进度条
│       ├── SavePath.cs            # 文件名/路径格式化
│       └── ArchiveLog.cs          # 归档记录
│
├── BBDown.Core/            # 核心类库 (library, IsAotCompatible)
│   ├── BiliApi.cs          # 各接口 Host/Path 常量
│   ├── Config.cs           # 清晰度档位 (Qualities/MaxQn/DolbyVisionQn)
│   ├── Parser.cs           # 播放地址解析入口：编排 ExtractTracksAsync + 番剧分段点映射（请求构造/发送、响应导航、轨道读取已下沉到 PlayUrl/）
│   ├── PlayUrl/            # 播放地址(playurl)解析：请求构造与发送、响应导航、四类轨道读取、跨端轨道装配
│   │   ├── PlayUrlRequest.cs     # 顶层 internal record struct（aidOri/aid/cid/epId 与 API 模式）
│   │   ├── PlayUrlClient.cs      # URL 构造 + 发送（WEB/TV/INTL/网页兜底）+ appkey 常量
│   │   ├── PlayUrlResponse.cs    # 响应形状导航（data/result/video_info 节点定位、大会员判定）
│   │   ├── DashTrackReader.cs    # 纯函数：DASH JSON → 视频/音频轨（免二压两次响应并集、杜比/Hi-Res 回退）
│   │   ├── FlvTrackReader.cs     # 纯函数：FLV 分段 → 轨道
│   │   ├── IntlTrackReader.cs    # 纯函数：INTL(BiliPlus) video_info → 轨道
│   │   ├── AppTrackReader.cs     # APP(gRPC) PlayViewReply → 轨道（FetchAsync 含 gRPC 调用）
│   │   └── TrackFactory.cs       # 跨端共享：baseUrl 选择 / codec 名 / Audio 构建
│   ├── AppHelper.cs        # APP gRPC 手写帧 (PackMessage/ReadMessage)
│   ├── IdPrefix.cs         # 输入编号前缀常量 (ep:/ss:/lists:/series:/fav:/cheese:/spaceMid: 等)
│   ├── Opus/               # 专栏导出：OpusInputResolver(输入解析) / OpusFetcher(抓取) / OpusHtmlToMarkdown(HTML→MD) / OpusMarkdownRenderer(渲染) / OpusImageUtil(图片) / OpusDocument(域模型)
│   ├── Fetcher/            # 7 个 static Fetcher + FetcherRegistry (按 IdPrefix 分发)
│   ├── Util/               # BV 转换、FileNameUtil(200 字节截断)、Buvid 等
│   ├── Entity/             # VInfo / Page / Video / Audio / ParsedResult 等
│   ├── APP/                # APP gRPC 协议 (proto 生成代码)
│   └── DanmakuUtil.cs      # 弹幕获取 (xml/ass)
│   ├── Comment/             # 评论区抓取与渲染（WBI 分页 / 楼中楼 / JSON·TXT 渲染）
│   ├── Live/                # 直播录制：LiveInputResolver(地址解析) / LiveFetcher(拉流地址) / LiveRoomInfo(房间信息 + 清晰度档位 LiveQuality)
│
├── BBDown.Tests/           # 针对 BBDown 的 xUnit 测试
└── BBDown.Core.Tests/      # 针对 BBDown.Core 的 xUnit 测试
```

**依赖方向**：`BBDown` → `BBDown.Core`；两个测试项目分别依赖对应实现。Core 不反向依赖入口项目，保证核心逻辑可独立测试。

**入口项目内部分层**：`BBDown` 主项目按职责细分为若干子命名空间（对应同名子文件夹），根命名空间 `BBDown` 仅保留两类类型——① 进程入口（`Program` / `AppEnv`）与全局取消；② **根契约层** record（`DownloadRequest` / `DownloadSession` / `WorkContext` / `PageContext` / `PageOutcome` / `PipelineSink` / `ToolPaths` / `ChargedPreviewException` / `DanmakuFormat`），它们被各子命名空间交叉引用，故刻意留在根层避免循环依赖。子命名空间之间的引用一律显式 `using`：`Cli` / `Pipeline` 被 `Program` 引用；`Pipeline` 内部 `DownloadPipeline → WorkSetup → VideoInfo → PageQueue` 单向串联；`Media` 依赖 `Mux`（`MuxFinish`）与 `Download`（下载入口）；`Serve` 引用 `Pipeline` 与 `Auth`，**反向不成立**——下载链路只通过根层的 `PipelineSink` 回调回吐进度，不认识 `BBDown.Serve.DownloadTask`（由 `just check-deps` 守护）。所有子命名空间类型通过 C# 嵌套命名空间查找可见根层类型，反之亦然（测试项目用 csproj 全局 `<Using>` 补齐）。

---

## 3. 请求生命周期

一次下载从输入到落盘的主干流程：

```
用户输入
  │
  ▼
InputResolver.ResolveAsync      URL/av/BV/ep/ss/md/合集/系列/收藏夹/空间/b23.tv → 内部 avid
  │  md{数字} 详情页 → pgc/review/user 映射出 season_id → 编码为 ep:ss{季_id}（整季形态）
  │  ss{数字} 季号 → pgc/view/web/season 取 season_id → 同样编码为 ep:ss{季_id}（整季形态，与 md 对称）
  │
  ▼
FetcherRegistry.FetchAsync     按 IdPrefix 分发给对应 Fetcher → VInfo(分P列表/标题/封面…)
  │  (ep: 先番剧后回退 cheese；番剧可按 --intl-api 走 IntlBangumiInfoFetcher)
  ▼
Parser.ExtractTracksAsync (编排，按 API 模式委派到 PlayUrl/*) → ParsedResult(视频轨/音频轨/FLV 分段/字幕/弹幕入口)
  │  WEB: WBI 签名(UGC)；TV: access_token；APP: gRPC + identify_v1；INTL: protobuf/json
  ▼
DownloadPipeline.RunAsync (BBDown.Pipeline，三段下载主干，CLI 与 serve 共用)
  │  ① WorkSetup.Build      → WorkContext (进程级初始化、账号探测)
  │  ② VideoInfo.FetchAsync → WorkContext (标题/分P/封面/弹幕入口)
  │  ③ PageQueue.RunAsync   → 逐分 P 编排（--comment>0 时逐分 P 委托内先跑 CommentDownload，按 aid 去重；与视频下载互不干扰）
  │     └─ CommentDownload.RunAsync (WBI 分页抓评论 → 按 --comment-formats 落盘 json/txt)
  ▼
PageDownload.RunAsync / DispatchAsync   (单分 P：封面/字幕准备 → 分派 DASH/FLV)
  │  ├─ DashDownload.RunAsync   / FlvDownload.RunAsync
  │  ├─ DownloadUtil.DownloadAsync (续传写入 .bbdown.part)
  │  ├─ SubUtil / DanmakuUtil (字幕/弹幕)
  │  └─ MuxFinish.RunAsync (FFmpeg/MP4Box 混流 + 嵌入元数据/章节/字幕) — 统一 DASH/FLV 收尾
  ▼
落盘 (SavePath 经 FileNameUtil 截断) + 写入 BBDown.archives (--save-records)
```

**直播录制分支**：当输入命中 `LiveInputResolver.TryParse`（`live:` / `live.bilibili.com/{数字}` / `m.live.bilibili.com`），`RunApp` 在 `WorkSetup.Build` 之前分流到 `LiveDownload.RunAsync`，这是一条不经 `WorkContext` / 混流主干的独立链路：

```
用户输入（直播间地址）
  │
  ▼
LiveInputResolver.TryParse    live: / live.bilibili.com/{房间号} → 真实房间号（短号换算）
  │
  ▼
LiveFetcher.FetchAsync        取 http_stream + flv 流地址、房间信息与清晰度档位
  │
  ▼
LiveRecorder.RunAsync         分段落盘（断流退避重连 / CDN failover / 首段成功后编码锁定）
  │  ├─ LiveSegmentWriter      单段 FLV 写入 <dest>.<NNN>.bbdown.part
  │  └─ LiveProgress           进度回吐
  ▼
LiveMuxer.MergeSegmentsAsync  Ctrl+Break 触发：分段 FLV → 单个 mp4（avc→h264_mp4toannexb / hevc→hevc_mp4toannexb，+genpts）；Ctrl+C 中断保留分段、不合并
  ▼
落盘 <主播名>-<标题>-<yyyyMMdd_HHmmss>.mp4
```

**取消令牌贯穿全链路**：全局 `CancellationTokenSource`，Ctrl+C 触发优雅取消，`OperationCanceledException` 被捕获后进程以 `130` 退出，已下载的 `.bbdown.part` 临时文件保留，重跑同一条命令即可续传。直播录制同样接入该令牌：`LiveSignal` 区分 `Ctrl+Break`（停录并合并，退出码 `0`）与 `Ctrl+C`（中断保留分段，退出码 `130`）。

---

## 4. 四种 API 解析模式

`DetermineApiType` 的优先级为 **TV > APP > INTL > WEB**。各模式差异：

| 维度       | WEB                        | TV                     | APP                                  | INTL                |
| ---------- | -------------------------- | ---------------------- | ------------------------------------ | ------------------- |
| 目标 Host  | `api.bilibili.com`         | `api.snm0516.aisee.tv` | `api.bilibili.tv` (gRPC)             | `api.biliintl.com`  |
| 鉴权       | Cookie (`SESSDATA` 等)     | `access_token`         | `authorization: identify_v1 {Token}` | `access_token`      |
| 传输       | JSON                       | JSON                   | 手写 gRPC 帧 (`AppHelper` 打包/解包) | protobuf/json       |
| WBI 签名   | 仅 UGC playurl / view / v2 | 否                     | 否                                   | 否                  |
| 清晰度限制 | 有 res/fps                 | 无 res/fps             | 番剧仅 HEVC；码率为估算              | 由 stream_list 决定 |
| 典型用途   | 普通视频、大会员网页内容   | 番剧/大会员 TV 接口    | 番剧 APP 接口                        | 东南亚国际版视频    |

关键细节：

- **WBI 签名**：仅对 **UGC** 的 playurl、`wbi/view`、`wbi/v2` 使用；当未探测到账号（`Wbi` 为空）时，所有 WBI 接口退化为不签名。番剧 / 课程的 playurl **不做 WBI 签名**。
- **APP 端 gRPC**：非 `Grpc.Net` 客户端，而是手写 HTTP POST + gzip 帧（`AppHelper.PackMessage` / `ReadMessage`），鉴权靠 `identify_v1` 令牌头。
- **playurl 请求次数**：DASH 会先按用户 `-q` 请求一次，再额外以最高清晰度（`qn=127`）请求一次以取得「免二压 / 原始画质」视频轨（两次取并集）；**FLV 始终以 `qn=127` 请求，忽略 `-q`**。

---

## 5. serve 模式与鉴权

`BBDown serve` 用 ASP.NET Minimal API 暴露任务增删查接口（完整契约见 [API.md](./API.md)）。设计要点：

- **令牌鉴权**：`SetUpServer` → `FinalizeAuth(url)` 判定监听地址：
    - 绑定**回环地址**（默认 `127.0.0.1`）→ 免令牌。
    - 绑定**非回环地址**（如 `0.0.0.0`）且未显式 `--serve-token` → 自动生成令牌并打印，客户端必须携带 `X-BBDown-Token` 请求头或 `?token=` 查询参数，否则返回 `401`。
- **请求契约收窄**：`ServeRequestOptions` 是 `DownloadRequest` 的受控子集，刻意剔除主机可控字段（`FFmpegPath`/`Mp4boxPath`/`Aria2cPath`/`Aria2cArgs`/`WorkDir`/`FilePattern`/`MultiFilePattern`/`Debug`/`UserAgent`/`ConfigFile`），这些一律以服务端启动配置为准，即便请求传入也会被忽略。
- **SSRF 防护**：任务完成后的 `CallBackWebHook` 回调用 `IsSafeWebHook` / `IsPrivateAddress` 校验，拒绝内网 / 回环地址，仅允许公网可达端点。
- **CORS**：默认**完全关闭**（不发送 `Access-Control-Allow-Origin` 头），从根本上消除恶意网页经浏览器发起的 CSRF 面；仅当显式 `--cors-origin <url>` 时才对该单一来源开放（用于同源之外的 Web 前端），且公网暴露仍需配合反向代理与 TLS。
- **容量上限**：已完成任务保留上限 `MaxFinishedTasks = 200`，超出按策略淘汰。
- **并发限流**：`--max-concurrent N`（默认 `0` = 不限制，保持历史行为）。`SetUpServer` 在 `N > 0` 时建立 `SemaphoreSlim(N, N)`；`AddDownloadTaskAsync` 经 `RunGatedAsync` 在调用 `DownloadPipeline.RunAsync` 前取额度、`finally` 归还。取额度发生在 aid 去重登记**之后**，因此排队中的任务已在 `runningTasks` 里可见，`DownloadTask.Status` 为 `Queued`，拿到额度转 `Running`，收尾转 `Finished`。`max-concurrent` 仅约束**同时下载的任务数**，多余任务排队；单个任务内部的下载并行度（分片并发）由多线程下载器自行决定（`PageDownload.BuildDownloadConfig` 始终将 `MaxDegreeOfParallelism` 设为 `0`，即回落到 `ProcessorCount`），不再随限流被压到 `1`。`0` 表示不限制（与 CLI 完全一致）。

> 注意：`/remove-finished*` 与 `/add-task` 均为 **POST**；查询类（`/get-tasks/*`）为 GET。
>
> **单任务取消**：每个 `DownloadTask` 持有与进程级 `AppEnv.CancellationToken`（关停源）`Link` 的 `CancellationTokenSource Cts`；`POST /stop-task/{id}` 调用 `task.Cts.Cancel()` 取消单个运行/排队中的任务，不影响其他任务。Ctrl+C 取消全局令牌会经链接源取消所有任务。`Cts` 标记 `[JsonIgnore]`，不进入任务 DTO 的序列化。

---

## 6. 断点续传

续传由 `PartFile` + `PartManifest` 实现：

- 每条流先写入 `<目标路径>.bbdown.part` 数据文件，并维护 `<目标路径>.bbdown.json` 清单（记录 URL 指纹、各分片已完成字节、服务器校验器）。
- 清单以目标路径的 **SHA256 前 16 位**作为指纹（`PartFile.Fingerprint`），用于识别「同一资源」避免错续。
- 分片大小 `DefaultChunkSize = 20MB`，支持分片并发写入；失败时临时文件保留。
- **重跑同一条命令即可从断点继续**，粒度覆盖：单条流（视频轨下完、音频轨失败 → 只补音频轨）与合集 / 多 P（某分 P 失败仅补该分 P）。所有分片（含边下边混流的临时文件）都成功后才清理临时文件。
- CDN 策略：CMCC 等特殊 CDN 强制单线程；`ReplaceUrl` 默认把 https→http（mcdn 域跳过）。

---

## 7. 凭据与安全模型

### 7.1 单一凭据文件 `BBDown.data`

WEB / TV / APP 三类凭据合并进**同一个 JSON 对象**（字段：`cookie` / `refresh_token` / `ts` / `tv_access_token` / `tv_ts` / `app_access_token` / `app_ts`），未登录字段为 `null`。由 `CredentialStore` 用 `System.Text.Json` **源生成器**（`CredentialJsonContext`）序列化，规避 AOT 裁剪。每次保存只更新对应字段并合并保留其余字段（登录 TV/APP 不会冲掉 WEB Cookie）。类 Unix 系统下文件权限收紧为 `600`。

### 7.2 Web Cookie 续期

`Login.TryRefreshWebCookieIfStaleAsync` 在下载前 best-effort 检测 Cookie 是否过旧，用 `refresh_token` 经 **RSA-OAEP** 加密请求续期，刷新 `cookie` 与 `refresh_token` 并回写；续期失败不影响正常下载（回退到已有 Cookie）。续期进程内仅触发一次。

### 7.3 传输与接口安全

- **TLS 逃生舱**：`BBDOWN_INSECURE_TLS=1` 用于关闭证书校验（无常规代理配置项，HTTPUtil 不读取系统代理）。
- **SSRF**：仅 `CallBackWebHook` 回调做内网 / 回环地址校验。
- **serve 令牌**：见第 5 节。
- **设备标识**：`buvid3` / `buvid4` 纯远端获取，失败时不本地伪造设备标识。

---

## 8. 字幕 / 弹幕 / 评论 / 文件名

- **字幕 (`SubUtil`)**：已登录账号可走 WEB/TV；**未登录时只能走 APP gRPC** 获取字幕。AI 字幕默认不下载，需 `--allow-ai` 显式开启。
- **弹幕 (`DanmakuUtil`)**：支持 XML / ASS 两种格式（`--danmaku-formats`），ASS 参数全部硬编码不可配，无去重 / 过滤。
- **评论 (`CommentFetcher` / `CommentRenderer` / `CommentDownload`)**：走 `/x/v2/reply/wbi/main`（WBI 签名 + 游标分页）。`--comment N` 默认 `0`（不下载），`--comment-sort` 选 `hot`/`time`，`--comment-formats` 选 `json`/`txt`（`CommentFormat` 与弹幕格式**两个互不相干的特性，解析逻辑各自独立**）。评论区按 **aid** 绑定、与 cid / 分 P 无关，挂在 `PageQueue.RunAsync` 逐分 P 委托里用局部 `HashSet` 按 aid 去重，**DASH 与 FLV 两条路径都覆盖**；多 P 同 aid 只抓一次。默认只保留一级评论内联的最多 3 条楼中楼预览，`--full-comment` 才额外翻页抓全。抓取失败一律降级为「拿到多少算多少」，只有 WBI 签名错误（`-403`）才抛异常——评论下载与视频下载互不干扰。
- **文件名 (`FileNameUtil`)**：按 **UTF-8 字节数截断，上限 200 字节**（约 66 个汉字），避免过长路径；变量支持自定义日期格式 `<publishDate:yyyyMMdd>` / `<videoDate:格式>`。

---

## 9. 构建与 AOT

- SDK：`Microsoft.NET.Sdk.Web`（`BBDown`）、`Microsoft.NET.Sdk`（`BBDown.Core` / 测试）。
- `BBDown.Core` 标记 `IsAotCompatible=true`；序列化一律用 `JsonSerializerContext` 源生成器（`CredentialJsonContext` / `DownloadRequestJsonContext` / `PartJsonContext` / `AppJsonSerializerContext`），禁止运行时反射。
- 全局 `TreatWarningsAsErrors=true`、`Nullable enable`、`LangVersion latest`、集中式包版本（`Directory.Packages.props`）。
- 发布 AOT：`dotnet publish -c Release -r <RID> /p:PublishAot=true`。注意 AOT 下 `BBDown.data` 等 JSON 必须走源生成器，否则会被裁剪导致反序列化失败。

> 调试构建（`dotnet build -c Debug`）不受 AOT 限制，可正常用运行时反射；仅发布 AOT 时需遵守上述约束。

---

## 10. 专栏导出旁路

根命令识别到专栏地址（`https://www.bilibili.com/opus/...`、`cv{id}`、`opus{id}` 等）时，会把 B 站「专栏 / 图文」抓取并转换为 Markdown。它与音视频下载链路**完全独立**，是一条旁路，目的是避免让专栏逻辑被 `WorkSetup.Build` 的 ffmpeg 探测、混流、`SavePath.Format`（硬编码 `.mp4`）等音视频专属步骤拖累。

### 10.1 分流点

分流发生在 `Program.RunApp` 顶部，**早于** `DownloadPipeline.RunAsync` 内的 `WorkSetup.Build`：

```
用户输入
  │
  ▼
OpusInputResolver.TryParse(input)   opus URL / cv 号 / opus id / 前缀写法 → OpusTarget(OpusId|CvId)
  │  （裸数字一律拒绝，留给视频链路 av 号简写）
  ▼
OpusDownload.RunAsync (BBDown.Pipeline)  不走 WorkSetup.Build / 不构造 WorkContext / 不探测 ffmpeg
  │  ├─ CredentialStore.LoadAll   读取 WEB Cookie（专栏可能登录可见）
  │  ├─ Buvid.InitAsync           获取 buvid3/4（沿用既有 HTTP 栈）
  │  ├─ OpusFetcher.FetchAsync     先试 opus/detail（htmlNewStyle）→ 失败回退 article/view(cv)；旧版 HTML 文章降级转换
  │  ├─ OpusHtmlToMarkdown        把 B 站专栏结构化的段落 JSON 转成 Markdown 模型
  │  ├─ OpusMarkdownRenderer      渲染标题/front matter/图片/列表/代码/公式等
  │  └─ OpusImageUtil             默认下载图片到 <标题>/images/；--no-images 则保留远程链接
  ▼
落盘 <标题>.md（UTF-8 无 BOM，保证 YAML front matter 可被解析）
```

### 10.2 与主干的关键差异

- **不经过 `WorkSetup.Build`**：专栏不需要 ffmpeg / 账号探测 / 分 P 编排，因此**没有 ffmpeg 也能跑**；也不会因缺 ffmpeg 而抛异常。
- **不构造 `WorkContext`**：复用了 `HTTPUtil` / `Buvid` / `CredentialStore` 等底层能力，但绕开了 `WorkContext` 这一音视频上下文。
- **不用 `SavePath.Format`**：输出文件名由 `FileNameUtil.GetValidFileName` 直接处理，按 `<标题>.md` 落盘，图片进 `<标题>/images/`，与音视频的 `.mp4` 命名体系解耦。
- **复用的 HTTP 桩点**：`OpusFetcher` 通过替换 `HTTPUtil.AppHttpClient` 进行单测（`StubHttpMessageHandler` + 路由桩），与 `BBDown.Core.Tests` 中其他 HTTP 测试共用 `HttpStubCollectionDefinition` 串行集合，避免 HttpClient 静态字段竞争。
- **AOT 约束一致**：`OpusFetcher` 解析接口 JSON 一律用 `JsonDocument` / `GetProperty`，不依赖运行时反射，与全项目 AOT 策略一致。

### 10.3 serve 模式说明

v1 的 `serve` JSON API 仅面向音视频任务，**不支持**提交专栏导出任务（`/add-task` 的 `Url` 只识别 `av|bv|BV|ep|ss` 编号）。专栏导出目前仅通过 CLI 根命令自动识别进行。
