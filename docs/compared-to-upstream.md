# 与原版 BBDown 的差异对照

本仓库是 [nilaoda/BBDown](https://github.com/nilaoda/BBDown) 的一个增强分支（fork，远程 `KaiHuaDou/BBDown`）。
本文档逐项列出本分支相对原版的新增能力与行为改进，供选用 / 迁移时参考。

> 对照基准：原版 `nilaoda/BBDown` 上游主干（README 与源码）。
> 能力声明均已对照本仓库源码核实，各条目后附源码位置（文件：类型/方法）。

## 1. 能力对照总表

| 维度 | 原版 nilaoda/BBDown | 本分支（KaiHuaDou/BBDown） |
| --- | --- | --- |
| **顶层子命令** | 主命令 + `logintv` 等离散命令 | `login`（统一 `--tv` / `--app`）、`opus`（新增）、`serve` 三个子命令，主命令解析视频/番剧/课程/收藏/空间 |
| **WEB Cookie 续期** | 仅列于 TODO（「自动刷新 cookie」未实现） | 登录保存 `refresh_token`，下载前尝试用 **RSA-OAEP(SHA-256)** 加密请求主动续期 `cookie` |
| **凭据存储** | 分离文件 `BBDownTV.data` / `BBDownApp.data`（APP 还需抓包后复制） | 单一 **`BBDown.data`**（同一 JSON 对象，源生成器序列化，AOT 安全）；WEB/TV/APP 分别落盘、互不覆盖 |
| **APP 端登录** | 无法自动获取，需抓包 `authorization: identify_v1` 并写入 `BBDownApp.data` | `login --app` **扫码登录** APP 账号，自动保存 |
| **TV 端登录** | 独立 `logintv` 子命令 | `login --tv`（与 `login` / `login --app` 统一为一个子命令的可选标志） |
| **AOT 原生发布** | 未提供；依赖运行时反射 | 代码已改造为 **AOT 安全**（`System.Text.Json` 源生成器替代反射）；AOT 在 `BBDown/Directory.Build.props` 默认开启，`dotnet publish BBDown -r <RID> -c Release` 即产出单文件原生二进制 |
| **WBI 签名降风控** | playurl / view 等接口明文或仅简单 sign | 对 playurl（wbi/playurl）、view（wbi/view）、字幕（player/wbi/v2）、空间列表（space/wbi/arc/search）均做标准 WBI 签名；未探测账号时退化为不签名 |
| **serve 鉴权** | 基础令牌 | 回环地址免令牌；非回环地址强制令牌（`X-BBDown-Token` 头或 `?token=` 查询），否则 401 |
| **serve 安全** | 请求体基本透传 | 请求契约收窄为受控子集 DTO；host 三兄弟与 work-dir 服务端固定；回调地址 **SSRF 防护**（拒绝内网/回环，连接前二次校验）；**CORS 默认关闭** |
| **专栏/图文导出** | 无 | 新增 `opus` 子命令，将专栏/图文动态导出为 Markdown + 图片目录 |
| **UP 主空间投稿列表** | 无 | 新增 `SpaceListFetcher` 与 space URL 解析，可下载某 UP 全部投稿 |
| **充电专属试看识别** | 无专门处理（按普通失败或下载残缺片段） | `IsTruncatedPreview` 双条件判定，命中抛 `ChargedPreviewException`，退出码 2 表示全部为试看（可 `--allow-preview` 放行） |
| **断点续传** | 基础续传 | 每条流维护 `<路径>.bbdown.part` 数据 + `<路径>.bbdown.json` **SHA256 指纹清单**，支持单流粒度与合集/多 P 粒度续传 |
| **文件名日期格式** | 固定 `yyyy-MM-dd_HH-mm-ss` | 支持自定义 `<publishDate:格式>` / `<videoDate:格式>`（任意 .NET `DateTime` 格式串） |
| **文件名长度** | 无特殊处理，超长路径易写入失败 | 按 **UTF-8 字节数截断，上限 200 字节**，并清理非法字符 / 保留设备名 / 处理首尾点 |
| **cheese 课程** | 仅 Web；存在冗余 `ss` 请求 | 消除冗余 `ss` 请求；`--intl-api` 对其**自动回退 WEB**；**过滤锁定分集**（`BuildPages` status==2） |
| **解析模式优先级** | 未明确文档化 | 明确 `DetermineApiType` 优先级 **TV > APP > INTL > WEB**；`--app-api --intl-api` 同给走 APP |
| **FLV / DASH 封装** | 通用说明 | DASH 先按 `-q` 请求再额外以 `MaxQn(127)` 取原始画质轨（两次并集）；FLV 固定 `qn=127`、忽略 `-q` |
| **归档记录** | `--save-archives-to-file`（旧竖线格式） | `--save-records` 写 Tab 分隔 `BBDown.archives`（`<aid>\t<cid>\t<路径>`），键为 `(aid, cid)` |
| **测试覆盖** | 较少 | **870+ 单元测试**（Core + BBDown.Tests，含 gRPC 打包往返、cheese 过滤、serve 安全、断点续传清单、文件名截断、WBI 签名等） |
| **代码现代化** | 传统结构 | god-class 拆分（如 `BBDownUtil` 按归属拆分）、现代化命名、`System.Threading.Lock`、`[GeneratedRegex]`、`Nullable enable` + `TreatWarningsAsErrors`、net9.0 |
| **直播录制** | 无 | 新增独立直播链路，直播间地址直录（`live:` / `live.bilibili.com`），`--live-quality` 选清晰度（默认原画 10000，可选 250 超清 / 400 蓝光 / 15000 2K / 20000 4K / 30000 杜比），分段 FLV 落盘后合并为 mp4（`Ctrl+Break` 停录合并 / `Ctrl+C` 中断保留分段）；录制状态机具备断流退避重连、CDN failover、编码锁定 |

## 2. 分主题详述

### 2.1 子命令与入口

- **`login`**：统一入口，无标志登录 WEB，加 `--tv` 登录 TV，加 `--app` 登录 APP（`BBDown/Program.cs`：`loginCommand` 的 `SetAction` → `Login.Web/TV/App`）。原版 `logintv` 已合并进 `login --tv`。
- **`opus`**：新增子命令，导出专栏/图文为 Markdown（`BBDown/CommandLineInvoker.cs`：`GetOpusCommand`；`BBDown/Program.cs`：`rootCommand.Subcommands.Add(... GetOpusCommand(...))`）。
- **`serve`**：服务器模式，选项含 `--listen` / `--serve-token` / `--work-dir` / `--host` / `--ep-host` / `--tv-host` / `--cors-origin` / `--max-concurrent`（`BBDown/Program.cs`：`BuildServeCommand`）。
- 主命令解析范围：`av` / `BV` / `ep` / `ss` / `md`、合集（`listBizId`）/ 系列（`seriesBizId`）、收藏夹（`favId`）、空间（`spaceMid`）、cheese（`cheese:`）（`BBDown/InputResolver.cs`：`GetAvIdAsync`）。

### 2.2 登录与凭据管理

- **三种扫码登录**共用同一轮询编排（`BBDown/Login.cs`：`RunQrLoginAsync` + `QrLoginPlan`），仅生成/轮询/解释/落盘环节不同：
    - WEB：`Login.Web`，扫码后从 query / `Set-Cookie` / crossDomain CookieContainer 多源合并出 `DedeUserID` `DedeUserID__ckMd5` `SESSDATA` `bili_jct` 四件套（`BuildWebCookieResilient`）。
    - TV / APP：`Login.TV` / `Login.App`，用各自 `appkey`/`secret`（TV: 云视听小电视；APP: 手机粉版），扫码拿 `access_token`（`LoginWithAppKey`）。
- **WEB Cookie 主动续期**：仅当本地持有 `refresh_token` 时尝试；先问 `/x/passport-login/web/cookie/info` 是否需要刷新，需要才走 RSA-OAEP(SHA-256) 签名 `refresh_{ts}` → 取 `refresh_csrf` → POST `/x/passport-login/web/cookie/refresh` → confirm 全流（`Login.cs`：`TryRefreshWebCookieIfStaleAsync` / `RefreshWebCookieAsync` / `MakeCorrespondPath`，公钥 `RefreshRsaPublicKey`）。任一步失败回退原 cookie，不阻断下载。
- **单一凭据文件**：`BBDown.data`，同一 JSON 对象含 `cookie` / `refresh_token` / `ts` / `tv_access_token` / `tv_ts` / `app_access_token` / `app_ts`（未登录字段为 `null`）；磁盘键 snake_case、属性 PascalCase（`BBDown/CredentialStore.cs`：`Credential` 记录 + `CredentialJsonContext` 源生成器）。每次保存只更新对应字段并合并保留其余字段（`SaveWebCookie` / `SaveTvToken` / `SaveAppToken` 用 `with` 表达式）。类 Unix 落盘权限收紧为 `600`。
- **旧格式不兼容**：旧的纯 cookie 串、`access_token=` 前缀纯文本、`BBDownTV.data` / `BBDownApp.data` / `BBDownRefresh.data` 分离文件均不被识别，反序列化为非法 JSON 时一律按空凭据处理（`CredentialStore.LoadCredential` 的 `catch`）。

### 2.3 WBI 签名与风控规避

- **签名算法**（`BBDown.Core/Util/SignUtil.cs`：`WbiSign`）：剔除已有 `w_rid`，对含 `wts` 的参数按 key 升序排序，值做 encodeURIComponent 风格编码（保留 `A-Za-z0-9-_.~`，过滤 `! ' ( ) *`，其余 UTF-8 字节大写十六进制转义），末尾拼 `mixinKey` 取 MD5 得 `w_rid`。算法对齐 `bilibili-API-collect/docs/misc/sign/wbi.md`。
- **应用范围**：
    - playurl：`/x/player/wbi/playurl`（`BiliApi.PlayUrlWebPath`，`PlayUrlClient` 非番剧分支经 `SignUtil.WbiSign` 签名）。
    - view：`/x/web-interface/wbi/view`（`NormalInfoFetcher.cs`：`ViewWbi`）。
    - 字幕：`/x/player/wbi/v2`（`SubUtil.cs`：`PlayerWbiV2`）。
    - 空间列表：`/x/space/wbi/arc/search`（`SpaceListFetcher.cs`：`SpaceArcSearch`）。
- **退化条件**：`cfg.Wbi` 为空（未探测账号）时 `WbiSign` 直接原样返回，不做签名（`SignUtil.WbiSign`：`if (cfg.Wbi.Length == 0) return api;`）。
- **番剧/课程 playurl 不签名**：`PlayUrlClient.FetchAsync` 对 `IsBangumi` 分支走 `req.TvApi ? BuildTvQuery : BuildWebQuery`，不经 `WbiSign`。
- **试看时长依赖 WBI**：充电试看判定需走 WBI 签名的 playurl 才能拿到真实试看时长；非 WBI 的 `x/player/playurl` 返回的 `timelength` 是声称的完整时长，会误判（见 2.7）。

### 2.4 serve 模式

- **路由与方法**（`BBDown/BBDownApiServer.cs`）：
    - `GET /get-tasks/`、`/get-tasks/running`、`/get-tasks/finished`、`/get-tasks/{id}`（任务状态查询）。
    - `POST /add-task`（新增下载任务）。
    - `POST /remove-finished/`、`/remove-finished/failed`、`/remove-finished/{id}`（清理已完成任务）。
    - `POST /stop-task/{id}`（取消单个运行中 / 排队中任务，不影响其他任务）。
- **鉴权**：`FinalizeAuth(url)` 判定监听地址——回环地址（`IsLoopbackUrl`）免令牌；非回环地址 `authRequired = true`，请求须带 `X-BBDown-Token` 头或 `?token=` 查询参数，缺失/错误返回 401（中间件 `context.Response.StatusCode = 401`）。
- **SSRF 防护**：
    - `IsSafeWebHook(uri)` 仅允许公网 `http/https`，拒绝 `localhost`、回环与私网地址。
    - 专用 `WebHookClient` 关闭自动重定向，并在 `ConnectCallback` 中于建立 TCP 连接前对最终端点 IP 做 `IsPrivateAddress` 二次校验（消除 DNS 重绑定 TOCTOU 窗口），拒绝私网/回环/链路本地/未指定地址（`::`）。
- **服务端固定字段**：`host` / `ep-host` / `tv-host` 三兄弟与 `work-dir` 不再出现在请求 DTO（`ServeRequestOptions`），改由 serve 启动参数固定，避免请求不带 cookie 时回落本机 `SESSDATA` 被导向外部服务器（`BBDownApiServer.cs` 注释 §）。
- **CORS 默认关闭**：不指定 `--cors-origin` 时完全不注册 CORS；指定时仅允许该单一来源（`AddCors` → `AllowSpecificOrigin`）。
- **并发度**：`--max-concurrent N>0` 用 `SemaphoreSlim` 限制同时下载任务数，多余任务排队；单个任务内部的下载并行度由多线程下载器自行决定，不再压到 1；`0`（默认）表示不限制（历史行为）。任务模型为 `DownloadTask` / `DownloadStatus`。

### 2.5 专栏/图文导出（Opus）

- **入口与分流**：`opus` 子命令或根命令下识别到专栏地址时，在 `Program.RunApp` 早于 `WorkSetup.Build` 分流到 `OpusDownload.RunAsync`，因此不构造 `WorkContext`、不探测 `ffmpeg`、不经过音视频保存路径（`BBDown/Program.cs`：`RunApp` 的 `opusCommand || OpusInputResolver.TryParse(...)` 分支）。
- **地址解析**（`BBDown.Core/Opus/OpusInputResolver.cs`：`TryParse`，`allowBareId` 门控）：
    - URL：`/opus/<id>`、`/read/cv<id>`、`/read/mobile/<id>`（支持 `//` 协议相对、`m.`、带 query）。
    - 简写：`opus:<id>`、`opus<id>`、`cv<id>`。
    - 裸数字（仅 `opus` 子命令入口 `allowBareId=true` 允许）：≥15 位视为 opus 雪花 id，否则视为 cv id；根命令下裸数字不触发专栏（归属视频链路）。
- **抓取与渲染**（`BBDown.Core/Opus/`）：
    - `OpusFetcher`：拉取专栏详情（依赖 cookie 中的 `buvid3`，旁路后自行 `Buvid.InitAsync`）。
    - `OpusHtmlToMarkdown`：将专栏正文 HTML 转为 Markdown（标签模式用 `[GeneratedRegex]` 集中在 `OpusRegexes`）。
    - `OpusMarkdownRenderer`：渲染为带 YAML front matter（标题/作者/段落数等，可用 `--no-metadata` 关闭）的 Markdown，图片按 `OpusImageUtil` 下载到 `<标题>/images/` 并以相对路径内联。
    - `OpusImageUtil`：归一化图片 URL、按 SHA256 前 8 位命名去重、失败时保留远程链接。
- **产物**：`<标题>.md` + `<标题>/images/` 目录；已存在非空 `.md` 时跳过（与 `MuxFinish.TrySkipExisting` 同语义）。选项 `--no-images`（保留远程链接）、`--no-metadata`（不加 front matter）。

### 2.6 UP 主空间投稿列表

- **解析入口**（`BBDown/InputResolver.cs`）：`space.bilibili.com/{mid}` 及其子路径（`/upload/video`、`/video?tid=0`、合集 `/lists/`、系列 /系列 `?type=series`）统一解析为 `spaceMid:{mid}`；另支持 `space{mid}` 简写。根命令裸数字解析为 `ep:{input}`（av 号须带 `av` 前缀，无回归）。
- **抓取**（`BBDown.Core/Fetcher/SpaceListFetcher.cs`：`FetchAsync`）：
    - 分页调用 `x/space/wbi/arc/search`（带 WBI 签名，`ps=30`），内置风控守卫（`is_risk` / `gaia_res_type` 抛「被风控拦截」）、越界页兜底、`MaxPages=1000` 防死循环。
    - 接口只返回 `aid`，对每条稿件并发回填（并发度 8，`SemaphoreSlim`）一次 `wbi/view` 取 `cid` 并展开多 P，摊平为 `VInfo.PagesInfo`；用 `HashSet<Page>` 去重。
    - 过滤：课堂视频（`is_lesson_video`，wbi/view 必失败）、已转为番剧的稿件（提示用 ep 链接）、回填失败的稿件——统一降级为「跳过并提示」，不中断整体。
    - 候选链上移到 `FetcherRegistry`（`ListBizId` 解析失败时回退按系列重试），各 Fetcher 保持单向无互相调用。

### 2.7 充电专属试看识别

- **判定**（`BBDown/PageDownload.cs`：`IsTruncatedPreview`）：双条件——稿件 `is_upower_exclusive == true`（稿件属性，与账号无关）**且** playurl 下发时长 `actual < full * 0.9` 且 `full - actual >= 30`（秒）。30 秒下限用于避开 `timelength`(ms) 与 `duration`(整秒) 的固有封装误差。
- **异常与退出码**（`BBDown/ChargedPreviewException.cs`、`BBDown/Program.cs`：`RunApp` / `IsChargedPreviewOnly`）：
    - 命中且非 `--allow-preview`、非 `--info`/`--cover-only`/`--danmaku-only` 模式 → 抛 `ChargedPreviewException`，该分 P 跳过。
    - 全部所选分 P 均为试看 → 退出码 **2**；混合（部分试看 + 部分真实失败）→ 退出码 **1**（使 2 成为强断言）；`Ctrl+C` → 退出码 **130**。
    - `--allow-preview`：输出文件名末段加 `[试看]` 前缀（`SavePath.ApplyPreviewPrefix`），退出码 0。
    - 信息/封面/弹幕模式仅提示，不中止。
- **重试排除**：`ShouldRetry` 明确将 `ChargedPreviewException` 排除在可重试之外（充电权限不会因重试改变）。

### 2.8 断点续传

- **机制**（`BBDown/PartFile.cs`：`PartFile` / `PartManifest` / `Fingerprint`）：每条流维护 `<路径>.bbdown.part` 数据文件与 `<路径>.bbdown.json` **SHA256 指纹清单**（含文件大小、分片范围、校验和），下载前比对指纹决定是否续传。
- **粒度**：支持单流粒度续传，以及合集/多 P 粒度（每个分 P 独立清单）；中断后重跑命令可从上次进度继续。

### 2.9 文件名与模板

- **清洗与截断**（`BBDown.Core/Util/FileNameUtil.cs`：`GetValidFileName`）：
    - 非法字符（`" < > | : * ? \ /` 及 0–31 控制字符）替换为 `_`。
    - Windows 保留设备名（`CON`/`PRN`/`AUX`/`NUL`/`COM1-9`/`LPT1-9`，含任意扩展名）前加 `_`。
    - 首尾空格/点清理；开头点转为隐藏文件时加 `_` 前缀。
    - 按 **UTF-8 字节数截断，上限 `MaxBytes = 200`**，代理对整体保留避免切出无效字符。
- **日期模板**（`BBDown/SavePath.cs`：`Format`）：`<publishDate:格式>` / `<videoDate:格式>` 支持任意 .NET `DateTime` 格式串；未指定时默认 `yyyy-MM-dd_HH-mm-ss`。其余占位符：`videoTitle` `pageNumber` `pageNumberWithZero` `pageTitle` `bvid` `aid` `cid` `ownerName` `ownerMid` `dfn` `res` `fps` `videoCodecs` `videoBandwidth` `audioCodecs` `audioBandwidth` `apiType`。

### 2.10 课程（cheese）解析

- **消除冗余 `ss` 请求**：cheese 输入 `cheese/ss` 直接取 `season_id` 拉整季，不再「先请求取首集 ep_id、再拉整季」（`InputResolver.ResolveCheeseAsync`；`CheeseInfoFetcher.FetchAsync`）。
- **intl 自动回退 WEB**：`NormalizeOptionsAfterFetch` 在 cheese 场景下将 `--intl-api` 回退为 WEB（`BBDown/Program.cs`）。
- **过滤锁定分集**：`CheeseInfoFetcher.BuildPages` 跳过 `status == 2`（未购买/锁定）的分集；抽为纯函数便于单测。

### 2.11 封装格式与 API 优先级

- **API 优先级**（`BBDown/VideoInfo.cs`：`DetermineApiType`）：**TV > APP > INTL > WEB**；`--app-api --intl-api` 同给走 APP（`Parser.ExtractTracksAsync` 中 `req.AppApi && !req.IntlApi` 才走 APP 分支，委派 `AppTrackReader.FetchAsync`）。
- **DASH 封装**（`DashTrackReader.Collect`）：先按 `-q` 请求一次收集轨道，再额外以 `MaxQn(127)` 请求一次取原始画质视频轨，两次视频轨取并集；音轨取第二次结果（二次请求降级时回退首次结果的音轨，避免杜比/Hi-Res-only 片源被丢）。
- **FLV 封装**（`FlvTrackReader.Collect`）：强制 `qn = MaxQn(127)`，忽略 `-q`，只产出单一最高清流（仍可按 `-e` 选编码）。

### 2.12 归档记录

- **`--save-records`**（`BBDown/CommandLineInvoker.cs`：`SaveRecords` → `DownloadRequest.SaveArchivesToFile`）：下载成功后追加到 `BBDown.archives`，行格式为 Tab 分隔的 `<aid>\t<cid>\t<路径>`，键为 `(aid, cid)`（`BBDown/ArchiveLog.cs`）。下次运行对同 `(aid, cid)` 跳过下载。

### 2.13 测试与工程化

- **测试规模**：`BBDown.Core.Tests` 与 `BBDown.Tests` 合计 **870+ 单元测试**（按 `[Fact]`/`[Theory]` 展开后测试用例数），覆盖解析、混流、serve 鉴权与 SSRF、断点续传清单、文件名截断、cheese 过滤、WBI 签名、Opus 抓取与渲染、空间列表等。
- **AOT 与现代化**：
    - `BBDown/Directory.Build.props`：`<PublishAot>true</PublishAot>`，直接 `dotnet publish BBDown -r <RID> -c Release`（CI 命令见 `.github/workflows/ci.yml`，RID 矩阵 8 个）。
    - `Directory.Build.props`：`<TargetFramework>net9.0</TargetFramework>`、`<Nullable>enable</Nullable>`、`<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`、`<AnalysisLevel>latest-all</AnalysisLevel>`。
    - `System.Text.Json` 源生成器（如 `CredentialJsonContext`、`AppJsonSerializerContext`）替代反射，AOT 安全。
    - `[GeneratedRegex]` 集中声明正则（如 `OpusRegexes`、`InputResolver`）；`System.Threading.Lock` 替代 `object` 锁；god-class（如 `BBDownUtil`）按归属拆分。

### 2.14 直播录制

- **入口与分流**（`BBDown/Pipeline/LiveDownload.cs`、`BBDown/Program.cs`：`RunApp` 的 `LiveInputResolver.TryParse(...)` 分支）：直播间地址（`live:` / `live.bilibili.com/{数字}` / `m.live.bilibili.com`）在 `WorkSetup.Build` 之前分流到 `LiveDownload.RunAsync`，是一条不经 `WorkContext` / 混流主干的独立链路；房间短号自动换算为真实 ID，裸数字不进入直播链路（归属视频 `ep`）。
- **地址解析**（`BBDown.Core/Live/LiveInputResolver.cs`：`TryParse`）：仅接受 `live.bilibili.com/{数字}`、`m.live.bilibili.com`、`live:{数字}` 及其协议相对 / https 形态。
- **拉流与录制**（`BBDown.Core/Live/LiveFetcher.cs` / `BBDown/Live/LiveRecorder.cs`）：拉取 `http_stream` + `flv` 流地址，分段落盘 `<dest>.<NNN>.bbdown.part`；状态机处理断流退避重连、CDN failover、静默超时、磁盘满 / 连续失败保护，首段成功后锁定编码避免合并静默丢段。
- **产物命名**（`BBDown/Live/LiveFileNaming.cs`）：`<主播名>-<标题>-<yyyyMMdd_HHmmss>.mp4`（主播名 / 标题按 UTF-8 字节截断）。
- **合并**（`BBDown/Live/LiveMuxer.cs`）：`Ctrl+Break` 触发分段 FLV → 单个 mp4，按编码分派 bitstream filter（avc → `h264_mp4toannexb`、hevc → `hevc_mp4toannexb`，加 `+genpts`）；`Ctrl+C` 中断保留分段不合并。
- **清晰度**（`BBDown.Core/Live/LiveRoomInfo.cs`：`LiveQuality`）：`--live-quality` / `-lq` 取值，10000 原画（默认）/ 400 蓝光 / 250 超清 / 150 高清 / 80 流畅 / 15000 2K / 20000 4K / 30000 杜比；未登录通常只给到 250。

## 3. 关键改动核实点（源码位置）

- **子命令**：`BBDown/Program.cs`（`BuildLoginCommand` / `BuildServeCommand`；`GetOpusCommand` 由 `CommandLineInvoker` 提供）。
- **登录三态 + Cookie 续期**：`BBDown/Login.cs`（`Web` / `TV` / `App` / `TryRefreshWebCookieIfStaleAsync` / `RefreshWebCookieAsync` / `MakeCorrespondPath` / `RefreshRsaPublicKey`）。
- **凭据单文件 + 源生成器**：`BBDown/CredentialStore.cs`（`Credential` / `CredentialJsonContext` / `SaveWebCookie` 等）。
- **WBI 签名**：`BBDown.Core/Util/SignUtil.cs`（`WbiSign` / `WbiEncodeValue`）；应用点 `NormalInfoFetcher.cs`、`SubUtil.cs`、`SpaceListFetcher.cs`、`BiliApi.cs`（`PlayUrlWebPath` / `ViewWbi` / `PlayerWbiV2` / `SpaceArcSearch`），playurl 侧由 `PlayUrlClient` 调用。
- **serve 安全**：`BBDown/BBDownApiServer.cs`（`FinalizeAuth` / `IsLoopbackUrl` / `IsSafeWebHook` / `IsPrivateAddress` / `WebHookClient` / `SetUpServer` / `AddCors`）；`BBDown/ServeRequestOptions.cs`。
- **Opus 导出**：`BBDown.Core/Opus/`（`OpusFetcher` / `OpusInputResolver` / `OpusHtmlToMarkdown` / `OpusMarkdownRenderer` / `OpusImageUtil` / `OpusRegexes` / `OpusDocument`）与 `BBDown/OpusDownload.cs`。
- **空间列表**：`BBDown.Core/Fetcher/SpaceListFetcher.cs`、`BBDown/InputResolver.cs`、`BBDown.Core/Fetcher/FetcherRegistry.cs`。
- **充电试看**：`BBDown/ChargedPreviewException.cs`、`BBDown/PageDownload.cs`（`IsTruncatedPreview` / `ShouldRetry`）、`BBDown/Program.cs`（`IsChargedPreviewOnly` / `ApplyPreviewPrefix` 经 `SavePath.cs`）。
- **断点续传**：`BBDown/PartFile.cs`（`PartFile` / `PartManifest` / `Fingerprint`）。
- **文件名**：`BBDown.Core/Util/FileNameUtil.cs`（`MaxBytes = 200`）、`BBDown/SavePath.cs`（`Format` 的 `<publishDate:格式>` / `<videoDate:格式>`）。
- **cheese 增强**：`BBDown.Core/Fetcher/CheeseInfoFetcher.cs`（`BuildPages`）、`BBDown/Program.cs`（`NormalizeOptionsAfterFetch`）。
- **封装/优先级**：`BBDown.Core/PlayUrl/`（`DashTrackReader.Collect` / `FlvTrackReader.Collect` / `IntlTrackReader.Collect` / `AppTrackReader.FetchAsync`，请求由 `PlayUrlClient` 发出）；`Parser.ExtractTracksAsync` 负责编排；`DetermineApiType` 在 `BBDown/VideoInfo.cs`、`BBDown.Core/Config.cs`（`MaxQn`）。
- **归档**：`BBDown/ArchiveLog.cs`、`BBDown/CommandLineInvoker.cs`（`SaveRecords`）。
- **AOT/现代化**：`BBDown/Directory.Build.props`、`Directory.Build.props`。
- **直播录制**：`BBDown/Pipeline/LiveDownload.cs`、`BBDown/Live/`（LiveFileNaming / LiveMuxer / LiveProgress / LiveRecorder / LiveSegmentWriter / LiveSignal）、`BBDown.Core/Live/`（LiveInputResolver / LiveFetcher / LiveRoomInfo）。

## 4. 不兼容说明（升级注意）

- **凭据格式不兼容旧版**：不再识别旧的纯字符串 Cookie、`access_token=` 前缀纯文本、`BBDownTV.data` / `BBDownApp.data` / `BBDownRefresh.data` 分离文件，需重新 `login`。
- **归档格式不兼容旧版**：`--save-records` 现为 Tab 分隔的 `BBDown.archives`（`<aid>\t<cid>\t<路径>`），键为 `(aid, cid)`；旧版 `aid|...` 竖线格式不再识别。旧选项名 `--save-archives-to-file` 已更名为 `--save-records`。
- **`logintv` 子命令已合并**：原版 `logintv` 在本分支为 `login --tv`。
- **serve 接口方法变更**：`/add-task` 与 `/remove-finished*` 均为 **POST**；`/get-tasks/*` 为 GET。旧调用方需相应调整。
- **CORS 默认关闭**：旧版可能默认放开跨域；本分支默认不开放，须显式传 `--cors-origin` 才对该单一来源开放。
