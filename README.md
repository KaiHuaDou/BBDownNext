<h1 align="center">BBDown vNEXT</h1>

<p align="center">
nilaoda/BBDown 的全面重构 - 增强分支（上游已归档）。开源 · 免费的哔哩哔哩（B 站）视频下载 / 解析工具，以命令行（CLI，跨平台）与图形界面（BBDown.GUI，Avalonia 桌面端）双形态提供：视频 / 番剧 / 课程 / 直播 / 专栏 / 稍后再看，支持 8K、HDR、杜比视界、DASH / FLV、多线程与断点续传，并提供带鉴权令牌的 HTTP API 服务器。
</p>

<p align="center">
  <img alt=".NET" src="https://img.shields.io/badge/.NET-9.0-512BD4?logo=dotnet&logoColor=white" />
  <img alt="License" src="https://img.shields.io/badge/license-MIT-green.svg" />
  <a href="https://github.com/KaiHuaDou/BBDownNext/actions/workflows/ci.yml"><img alt="CI" src="https://github.com/KaiHuaDou/BBDownNext/actions/workflows/ci.yml/badge.svg" /></a>
  <a href="https://github.com/KaiHuaDou/BBDownNext/releases"><img alt="Release" src="https://img.shields.io/github/v/release/KaiHuaDou/BBDownNext?label=release" /></a>
  <img alt="Downloads" src="https://img.shields.io/github/downloads/KaiHuaDou/BBDownNext/total" />
  <a href="https://github.com/KaiHuaDou/BBDownNext/issues"><img alt="Issues" src="https://img.shields.io/github/issues/KaiHuaDou/BBDownNext" /></a>
  <a href="https://github.com/KaiHuaDou/BBDownNext/discussions"><img alt="Discussions" src="https://img.shields.io/badge/Discussions-%E5%BC%80%E5%90%AF-1EAEDB" /></a>
</p>

<p align="center">
  <a href="#特性">特性</a> ·
  <a href="#与原版-bbdown-的差异">与原版差异</a> ·
  <a href="#安装">安装</a> ·
  <a href="#快速开始">快速开始</a> ·
  <a href="#参数说明">参数说明</a> ·
  <a href="#子命令">子命令</a> ·
  <a href="#GUI">GUI</a> ·
  <a href="#服务器模式">服务器模式</a> ·
  <a href="#数据文件格式">数据文件格式</a> ·
  <a href="./PROTOCOL.md">后处理协议</a> ·
  <a href="#常见问题">常见问题</a> ·
  <a href="./TODO.md">路线图</a> ·
  <a href="./SECURITY.md">安全</a> ·
  <a href="./CONTRIBUTING.md">贡献</a>
</p>

