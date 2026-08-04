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
├── BBDown/                 # 入口可执行项目 (Sdk.Web, PackAsTool)
│   ├── Program.cs          # Main、子命令装配、serve 启动、全局取消；RunDownloadAsync 三段下载主干（CLI 与 serve 共用）
│   ├── CommandLineInvoker.cs  # 全部 CLI 选项与别名 (GetRootCommand)
│   ├── WorkSetup.cs        # 进程级初始化 → WorkContext (Build)
│   ├── VideoInfo.cs        # FetchAsync 解析视频信息（标题/分P/封面/账号探测）
│   ├── PageQueue.cs        # 逐分 P 编排 (RunAsync)
│   ├── PageDownload.cs     # 单分 P 下载入口，分派 DASH/FLV (RunAsync / DispatchAsync)
│   ├── DashDownload.cs     # DASH 轨下载 (RunAsync)
│   ├── FlvDownload.cs      # FLV 分段下载与合并 (RunAsync)
│   ├── PageAssets.cs       # 封面/字幕准备、弹幕下载
│   ├── MuxFinish.cs        # 混流收尾、跳过已存在、清理临时文件 (DASH/FLV 共用)
│   ├── TrackSelect.cs      # 轨道排序、信息打印、交互选轨
│   ├── PageSelect.cs       # 分 P 选择/范围
│   ├── SavePath.cs         # 文件名/路径格式化
│   ├── CdnHost.cs          # CDN host 策略
│   ├── ChapterMeta.cs      # 章节元数据
│   ├── Account.cs          # 账号探测
│   ├── ArchiveLog.cs       # 归档记录
│   ├── BBDownAria2c.cs     # Aria2c 下载
│   ├── BBDownUtil.cs       # 通用工具 (Utils)
│   ├── ProgressBar.cs      # 进度条
│   ├── DownloadUtil.cs     # 唯一下载入口 (续传、CDN 策略)
│   ├── Muxer.cs            # FFmpeg/MP4Box 混流、FLV 合并
│   ├── PartFile.cs         # 断点续传状态 (.bbdown.part/.bbdown.json)
│   ├── CredentialStore.cs  # 单一 JSON 凭据读写 (源生成器 AOT 安全)
│   ├── Login.cs            # WEB/TV/APP 扫码登录与 refresh_token 续期
│   ├── InputResolver.cs    # URL/编号 → 内部 avid 解析
│   ├── ConfigParser.cs     # 配置文件解析 (仅补齐命令行未指定项)
│   ├── DownloadOptions.cs  # 运行时配置 (含 WithSecretsRedacted)
│   ├── DownloadSession.cs  # 分 P 生命周期恒定入参 record
│   ├── WorkContext.cs      # 工作上下文 record
│   ├── PageContext.cs      # 分 P 上下文 record
│   ├── DanmakuFormat.cs    # 弹幕格式信息
│   └── ServeRequestOptions.cs # serve 请求受控子集 + CallBackWebHook
│
├── BBDown.Core/            # 核心类库 (library, IsAotCompatible)
│   ├── BiliApi.cs          # 各接口 Host/Path 常量
│   ├── Config.cs           # 清晰度档位 (Qualities/MaxQn/DolbyVisionQn)
│   ├── Parser.cs           # 播放地址解析 (DASH/FLV/APP/INTL)、WBI 签名、playurl 请求
│   ├── AppHelper.cs        # APP gRPC 手写帧 (PackMessage/ReadMessage)
│   ├── IdPrefix.cs         # 输入编号前缀常量 (ep:/ss:/lists:/series:/fav:/cheese: 等)
│   ├── Fetcher/            # 6 个 static Fetcher + FetcherRegistry (按 IdPrefix 分发)
│   ├── Util/               # BV 转换、FileNameUtil(200 字节截断)、Buvid 等
│   ├── Entity/             # VInfo / Page / Video / Audio / ParsedResult 等
│   ├── APP/                # APP gRPC 协议 (proto 生成代码)
│   └── DanmakuUtil.cs      # 弹幕获取 (xml/ass)
│
├── BBDown.Tests/           # 针对 BBDown 的 xUnit 测试
└── BBDown.Core.Tests/      # 针对 BBDown.Core 的 xUnit 测试
```

**依赖方向**：`BBDown` → `BBDown.Core`；两个测试项目分别依赖对应实现。Core 不反向依赖入口项目，保证核心逻辑可独立测试。

---

## 3. 请求生命周期

一次下载从输入到落盘的主干流程：

```
用户输入
  │
  ▼
InputResolver.ResolveAsync      URL/av/BV/ep/ss/合集/系列/收藏夹/空间/b23.tv → 内部 avid
  │
  ▼
FetcherRegistry.FetchAsync     按 IdPrefix 分发给对应 Fetcher → VInfo(分P列表/标题/封面…)
  │  (ep: 先番剧后回退 cheese；番剧可按 --intl-api 走 IntlBangumiInfoFetcher)
  ▼
Parser (按 API 模式发 playurl)  → ParsedResult(视频轨/音频轨/FLV 分段/字幕/弹幕入口)
  │  WEB: WBI 签名(UGC)；TV: access_token；APP: gRPC + identify_v1；INTL: protobuf/json
  ▼
Program.RunDownloadAsync (三段下载主干，CLI 与 serve 共用)
  │  ① WorkSetup.Build      → WorkContext (进程级初始化、账号探测)
  │  ② VideoInfo.FetchAsync → WorkContext (标题/分P/封面/弹幕入口)
  │  ③ PageQueue.RunAsync   → 逐分 P 编排
  ▼
PageDownload.RunAsync / DispatchAsync   (单分 P：封面/字幕准备 → 分派 DASH/FLV)
  │  ├─ DashDownload.RunAsync   / FlvDownload.RunAsync
  │  ├─ DownloadUtil.DownloadAsync (续传写入 .bbdown.part)
  │  ├─ SubUtil / DanmakuUtil (字幕/弹幕)
  │  └─ MuxFinish.RunAsync (FFmpeg/MP4Box 混流 + 嵌入元数据/章节/字幕) — 统一 DASH/FLV 收尾
  ▼
落盘 (SavePath 经 FileNameUtil 截断) + 写入 BBDown.archives (--save-records)
```

**取消令牌贯穿全链路**：全局 `CancellationTokenSource`，Ctrl+C 触发优雅取消，`OperationCanceledException` 被捕获后进程以 `130` 退出，已下载的 `.bbdown.part` 临时文件保留，重跑同一条命令即可续传。

---

## 4. 四种 API 解析模式

`DetermineApiType` 的优先级为 **TV > APP > INTL > WEB**。各模式差异：

| 维度        | WEB                              | TV                                 | APP                                  | INTL                                |
| ----------- | -------------------------------- | ---------------------------------- | ------------------------------------ | ----------------------------------- |
| 目标Host    | `api.bilibili.com`               | `api.snm0516.aisee.tv`             | `api.bilibili.tv` (gRPC)             | `api.biliintl.com`                  |
| 鉴权        | Cookie (`SESSDATA` 等)           | `access_token`                     | `authorization: identify_v1 {Token}` | `access_token`                      |
| 传输        | JSON                             | JSON                               | 手写 gRPC 帧 (`AppHelper` 打包/解包) | protobuf/json                       |
| WBI 签名    | 仅 UGC playurl / view / v2       | 否                                 | 否                                   | 否                                  |
| 清晰度限制  | 有 res/fps                       | 无 res/fps                         | 番剧仅 HEVC；码率为估算              | 由 stream_list 决定                 |
| 典型用途    | 普通视频、大会员网页内容         | 番剧/大会员 TV 接口                | 番剧 APP 接口                        | 东南亚国际版视频                    |

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
- **请求契约收窄**：`ServeRequestOptions` 是 `DownloadOptions` 的受控子集，刻意剔除主机可控字段（`FFmpegPath`/`Mp4boxPath`/`Aria2cPath`/`Aria2cArgs`/`WorkDir`/`FilePattern`/`MultiFilePattern`/`Debug`/`UserAgent`/`ConfigFile`），这些一律以服务端启动配置为准，即便请求传入也会被忽略。
- **SSRF 防护**：任务完成后的 `CallBackWebHook` 回调用 `IsSafeWebHook` / `IsPrivateAddress` 校验，拒绝内网 / 回环地址，仅允许公网可达端点。
- **CORS**：仍默认 `AllowAnyOrigin`（便于本地前端调试），因此公网暴露存在风险，需配合反向代理与 TLS。
- **容量上限**：已完成任务保留上限 `MaxFinishedTasks = 200`，超出按策略淘汰。

> 注意：`/remove-finished*` 与 `/add-task` 均为 **POST**；查询类（`/get-tasks/*`）为 GET。

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

## 8. 字幕 / 弹幕 / 文件名

- **字幕 (`SubUtil`)**：已登录账号可走 WEB/TV；**未登录时只能走 APP gRPC** 获取字幕。AI 字幕默认不下载，需 `--allow-ai` 显式开启。
- **弹幕 (`DanmakuUtil`)**：支持 XML / ASS 两种格式（`--danmaku-formats`），ASS 参数全部硬编码不可配，无去重 / 过滤。
- **文件名 (`FileNameUtil`)**：按 **UTF-8 字节数截断，上限 200 字节**（约 66 个汉字），避免过长路径；变量支持自定义日期格式 `<publishDate:yyyyMMdd>` / `<videoDate:格式>`。

---

## 9. 构建与 AOT

- SDK：`Microsoft.NET.Sdk.Web`（`BBDown`）、`Microsoft.NET.Sdk`（`BBDown.Core` / 测试）。
- `BBDown.Core` 标记 `IsAotCompatible=true`；序列化一律用 `JsonSerializerContext` 源生成器（`CredentialJsonContext` / `DownloadOptionsJsonContext` / `PartJsonContext` / `AppJsonSerializerContext`），禁止运行时反射。
- 全局 `TreatWarningsAsErrors=true`、`Nullable enable`、`LangVersion latest`、集中式包版本（`Directory.Packages.props`）。
- 发布 AOT：`dotnet publish -c Release -r <RID> /p:PublishAot=true`。注意 AOT 下 `BBDown.data` 等 JSON 必须走源生成器，否则会被裁剪导致反序列化失败。

> 调试构建（`dotnet build -c Debug`）不受 AOT 限制，可正常用运行时反射；仅发布 AOT 时需遵守上述约束。