> 问题反馈与功能建议请前往 [Issues](https://github.com/KaiHuaDou/BBDownNext/issues)；使用交流请前往 [Discussions](https://github.com/KaiHuaDou/BBDownNext/discussions)。

---

## 为什么选择 BBDown

面向追求 **稳定、安全、拿来即用** 的用户与开发者：

- **下载可靠**：下载引擎统一由 Downloader 库实现多线程分片与断点续传，分片级重试、续传元数据自愈校验、下载请求头统一注入，配套 980+ 单元测试守护。
- **serve 安全**：HTTP API 模式内置令牌鉴权、SSRF 防护（含 IPv4-mapped IPv6 归一化）、CORS 默认关闭、host 与工作目录服务端固定，取消令牌贯通全链路。
- **工程规范**：下载能力集中 `BBDown.Core`、依赖单向无环（`check-deps` 守护）、`ResourceId` 判别联合缺分支编译报错、单文件 / 单方法行数上限、Microsoft Testing Platform 现代测试栈。
- **拿来即用**：AOT 单文件发布免安装 .NET 运行时，Windows 7 兼容产物、musl 静态产物开箱即用；CLI 与 GUI 双形态共享同一套下载核心。

## 特性

- 内容与来源
    - **视频 / 番剧 / 课程** · 直播回放、收藏夹、合集 / 系列、UP 主空间列表、稍后再看列表
    - **内容组合选择** · `-g` / `-w` / `-W` 自由组合音频、视频、字幕、弹幕、封面、评论等（get ∪ with − without）
    - **多 P 批量选择** · `-p` 支持单集、列表、区间、`latest`，`-iap` 逐集交互确认
    - **专栏 / 图文导出** · 根命令自动识别专栏地址与纯图文动态，转为 Markdown，图片可选本地下载

- 解析引擎
    - **4 种模式**：`--api` 单选 `web` / `tv` / `app` / `intl`，自动应对区域限制
    - **兼容 BiliPlus 代理**，WEB 模式自动 WBI 签名
    - **解析优先**：`--info-only` 查看可用流，`-iaq` 交互式选择清晰度、`-iap` 交互式选择分 P
    - **解析加速**：播放器信息（player/v2）与拉流解析并行发起，WEB 自动档每分 P 仅一次 API 请求

- 媒体与封装
    - **DASH / FLV** 封装 · 杜比视界、HDR、8K、高码率音视频流
    - **4 种混流方式**：`--mux` 支持 `none` / `mpeg4`（默认）/ `mp4box` / `mkv`（Matroska 容器，字幕原生 `-c:s copy`）
    - **外部后处理** · `--post-process` 指定外部进程，下载完成后按需处理轨道文件（协议见 [PROTOCOL.md](./PROTOCOL.md)）
    - **编码与画质优先级** `-e` / `-q`，弹幕（XML/ASS）、字幕、封面、AI 字幕
    - **混流增强**：写入元数据与章节，多 P 混流写入分 P 序号与总集数
    - **封面嵌入** · `C` 将封面嵌入视频文件（attached_pic），播放器可直接显示缩略图；`c` 则单独保存封面文件

- 直播录制
    - **直播间直录** · 传入直播间地址（`live12345` 直写 / `live.bilibili.com`）即可录制，短号自动换算真实房间号
    - **清晰度可选** · `--live-quality`（默认原画），支持原画 / 蓝光 / 超清 / 高清 / 流畅 / 2K / 4K / 杜比
    - **可控停录** · `Ctrl+Break` 停录并合并分段，`Ctrl+C` 中断保留分段不合并

- 下载引擎与可靠性
    - **统一下载引擎** · 多线程分片与断点续传由 Downloader 库实现（AOT 兼容），替代自研分片下载器与 `.bbdown.part` / `.bbdown.json` 清单
    - **自愈式断点续传** · 续传元数据内嵌 `.download` 临时文件末尾并周期性刷新；恢复时比对服务端大小，URL 指向的内容已变化则自动删除临时文件重下
    - **分片级重试** · 分片瞬态故障自动退避重试（上限 5 次）并从断点续下，避免整 P 退避重下
    - **并发控制** · 分片并发上限 32；FLV 分段并行（上限 4）且与片段内下载连接共享配额；`--single-thread` 与 CMCC 域名强制单块
    - **下载头统一注入** · UA / 平台条件 Referer / Cookie 由 `DownloadHeaderHandler` 统一注入，修复 CDN 403
    - **下载性能** · 探测改 Range 0-0 并复用连接；进度采样 1 秒 5 次并按采样周期折算速率；字幕并行下载；分片缓冲走 ArrayPool
    - **进度条隔离** · 进度条仅在实际下载音视频轨时显示，混流 / 封装即清行；与日志、交互输入互不污染
    - **`--save-records`** 自动跳过已下载分 P，`--delay-per-page` 控制请求间隔

- 账号与配置
    - **扫码登录**（WEB / TV / APP），凭据自动保存，`refresh_token` 续期
    - **自定义文件名/日期** `-F` / `-M`，配置文件 `BBDown.config`
    - **CDN / PCDN 控制** `--upos-host` / `--allow-pcdn`
    - **日志脱敏** · Cookie、access_token 与密钥由 `Redactor` 自动打码，不落明文日志

- 双形态
    - **命令行 CLI** · 跨平台（Win / Linux / macOS）· .NET 9 · AOT 单文件发布
    - **图形界面 BBDown.GUI** · 单窗口 Avalonia，直接复用 BBDown.Core 下载库（非子进程调用）：任务队列与并发控制、日志实时显示、扫码登录、直播 / 专栏任务分流、队列持久化、窗口尺寸记忆、拖放输入、选项随 exe 便携保存；独立 CI 发布 Windows / macOS / Linux 三平台 AOT 单文件

- 扩展与集成
    - **服务器模式** `serve`，带鉴权令牌的 HTTP JSON API → [API.md](./API.md)
    - **serve 安全加固** · SSRF 防护（拒绝内网 / 回环，IPv4-mapped IPv6 先归一化再判定，连接前二次校验）、CORS 默认关闭、host 与工作目录由服务端启动参数固定（请求体无法覆盖）、非回环地址强制令牌、取消令牌沿触网路径贯通（Ctrl+C 可中断排队任务解析）
    - **后处理插件协议** · `--post-process` 对所有 DASH 轨调起外部进程，是否加密由处理方自行判断，主程序不内置解密能力，密钥与加密信息由外部进程自行获取管理 → [PROTOCOL.md](./PROTOCOL.md)
    - **内置示例插件** · `Plugins/BBDown.Sample` 提供协议最小实现与模板，自带独立构建配置与契约测试
    - **Windows 7 兼容** · `win-x64` 产物内置 YY-Thunks 与 VC-LTL，在 Windows 7 上可直接运行（无需安装 .NET 运行时）
    - **musl 静态产物** · `linux-musl-x64` / `linux-musl-arm64`，无动态依赖，可直接放入容器运行（无需 Dockerfile）

- 工程品质
    - **980+ 单元测试**，覆盖解析、混流、serve 安全等全部核心路径
    - **分层清晰** · 下载能力集中在 `BBDown.Core`（`Pipeline` / `Media` / `Mux` / `Download` / `Live` / `Auth` / `Fetcher` / `PlayUrl` / `Opus` / `Comment` / `Entity` / `Util`），CLI 与 serve 留在 `BBDown`（`Cli` / `Serve`）；依赖单向成树（`check-deps` 守护）
    - **代码规模约束** · 单文件 ≤ 384 行、单方法 ≤ 128 行（`just tokei` 守护），超出即拆分
    - **类型安全** · `ResourceId` 判别联合（10 个 sealed 子类型）取代字符串前缀打标，按类型分发、缺分支编译报错
    - **现代测试栈** · 测试运行器迁移至 Microsoft Testing Platform（xunit.v3 4.0.0），原生运行更快，自带代码覆盖率与 Trx 报告
    - **现代 .NET** · C# 13、全部语法兼容 AOT（正则源生成、源生成器）、不可变 record 契约、纯函数优先、单一来源化（清晰度档位 / 内容字符表由 Core 枚举生成）

## 与原版 BBDown 的差异

本仓库是 [nilaoda/BBDown](https://github.com/nilaoda/BBDown) 的增强分支。与原版相比：

| 能力            | 原版 nilaoda/BBDown             | 本分支                                                                                                                |
| --------------- | ------------------------------- | --------------------------------------------------------------------------------------------------------------------- |
| WBI 签名降风控  | 无                              | playurl / view / 字幕 / 空间列表均做标准 WBI 签名                                                                     |
| 扫码登录        | `login` / `logintv`，APP 需抓包 | 统一 `login`，`--tv` / `--app` 扫码，凭据自动保存                                                                     |
| Cookie 主动续期 | 未实现                          | 保存 `refresh_token`，下载前 RSA-OAEP 主动续期                                                                        |
| 凭据存储        | 分离文件 + 抓包                 | 单一 `BBDown.data`（WEB / TV / APP 同文件互不覆盖）                                                                   |
| AOT 原生发布    | 无                              | 默认 AOT，单文件原生二进制，8 个 RID                                                                                  |
| 下载引擎        | 自研分片下载器 + 清单文件       | Downloader 库：分片级重试、自愈式断点续传、并发 32                                                                    |
| 下载头注入      | 手动拼接                        | `DownloadHeaderHandler` 统一注入（UA / 平台 Referer / Cookie）                                                        |
| serve 安全      | 基础令牌                        | SSRF 防护（含 IPv4-mapped IPv6）、CORS 默认关闭、host 与工作目录服务端固定、取消令牌贯通、ResourceId 任务标识天然去重 |
| 测试运行器      | VSTest                          | Microsoft Testing Platform（xunit.v3）                                                                                |
| 工程约束        | 无                              | 单文件 / 单方法行数上限，依赖单向无环（`check-deps` 守护）                                                            |
| 日志安全        | 明文                            | `Redactor` 脱敏（Cookie / access_token / 密钥）                                                                       |
| mkv 混流        | 无                              | `--mux mkv`（Matroska 容器，字幕原生 copy）                                                                           |
| 专栏 / 图文导出 | 无                              | 根命令自动识别并导出 Markdown + 图片目录                                                                              |
| 稍后再看列表    | 无                              | 根命令识别整个列表，多 P 自动展开                                                                                     |
| UP 主空间投稿   | 无                              | 空间 URL 直接下载全部投稿                                                                                             |
| 直播录制        | 无                              | 根命令直录，断流自动重连，分段合并                                                                                    |
| 图形界面        | 无                              | BBDown.GUI（Avalonia，三平台 AOT 单文件）                                                                             |
| 充电专属试看    | 无专门处理                      | 下载前识别，退出码 2 表示全部为试看                                                                                   |
| 封面处理        | 独立封面下载                    | 独立封面 `c` + 嵌入 `C`（attached_pic）                                                                               |
| 断点续传        | 基础续传                        | downloader 库自动续传（元数据内嵌 .download，内容变化自愈重下）                                                       |
| 单元测试        | 较少                            | 980+，覆盖解析、混流、serve 安全等核心路径                                                                            |

逐项对照与源码位置见 [docs/compared-to-upstream.md](./docs/compared-to-upstream.md)。

## 安装

前往 [Releases](https://github.com/KaiHuaDou/BBDownNext/releases) 页面，下载最新发布版本。

前往 [Actions](https://github.com/KaiHuaDou/BBDownNext/actions) 页面，下载构建版本。

Windows 7 用户请下载 `BBDown-win7-x64` 产物，并安装 [KB3140245](https://support.microsoft.com/help/3140245)（TLS 1.1 / 1.2 支持）后再使用。

## Docker

仓库根目录提供 `Dockerfile`（基于 `linux-musl-x64` 静态产物，运行时镜像自带 FFmpeg）。

```bash
docker build -t bbdown .
docker run --rm -v "$PWD:/downloads" bbdown "https://www.bilibili.com/video/BV16h4y137YS"
```

产物为静态 musl 二进制，也可不写 Dockerfile、直接把 Release 中的 `linux-musl-x64` 产物 `COPY` 进 `scratch` / `distroless` 镜像运行（需自带 FFmpeg 用于混流，或 `--mux none` 跳过混流）。

## 构建

需要先安装 [.NET SDK](https://dot.net)（版本 ≥ 9.0，具体版本以仓库 `global.json` 为准）。

```bash
git clone https://github.com/KaiHuaDou/BBDownNext.git --depth 1
cd BBDown

dotnet build -c Release
```

构建产物位于各项目的 `bin/Release/net9.0/` 目录下

### AOT 单文件

```bash
dotnet publish BBDown -r <RID> -c Release -o <DEST>
```

构建产物位于 `<DEST>` 中

### Windows 7 兼容

```bash
dotnet publish BBDown -r win-x64 -c Release -o <DEST> -p:WindowsWin7Compat=true
```

特定平台细节可参考 [ci.yml](https://github.com/KaiHuaDou/BBDownNext/blob/master/.github/workflows/ci.yml)

### 图形界面

BBDown.GUI 是图形界面（GUI）客户端（Avalonia），目标框架 `net9.0`：

```bash
dotnet build BBDown.GUI -c Release
```

产物位于 `BBDown.GUI/bin/Release/net9.0/` 下，独立运行，直接复用 `BBDown.Core` 下载库，无需额外的 `BBDown.exe`。

图形界面由独立 CI（[gui.yml](https://github.com/KaiHuaDou/BBDownNext/blob/master/.github/workflows/gui.yml)）在 Windows / macOS / Linux（各 `x64` / `arm64`，Linux 仅 glibc）构建自包含 AOT 单文件产物并上传，可手动触发追加到最新 Release；

## 依赖

- **FFmpeg**：用于音视频下载与混流（推荐）。BBDown 会在 `PATH` 与程序所在目录中自动查找；也可用 `--ffmpeg-path` 显式指定。
- **MP4Box**：可选，用于杜比视界等特殊封装的混流。可用 `--mux mp4box` 切换为 MP4Box 混流，或用 `--mp4box-path` 指定路径。
- **aria2c**：可选，用于多线程加速下载。可用 `--aria2c` 启用，或用 `--aria2c-path` 指定路径。

> 放在 BBDown 同目录或系统 `PATH` 中即可被自动识别。专栏导出不经过混流，无需 FFmpeg。
>
> **容器部署**：`linux-musl-x64` / `linux-musl-arm64` 产物为静态链接，无动态依赖，可直接 `COPY` 进 `scratch` / `distroless` 等镜像运行；仓库另提供现成 `Dockerfile`（见上文「Docker」节）。需要混流时在镜像中自带 FFmpeg（或使用 `--mux none` 跳过混流）。

## 快速开始

以下为命令行（CLI）用法；Windows 用户也可直接使用图形界面 BBDown.GUI，覆盖视频 / 番剧 / 直播录制 / 专栏导出，无需命令行操作。

```bash
# 下载一个视频（默认下载最高清晰度）
BBDown "https://www.bilibili.com/video/BV16h4y137YS"

# 仅解析，不下载，查看可用流
BBDown "BV16h4y137YS" -i

# 仅下载音频
BBDown "BV16h4y137YS" -W v

# 指定清晰度与编码优先级
BBDown "BV16h4y137YS" -q "1080P 高码率" -e "avc,flac"

# 下载并另存独立封面（-w c），自动内嵌封面
BBDown "BV16h4y137YS" -w c

# 下载番剧 / 课程
BBDown "ep68540" --api tv

# 只看下一个 UP 的投稿列表，不下载
BBDown "space402787936" --info-only

# 下载一篇专栏并导出为 Markdown
BBDown cv51908655
BBDown opus1230485246732926996

# 录制一个直播间（默认原画清晰度，短号自动换算）
BBDown "https://live.bilibili.com/12345"

# 指定直播清晰度（250 超清 / 400 蓝光 / 10000 原画）
BBDown "live12345" -lq 400

```

### 支持的输入

- **视频页 URL**：`https://www.bilibili.com/video/BV...`（可用 `?p=` 指定分 P）
- **短链**：`b23.tv/...`
- **裸编号**：`av{数字}`、`BV{字符}`、`ep{数字}`、`ss{数字}`、纯数字按 `ep` 解析（如 `402787936`）、`space{mid}`
- **番剧 / 影视 / 课程**：`/bangumi/play/...`、`/cheese/...`、番剧 `md{数字}` 详情页（如 `https://www.bilibili.com/bangumi/media/md2539`，或简写 `md2539`）、`/bangumi/play/ss{季_id}`（或简写 `ss{数字}`）。`md` 与 `ss` 两种入口**均默认下载整季全部正片分集**（不含 OP/ED/PV 等 `section` 内容，可用 `-p` 指定具体集）；`ep{数字}` 则只下载该单集。
- **合集 / 系列**：UP 主空间的 `lists/` 页面（`business=space_collection` 为合集，`business=space_series` 为系列）
- **收藏夹**：UP 主空间的 `favlist` 页面
- **稍后再看**：`https://www.bilibili.com/watchlater/`、`https://www.bilibili.com/watchlater/#/list`、`https://www.bilibili.com/list/watchlater`（整个列表按添加顺序作为大列表下载，多 P 自动展开，支持 `-p` / `-iap`；接口私有，需登录 Cookie）。分享链接带 `bvid` / `oid` 参数时只下载该单个视频。
- **空间投稿列表**：UP 主空间首页 / `upload/video` / `video?tid=0`，也可直接传 UP mid（`402787936`）或 `space402787936`。默认按**最新发布**（`pubdate`）倒序拉取**全部**投稿；课堂视频、无法解析的稿件（直播回放 / 充电专属 / 已删除等）会**跳过并告警**，不中断整批。
- **专栏 / 图文**：`https://www.bilibili.com/opus/{opus_id}`、`https://www.bilibili.com/mobile/opus/{opus_id}`、`https://www.bilibili.com/read/cv{cv_id}`、`https://www.bilibili.com/read/mobile/{cv_id}`，以及前缀写法 `opus:{opus_id}` / `opus{opus_id}` / `cv{cv_id}`。专栏导出为 Markdown 文件，详见 [专栏 / 图文导出](#专栏--图文导出)。
- **直播间**（独立录制链路）：`https://live.bilibili.com/{房间号}`、`https://m.live.bilibili.com/{房间号}`、`live{房间号}`（直写形式，不写冒号，如 `live12345`；房间号短号自动换算为真实 ID）。裸数字按 `ep` 解析、不进入直播链路；直播链路不依赖 `WorkContext`，直接拉取 `http_stream` + `flv` 流地址录制。

> 命令行、配置文件与 `serve` 接口使用同一套写法。

## 参数说明

### 解析模式

| 参数        | 简写 | 说明                                                                                        |
| ----------- | ---- | ------------------------------------------------------------------------------------------- |
| `--api`     | `-a` | 指定 API 解析通道：`web` / `tv` / `app` / `intl`，默认 `web`，忽略大小写（详见脚注 [^api]） |
| `--host`    |      | 指定 BiliPlus host（详见脚注 [^host]）                                                      |
| `--ep-host` |      | 指定 BiliPlus EP host（详见脚注 [^ep-host]）                                                |
| `--tv-host` |      | 自定义 TV 端接口请求 Host（用于代理 `api.snm0516.aisee.tv`）                                |
| `--area`    |      | 使用 BiliPlus 时指定区域：`hk` / `tw` / `th`                                                |

> `--api` 单值选择解析通道；`web` 之外的模式按需携带对应凭据（`--access-token` 等）。番剧 / 课程在 `tv` 或 `intl` 下不可用时自动回退 `web`。

### 清晰度与编码

| 参数                    | 简写   | 说明                                                     |
| ----------------------- | ------ | -------------------------------------------------------- |
| `--encoding-priority`   | `-e`   | 视频及音频编码选择优先级（详见脚注 [^encodingpriority]） |
| `--dfn-priority`        | `-q`   | 画质优先级（详见脚注 [^dfnpriority]）                    |
| `--audio-quality`       | `-aq`  | 音频档位优先级（详见脚注 [^audioquality]）              |
| `--video-ascending`     | `-va`  | 视频升序（最小体积优先）                                 |
| `--audio-ascending`     | `-aa`  | 音频升序（最小体积优先）                                 |
| `--interactive-quality` | `-iaq` | 交互式选择清晰度                                         |
| `--hide-streams`        | `-hs`  | 不显示所有可用音视频流                                   |
| `--info-only`           | `-i`   | 仅解析而不进行下载                                       |
| `--all`                 |        | 展示所有分 P 标题                                        |

> 同时指定 `-e` 与 `-q` 时，以命令行书写的先后为准（写在前的优先）。`-q` 仅作用于清晰度筛选，编码仍由 `-e` 控制。

> **封装对 `-q` 的影响：**
>
> - **DASH**：先按 `-q` 请求一次，再额外以最高清晰度（qn=127）请求一次以取得「免二压 / 原始画质」视频轨（两次结果取并集）。因此 DASH 比 FLV 多一次播放地址请求。
> - **FLV**：固定以最高清晰度（qn=127）请求播放地址，用户通过 `-q` 指定的清晰度优先级对它**不生效**——FLV 只会产出单一最高清视频流（仍可按 `-e` 选编码）。

### 下载内容

| 参数                 | 简写   | 说明                                                       |
| -------------------- | ------ | ---------------------------------------------------------- |
| `--get`              | `-g`   | 设置下载内容字符集，默认 `avmsCiM`（详见脚注 [^get]）      |
| `--with`             | `-w`   | 在 `--get` 基础上追加内容字符                              |
| `--without`          | `-W`   | 在 `--get` 与 `--with` 基础上移除内容字符                  |
| `--danmaku-formats`  | `-ddf` | 指定需下载的弹幕格式（详见脚注 [^danmakuformats]）         |
| `--comments-count`   | `-cn`  | 下载评论区前 N 条评论（默认 `0`，即不下载）                |
| `--comments-sort`    | `-cs`  | 评论排序：`hot`（热度，默认）或 `time`（最新）             |
| `--comments-formats` | `-cf`  | 指定评论导出格式（详见脚注 [^commentformats]）             |
| `--mux`              | `-m`   | 混流方式：`none` / `mpeg4`（默认）/ `mp4box` / `mkv`       |
| `--post-process`     |        | 指定外部后处理进程（详见脚注 [^postprocess]）              |
| `--allow-preview`    | `-P`   | 允许下载充电专属视频的试看片段（详见脚注 [^allowpreview]） |
| `--lang`             |        | 设置混流音频语言代码，如 `chi`、`jpn` 等                   |

内容字符对照表：

| 字符 | 含义         | 字符 | 含义                         |
| ---- | ------------ | ---- | ---------------------------- |
| `a`  | 音频         | `m`  | 嵌入元数据（标题 / 描述等）  |
| `v`  | 视频         | `M`  | 专栏 YAML front matter       |
| `c`  | 独立封面文件 | `o`  | 评论                         |
| `C`  | 封面嵌入     | `O`  | 全部评论（含楼中楼全部回复） |
| `d`  | 弹幕         | `S`  | AI 字幕                      |
| `i`  | 专栏图片     | `s`  | 字幕                         |

[^get]: 最终内容为 `--get ∪ --with − --without`，多个 `--get` / `--with` / `--without` 自动合并：

    - `o` / `O` 同时出现按 `O` 处理，不警告；
    - `c` 与 `C` 相互独立，可同时选择；
    - `S` 不依赖 `s`；
    - 仅当没有 `a` / `v` 时 `C` / `m` 不生效并警告。

#### 旧选项参考表

| 旧选项                  | 新写法                                     |
| ----------------------- | ------------------------------------------ |
| `--audio-only` / `-a`   | `-W v`（默认集去掉视频）                   |
| `--video-only` / `-v`   | `-W a`（默认集去掉音频）                   |
| `--danmaku-only` / `-d` | `-g d`（默认集不含弹幕，重设为仅弹幕）     |
| `--cover-only` / `-c`   | `-g c`（默认集不含独立封面，重设为仅封面） |
| `--sub-only` / `-s`     | `-g s`                                     |
| `--danmaku` / `-dd`     | `-w d`（默认集上附加弹幕）                 |
| `--no-sub`              | `-W s`                                     |
| `--no-cover`            | `-W C`（默认集不含独立封面，去掉封面混流） |
| `--no-metadata`         | `-W m`                                     |
| `--full-comment`        | `-w O`（另需 `--comments-count > 0`）      |
| `--allow-ai`            | `-w S`（默认集含字幕，附加 AI 字幕）       |
| `--no-images`           | `-W i`（专栏）                             |

### 直播录制

| 参数             | 简写  | 说明                                                                                                                                          |
| ---------------- | ----- | --------------------------------------------------------------------------------------------------------------------------------------------- |
| `--live-quality` | `-lq` | 直播录制清晰度：30000 杜比 / 20000 4K / 15000 2K / 10000 原画（默认）/ 400 蓝光 / 250 超清 / 150 高清 / 80 流畅；未登录时服务端通常只给到 250 |

> 直播录制为独立链路：传入直播间地址后，BBDown 拉取 `http_stream` + `flv` 流地址并分段落盘；录制中 `Ctrl+Break` 可停录并把分段合并为单个 `mp4`，`Ctrl+C` 则中断录制、保留已落盘分段（不合并）。清晰度档位数值越大越清晰（30000 杜比 / 20000 4K / 15000 2K / 10000 原画 / 400 蓝光 / 250 超清 / 150 高清 / 80 流畅）。

### 下载方式与性能

| 参数               | 简写     | 说明                                                              |
| ------------------ | -------- | ----------------------------------------------------------------- |
| `--aria2c`         | `-aria2` | 调用 aria2c 进行下载（需自行准备二进制）                          |
| `--aria2c-args`    |          | 调用 aria2c 的附加参数（详见脚注 [^aria2cargs]）                  |
| `--single-thread`  | `-st`    | 使用单线程下载，用于不支持 Range 的服务器；不加此选项即默认多线程 |
| `--delay-per-page` |          | 分 P 之间下载的间隔时间，单位秒，默认 `0`（无间隔）               |
| `--upos-host`      |          | 自定义 upos（CDN）服务器                                          |
| `--no-force-host`  |          | 不强制替换下载服务器 host（默认强制替换，加此选项才不替换）       |
| `--allow-pcdn`     |          | 不替换 PCDN 域名，仅在正常情况与 `--upos-host` 均无法下载时使用   |
| `--no-force-http`  |          | 下载音视频默认以 HTTP 替换 HTTPS；加此选项则不降级（保持 HTTPS）  |

### 账号与凭据

| 参数             | 简写     | 说明                                           |
| ---------------- | -------- | ---------------------------------------------- |
| `--cookie`       |          | 字符串 cookie，用于下载网页接口的会员内容      |
| `--access-token` | `-token` | access_token，用于下载 TV / APP 接口的会员内容 |
| `--user-agent`   | `-ua`    | 指定 user-agent；不指定则使用随机 user-agent   |

> 推荐用 `login`（WEB）/ `login --tv` / `login --app` 扫码登录后自动保存凭据，避免手动粘贴 `--cookie` / `--access-token`。

### 文件、路径与调试

| 参数                   | 简写   | 说明                                                                            |
| ---------------------- | ------ | ------------------------------------------------------------------------------- |
| `--file-pattern`       | `-F`   | 自定义单 P 存储文件名（支持内置变量，见下）                                     |
| `--multi-file-pattern` | `-M`   | 自定义多 P 存储文件名（支持内置变量，见下）                                     |
| `--pages`              | `-p`   | 选择分 P（语法详见脚注 [^selectpage]）                                          |
| `--interactive-pages`  | `-iap` | 逐集确认是否下载：[y] 要，[n] 不要，[a] 剩余全部要，[q] 剩余全部不要，回车=不要 |
| `--work-dir`           |        | 设置程序工作目录                                                                |
| `--ffmpeg-path`        |        | 指定 FFmpeg 路径                                                                |
| `--mp4box-path`        |        | 指定 MP4Box 路径                                                                |
| `--aria2c-path`        |        | 指定 aria2c 路径                                                                |
| `--save-records`       |        | 将下载过的视频记录到本地文件，用于后续跳过同一视频                              |
| `--stop-on-error`      |        | 遇到分 P 下载失败时立即停止（详见脚注 [^stoponerror]）                          |
| `--config`             |        | 读取指定的 BBDown 本地配置文件（默认为程序目录下的 `BBDown.config`）            |
| `--debug`              | `-D`   | 输出调试日志                                                                    |

#### 文件名内置变量

单 P 默认文件名：`<videoTitle>`；多 P 默认文件名：`<videoTitle>/[P<pageNumberWithZero>]<pageTitle>`。

可用变量：

| 变量                   | 含义                                             |
| ---------------------- | ------------------------------------------------ |
| `<videoTitle>`         | 视频主标题                                       |
| `<pageNumber>`         | 分 P 序号                                        |
| `<pageNumberWithZero>` | 分 P 序号（前缀补零）                            |
| `<pageTitle>`          | 分 P 标题                                        |
| `<bvid>`               | 视频 BV 号                                       |
| `<aid>`                | 视频 aid                                         |
| `<cid>`                | 视频 cid                                         |
| `<dfn>`                | 视频清晰度                                       |
| `<res>`                | 视频分辨率                                       |
| `<fps>`                | 视频帧率                                         |
| `<videoCodecs>`        | 视频编码                                         |
| `<videoBandwidth>`     | 视频码率                                         |
| `<audioCodecs>`        | 音频编码                                         |
| `<audioBandwidth>`     | 音频码率                                         |
| `<ownerName>`          | 上传者名称                                       |
| `<ownerMid>`           | 上传者 mid                                       |
| `<publishDate>`        | 收藏夹 / 番剧 / 合集发布时间                     |
| `<videoDate>`          | 视频发布时间（分 P 视频与 `<publishDate>` 相同） |
| `<apiType>`            | API 类型（TV / APP / INTL / WEB）                |

> **自定义日期格式**：`<publishDate>` / `<videoDate>` 后可接 `:` + .NET 的 `DateTime` 格式串，例如 `<publishDate:yyyyMMdd>`、`<videoDate:yyyy-MM-dd HH:mm>`。省略格式串时使用默认格式。
>
> **长度限制**：最终文件名按 **UTF-8 字节数截断，上限 200 字节**（约 66 个汉字），超出部分会被裁掉，避免过长路径导致写入失败。

示例：

```bash
# 单 P：标题 + 清晰度
BBDown "BV1xx" -F "<videoTitle>[<dfn>]"

# 多 P：按序号子目录归档
BBDown "BV1xx" -M "<videoTitle>/[P<pageNumberWithZero>]<pageTitle>"

# 用发布日期组织目录
BBDown "BV1xx" -M "<publishDate:yyyy>/<publishDate:MMdd> <pageTitle>"
```

## 子命令

| 子命令  | 说明                                                                                          |
| ------- | --------------------------------------------------------------------------------------------- |
| `login` | 通过 APP 扫描二维码登录账号（默认 WEB；加 `--tv` 登录 TV，加 `--app` 登录 APP），凭据自动保存 |
| `serve` | 以服务器模式运行，提供带鉴权令牌的 HTTP JSON API（详见 [API.md](./API.md)）                   |

### 专栏 / 图文导出

根命令在输入为专栏地址（`https://www.bilibili.com/opus/...`、`https://www.bilibili.com/read/...`、`opus{id}` / `opus:{id}` / `cv{id}`）时自动识别并走专栏导出支路，纯图文动态（非专栏的文章类 opus）也按正文导出为 Markdown，不再误判为专栏。默认会把正文中的图片下载到本地 `<标题>/images/` 子目录，并在 Markdown 中用相对路径引用；专栏 / 图文动态的顶部相册图片随正文一并下载，并置于文档最前。内容集沿用根命令的默认 `avmsCiM`（专栏下仅 `i` / `M` 生效）：默认下载图片、输出 YAML front matter；加 `-W i` 跳过图片、加 `-W M` 不输出 front matter。仅支持**单篇**专栏 / 图文动态，不支持批量 / 合集。

```bash
# 下载一篇专栏并导出为 Markdown（图片下载到 <标题>/images/，含 front matter）
BBDown "https://www.bilibili.com/opus/1230485246732926996"

# 前缀写法：cv 号 / opus id
BBDown cv51908655
BBDown opus1230485246732926996

# 不下载图片、不输出 front matter，适合纯文本归档
BBDown cv51908655 -W i -W M
```

### `serve` 参数

| 参数               | 简写 | 说明                                                                                                          |
| ------------------ | ---- | ------------------------------------------------------------------------------------------------------------- |
| `--listen`         | `-l` | 监听地址，默认 `http://127.0.0.1:23333`                                                                       |
| `--serve-token`    |      | serve 鉴权令牌；未提供且绑定到非回环地址时自动生成并打印，客户端需带 `X-BBDown-Token` 头或 `?token=` 查询参数 |
| `--work-dir`       |      | 所有任务的工作目录，请求中的同名字段会被忽略                                                                  |
| `--host`           |      | API 请求 Host，所有任务统一使用此值；请求体不再能指定 host（防止凭据被导向外部服务器）                        |
| `--ep-host`        |      | 番剧 / 影视 API 请求 Host，所有任务统一使用此值                                                               |
| `--tv-host`        |      | TV 端 API 请求 Host，所有任务统一使用此值                                                                     |
| `--cors-origin`    |      | 仅允许该单一来源跨域调用 serve（CORS）                                                                        |
| `--max-concurrent` |      | 同时下载的任务数上限，默认 0（不限制）。                                                                      |

```bash
# 以默认地址启动服务器（本地回环，免令牌）
BBDown serve

# 指定监听地址与工作目录（非回环将自动要求令牌）
BBDown serve -l http://0.0.0.0:23333 --work-dir "D:/Downloads"

# 显式指定令牌
BBDown serve --serve-token "你的令牌"
```

## 退出码

| 退出码 | 含义                                                |
| ------ | --------------------------------------------------- |
| `0`    | 全部成功                                            |
| `1`    | 存在下载失败的分 P                                  |
| `2`    | 所选分 P 全部为充电专属试看片段，已跳过，未产出文件 |
| `130`  | 用户取消（Ctrl+C）                                  |

> 退出码 `2` 表示：没有任何分 P 因真实故障失败，唯一原因是充电权限。若同时存在充电试看与真实下载失败，退出码为 `1`。

> 直播录制下，`Ctrl+Break` 表示「停录并合并」（正常结束，退出码 `0`）；`Ctrl+C` 表示「中断」（保留已落盘分段、不合并，退出码 `130`）。

## 配置文件

BBDown 支持从配置文件读取参数，避免每次都在命令行重复输入。默认读取程序目录下的 `BBDown.config`，也可通过 `--config` 指定其他路径。

配置文件每行一个参数（与命令行写法一致），以 `#` 开头的行为注释。视频地址也可写在配置文件里。**配置文件只补齐命令行未指定的选项**，同一选项以命令行为准。

```ini
# BBDown.config 示例
--api tv
--dfn-priority 1080P 高码率, 720P 流畅
--cookie SESSDATA=xxxxxx

# 也支持把地址写进配置文件
BV1uv411q7Mv
```

> 带空格的参数（如 `--dfn-priority 1080P 高码率`）在配置文件中直接按原样书写即可，BBDown 会自动按空格切分并去除引号。

## 服务器模式

`BBDown serve` 会在本地启动一个 HTTP 服务器，对外暴露任务增删查的 JSON API，适合与下载器面板、自动化脚本集成。完整接口定义、数据结构与请求示例见 **[API.md](./API.md)**。

> ⚠️ **鉴权说明**：默认监听 `http://127.0.0.1:23333`（回环地址）时**免令牌**即可调用，便于本机脚本使用。一旦绑定到**非回环地址**（如 `0.0.0.0`），BBDown 会强制要求令牌鉴权：若未通过 `--serve-token` 指定，会自动生成一个令牌并打印到控制台，客户端必须携带 `X-BBDown-Token` 请求头或 `?token=` 查询参数；令牌不匹配一律返回 `401`。服务器**默认完全关闭 CORS**（不发送 `Access-Control-Allow-Origin` 头），仅当显式 `--cors-origin <url>` 时才对该单一来源开放；无论是否开 CORS，令牌都只防未授权调用、不验证调用方身份，因此**请勿暴露到公网**。需要跨机器访问时请自行加反向代理与 TLS，并显式指定 `serve -l http://0.0.0.0:23333`。

> **安全机制一览**：
>
> - **SSRF 防护**：回调地址拒绝内网 / 回环地址；IPv4-mapped IPv6（如 `::ffff:169.254.169.254`）先归一化为 IPv4 再判定，云元数据地址无法绕过过滤；连接建立前二次校验。
> - **凭据防外泄**：host / 工作目录由服务端启动参数固定，请求体无法覆盖，凭据不会被导向外部服务器。
> - **任务可中断**：取消令牌沿触网路径贯通，`Ctrl+C` 关停 serve 可中断排队中任务的解析与播放信息请求。
> - **任务天然去重**：任务标识为 `ResourceId` 规范字符串（如 `av170001`、`season2539`、`fav100_200`），值相等自动去重。
> - **安全回归测试**：认证矩阵与 SSRF 补漏用例随 CI 持续守护上述行为。

## 数据文件格式

### 凭据：`BBDown.data`

WEB / TV / APP 三类凭据**全部合并进同一个 `BBDown.data` 的同一个 JSON 对象**，由 `login`（默认 WEB）、`login --tv`、`login --app` 扫码登录后写入。对应类型未登录时其字段为 `null`，结构如下：

```json
{
    "cookie": "DedeUserID=xxx; DedeUserID__ckMd5=xxx; SESSDATA=xxx; bili_jct=xxx",
    "refresh_token": "WEB 登录时获取的刷新令牌",
    "ts": 1700000000,
    "tv_access_token": "TV 扫码登录获取的令牌（未登录为 null）",
    "tv_ts": 1700000000,
    "app_access_token": "APP 扫码登录获取的令牌（未登录为 null）",
    "app_ts": 1700000000
}
```

| 字段               | 类型    | 说明                              |
| ------------------ | ------- | --------------------------------- |
| `cookie`           | string? | 完整 Cookie 字符串                |
| `refresh_token`    | string? | 刷新令牌，用于主动续期 Cookie     |
| `ts`               | number? | WEB 凭据签发时间戳（Unix 秒）     |
| `tv_access_token`  | string? | TV 扫码登录获取的 `access_token`  |
| `tv_ts`            | number? | TV 凭据签发时间戳（Unix 秒）      |
| `app_access_token` | string? | APP 扫码登录获取的 `access_token` |
| `app_ts`           | number? | APP 凭据签发时间戳（Unix 秒）     |

### 下载归档记录：`BBDown.archives`

启用 `--save-records` 后写入，纯文本，**每行一条记录，字段以制表符（Tab，`\t`）分隔**：

```
<aid>\t<cid>\t<保存路径>
```

| 字段         | 说明                                                                             |
| ------------ | -------------------------------------------------------------------------------- |
| `<aid>`      | 视频 av 号（数字字符串）                                                         |
| `<cid>`      | 分 P 的 cid                                                                      |
| `<保存路径>` | 该分 P 完整下载成功（含混流）后的本地文件路径；可选，记录被删/移动后会被重新下载 |

- 仅在该分 P 完整成功（含混流）后才**追加**写入；键为 `(aid, cid)`，同一视频不同分 P 互不干扰。
- 再次运行同一视频时，`CheckArchive` 会跳过已记录且文件仍存在的分 P；文件被删/移走则视为未下载，重新下载。

## 常见问题

**Q：下载提示需要大会员？**

部分番剧、课程需要大会员权限。请使用 `login`（WEB）/`login --tv`/`login --app` 登录对应账号，或通过 `--cookie` / `--access-token` 传入凭据。

**Q：为什么有时只能下到最低清晰度？**

部分视频的非会员下载会被限制为低清晰度，这是 B 站服务端策略，与工具无关。登录大会员账号通常可解除限制。

**Q：断点续传是怎么工作的？**

多线程分片与断点续传由 Downloader 库实现：续传元数据内嵌在 `.download` 临时文件末尾并周期性刷新，恢复下载时先比对服务端文件大小，若 URL 指向的内容已变化则自动删除临时文件重新下载，不会产出损坏的混合文件。

**Q：下载中断后重试会不会整 P 重下？**

不会。分片级重试：单个分片瞬态故障自动退避重试（上限 5 次）并从断点续下；只有分片内容已变化时才会重下该片。

**Q：FLV 模式下 `-q` 怎么没生效？**

FLV 封装固定以最高清晰度（qn=127）请求播放地址，用户的清晰度优先级对它不生效；如需按清晰度选择，请使用默认的 **DASH** 封装，并通过 `-q` 指定。

**Q：如何让下载更快？**

内置下载引擎默认分片并发 32，多 P 视频可用 `--delay-per-page` 控制间隔以免触发风控；也可配合 `--aria2c` 调用 aria2c 多线程下载。

**Q：aria2c 怎么用？**

下载 aria2c 二进制并放在 BBDown 同目录或 `PATH` 中，然后加 `--aria2c` 即可。可用 `--aria2c-args` 追加自定义参数（默认已含 `-x16 -s16 -j16 -k 5M`）。

**Q：配置文件和命令行的优先级？**

命令行未显式给出的选项，才会由配置文件补齐；命令行已给出的以命令行为准。

**Q：和原版 BBDown 有什么区别？**

本分支在解析、安全与易用性上做了系统性增强，对照表见 [与原版 BBDown 的差异](#与原版-bbdown-的差异)：WBI 签名降低风控概率、serve 模式带 SSRF 防护与令牌鉴权、下载引擎统一为 Downloader 库（分片重试 + 自愈续传）、新增直播录制 / 专栏导出 / 空间投稿 / 稍后再看、图形界面与 AOT 单文件发布等。

**Q：WBI 签名解决什么问题？**

B 站 web 接口要求 WBI 签名，未签名的请求更容易触发风控。BBDown 对 playurl、view、字幕、空间列表均做标准 WBI 签名；未探测到账号时自动退化为不签名。

**Q：serve 模式的安全如何保证？**

回环地址免令牌、非回环地址强制令牌（`X-BBDown-Token` 头或 `?token=` 查询参数）；回调地址有 SSRF 防护（拒绝内网 / 回环，IPv4-mapped IPv6 先归一化再判定，连接前二次校验）；CORS 默认关闭；host 与工作目录由服务端启动参数固定，请求体无法覆盖；`Ctrl+C` 关停可中断排队中任务的解析请求。

**Q：如何把 serve 安全地暴露到局域网 / 公网？**

请勿直接暴露到公网（令牌不验证调用方身份）。需要跨机器访问时，请用反向代理 + TLS（如 Caddy / Nginx）包裹，并显式 `serve -l http://0.0.0.0:23333`；仅本机脚本使用则保持默认回环监听即可。

## 致谢

- [nilaoda/BBDown](https://github.com/nilaoda/BBDown) 用于原版 BBDown：本项目由其衍生，登录、接口解析等核心设计沿袭自原作者 nilaoda。
- [QRCoder](https://github.com/codebude/QRCoder) 用于生成扫码登录二维码。
- [protobuf](https://github.com/protocolbuffers/protobuf) 用于 APP 端 gRPC 消息序列化。
- [gRPC](https://github.com/grpc/grpc) 用于 APP 端接口协议。
- [System.CommandLine](https://github.com/dotnet/command-line-api) 用于命令行解析。
- [bilibili-API-collect](https://github.com/SocialSisterYi/bilibili-API-collect) 用于 B 站接口文档参考（随仓库以子目录形式附带）。
- [bilibili-grpc-api](https://github.com/SeeFlowerX/bilibili-grpc-api) 用于 APP 端 gRPC 协议定义。
- [FFmpeg](https://github.com/FFmpeg/FFmpeg) 用于音视频下载与混流。
- [GPAC](https://github.com/gpac/gpac) 用于 MP4Box 混流。
- [aria2](https://github.com/aria2/aria2) 用于 aria2c 多线程下载。
- [YY-Thunks](https://github.com/Chuyu-Team/YY-Thunks) 用于 Win7 兼容构建时在链接期补齐旧系统缺失的 API。
- [VC-LTL](https://github.com/Chuyu-Team/VC-LTL) 用于 Win7 兼容构建时静态消除 api-ms-win-crt 依赖。
- [PublishAotCross](https://github.com/MichalStrehovsky/PublishAotCross) 用于在 Windows 上本地交叉发布 Linux 目标。

## 许可证

本项目基于 [MIT](LICENSE) 许可证开源。

---

_BBDown 2.0 · 仅供个人学习与研究使用，请遵守 B 站相关服务条款，勿将下载内容用于商业或侵权用途。_

[^selectpage]: 选择分 P 语法：

    - `all` 全部
    - `8` 单集
    - `1,2,5` 逗号列表
    - `3-5` 闭区间（含两端：3,4,5）；`3-3` 仅第 3 集
    - `16-` 开区间，到末集
    - `-22` 开区间，从首集到 22
    - `1,2,3-3,4-5,6-10,15-latest` 混合写法
    - `latest` / `new` 最后一集（最新一集）
    - `last` / `LAST` 倒数第二集
    - 关键字大小写不敏感；`latest` 可写作 `new`；越界数字自动夹紧到有效边界；非法项忽略并提醒
    - 以 `-` 开头的表达式需在命令行加引号：`-p "-22,25-27,33"`

[^aria2cargs]: 调用 aria2c 的附加参数，含空格的参数用引号包裹。默认参数包含 `-x16 -s16 -j16 -k 5M`。

[^encodingpriority]: 视频及音频编码的选择优先级，逗号分隔。例：`hevc,avc,flac,eac3,m4a`

[^dfnpriority]: 画质优先级，逗号分隔。例：`8K 超高清, 1080P 高码率, HDR 真彩, 杜比视界`

[^audioquality]: 音频档位优先级，逗号分隔。可填音质名或音质 id，匹配不分先后、去重后按下标赋权（写在前优先）。例：`杜比全景声, Hi-Res 无损, 192K` 或 `30250, 30251, 30280`。杜比全景声 / 杜比音效 / Hi-Res 无损需登录大会员 Cookie，否则接口不下发对应轨道；未指定时取各通道默认最高音质。

[^danmakuformats]: 指定需下载的弹幕格式，逗号分隔，默认全部下载（如 `xml,ass`）。

[^commentformats]: 指定评论导出格式，逗号分隔，默认同时导出 `json,txt`。`json` 为完整结构化数据（含每条评论的会员等级、点赞数、IP 属地、配图与楼中楼）；`txt` 为便于阅读的纯文本。注意未登录时接口不下发 `IP属地`，故 txt 中相应字段会缺失——属预期。

[^api]: 各通道特性：`web` 为常规网页接口（WBI 签名，UGC 需 Cookie）；`tv` 为 TV 端接口（需 TV access_token，番剧 / 大会员内容）；`app` 为 APP 端 gRPC 接口（番剧仅 HEVC）；`intl` 为国际版接口（东南亚区域，需 access_token）。番剧输入走 `intl` 需给出具体 `ep` 号。

[^stoponerror]: 遇到分 P 下载失败时立即停止，而不是继续下载其余分 P。默认继续，并在末尾汇总失败的分 P 后非零退出。

[^allowpreview]: UP 主的充电专属稿件，在当前账号没有充电权限时接口不会报错，而是照常返回成功并只下发几分钟的试看片段。BBDown 默认会在下载前识别并跳过（退出码 `2`），避免产出被报告为「下载成功」的残片。加此选项则保留试看片段，输出文件名带 `[试看]` 前缀以便与完整视频区分。登录一个已为该 UP 主充电的账号（`BBDown login`）即可正常下载完整视频，无需此选项。

[^postprocess]: 指定外部后处理进程（可执行文件路径）。下载完成后轨道文件会交给该进程处理，是否加密由处理方自行判断（无需处理时退出码 0 且无产物，原文件照常混流）；成功产物替换原文件参与混流，进程不可用或处理失败时静默保留原文件。本程序不感知其语义。完整协议见 [PROTOCOL.md](./PROTOCOL.md)。

[^host]: 指定 BiliPlus host。使用 BiliPlus 需要 access_token、不需要 cookie；解析服务器能够获取你账号的大部分权限，请谨慎使用！

[^ep-host]: 指定 BiliPlus EP host，用于代理 `api.bilibili.com/pgc/view/web/season`；大部分解析服务器不支持代理该接口。
