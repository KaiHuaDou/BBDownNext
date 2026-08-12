# 与原版 BBDown 的差异对照

本仓库是 [nilaoda/BBDown](https://github.com/nilaoda/BBDown) 的一个增强分支（fork，远程 `KaiHuaDou/BBDownNext`）。
本文档逐项列出本分支相对原版的新增能力与行为改进，供选用 / 迁移时参考。

> 对照基准：原版 `nilaoda/BBDown` 上游主干（README 与源码）。
> 能力声明均已对照本仓库源码核实，各条目后附源码位置（文件：类型/方法）。

## 1. 能力对照总表

| 维度 | 原版 nilaoda/BBDown | 本分支（KaiHuaDou/BBDownNext） |
| --- | --- | --- |
| **顶层子命令** | 主命令 + `logintv` 等离散命令 | `login`（统一 `--tv` / `--app`）、`serve` 两个子命令，主命令解析视频/番剧/课程/收藏/空间/稍后再看，并自动识别专栏地址 |
| **WEB Cookie 续期** | 仅列于 TODO（「自动刷新 cookie」未实现） | 登录保存 `refresh_token`，下载前尝试用 **RSA-OAEP(SHA-256)** 加密请求主动续期 `cookie` |
| **扫码登录健壮性** | 单次轮询失败即中断 | 轮询接入全局取消（`Ctrl+C` 立即终止）与失败重试（单次失败自动重试至多 3 次）；WEB 登录成功后 best-effort 校验凭据并打印账号名 |
| **凭据存储** | 分离文件 `BBDownTV.data` / `BBDownApp.data`（APP 还需抓包后复制） | 单一 **`BBDown.data`**（同一 JSON 对象，源生成器序列化，AOT 安全）；WEB/TV/APP 分别落盘、互不覆盖 |
| **APP 端登录** | 无法自动获取，需抓包 `authorization: identify_v1` 并写入 `BBDownApp.data` | `login --app` **扫码登录** APP 账号，自动保存 |
| **TV 端登录** | 独立 `logintv` 子命令 | `login --tv`（与 `login` / `login --app` 统一为一个子命令的可选标志） |
| **AOT 原生发布** | 未提供；依赖运行时反射 | 代码已改造为 **AOT 安全**（`System.Text.Json` 源生成器替代反射）；AOT 在 `BBDown/Directory.Build.props` 默认开启，`dotnet publish BBDown -r <RID> -c Release` 即产出单文件原生二进制 |
| **WBI 签名降风控** | playurl / view 等接口明文或仅简单 sign | 对 playurl（wbi/playurl）、view（wbi/view）、字幕（player/wbi/v2）、空间列表（space/wbi/arc/search）均做标准 WBI 签名；未探测账号时退化为不签名 |
| **serve 鉴权** | 基础令牌 | 回环地址免令牌；非回环地址强制令牌（`X-BBDown-Token` 头或 `?token=` 查询），否则 401 |
| **serve 安全** | 请求体基本透传 | 请求契约收窄为受控子集 DTO；host 三兄弟与 work-dir 服务端固定；回调地址 **SSRF 防护**（拒绝内网/回环，连接前二次校验）；**CORS 默认关闭** |
| **专栏/图文导出** | 无 | 主命令自动识别专栏地址（`/opus/`、`/read/`、`opus{id}` / `cv{id}`），导出为 Markdown + 图片目录；纯图文动态（`item.type == 0`）按正文导出，顶部相册一并下载置于文档最前 |
| **稍后再看列表** | 无 | `watchlater` 系列地址解析为整个列表（按添加顺序），多 P 自动展开，支持 `-p` / `-iap`；接口私有，需登录 Cookie |
| **UP 主空间投稿列表** | 无 | 新增 `SpaceListFetcher` 与 space URL 解析，可下载某 UP 全部投稿 |
| **充电专属试看识别** | 无专门处理（按普通失败或下载残缺片段） | `IsTruncatedPreview` 双条件判定，命中抛 `ChargedPreviewException`，退出码 2 表示全部为试看（可 `--allow-preview` 放行） |
| **加密轨道后处理** | 无 | playurl 默认解析 `widevine_pssh` / `bilidrm_uri` 加密标记（`IsEncrypted`）；`--post-process <exe>` 指定外部进程处理加密轨，成功产物覆盖原轨参与混流，未配置 / 失败 / 超时静默保留原文件。主程序不内置解密能力，密钥与加密信息由外部进程自行获取管理 |
| **断点续传** | 基础续传 | 每条流维护 `<路径>.bbdown.part` 数据 + `<路径>.bbdown.json` **SHA256 指纹清单**，支持单流粒度与合集/多 P 粒度续传 |
| **文件名日期格式** | 固定 `yyyy-MM-dd_HH-mm-ss` | 支持自定义 `<publishDate:格式>` / `<videoDate:格式>`（任意 .NET `DateTime` 格式串） |
| **文件名长度** | 无特殊处理，超长路径易写入失败 | 按 **UTF-8 字节数截断，上限 200 字节**，并清理非法字符 / 保留设备名 / 处理首尾点 |
| **cheese 课程** | 仅 Web；存在冗余 `ss` 请求 | 消除冗余 `ss` 请求；`--api intl` 对其**自动回退 WEB**；**过滤锁定分集**（`BuildPages` status==2） |
| **解析模式选择** | 未明确文档化 | 单选 `--api web | tv | app | intl`（忽略大小写），取代多布尔开关的隐式优先级 |
| **FLV / DASH 封装** | 通用说明 | DASH 先按 `-q` 请求再额外以 `MaxQn(127)` 取原始画质轨（两次并集）；FLV 固定 `qn=127`、忽略 `-q` |
| **归档记录** | `--save-archives-to-file`（旧竖线格式） | `--save-records` 写 Tab 分隔 `BBDown.archives`（`<aid>\t<cid>\t<路径>`），键为 `(aid, cid)` |
| **测试覆盖** | 较少 | **950+ 单元测试**（Core + BBDown.Tests，含 gRPC 打包往返、cheese 过滤、serve 安全、断点续传清单、文件名截断、WBI 签名、加密标记、Opus 渲染等） |
| **代码结构** | 传统结构 | 深度重构：下载能力整体下沉 `BBDown.Core`，按职责拆分命名空间（`Pipeline` / `Media` / `Mux` / `Download` / `Live` / `Auth` / `Fetcher` / `PlayUrl` / `Opus` / `Comment` / `Entity` / `Util`，CLI 与 serve 留在 `BBDown`），依赖单向成树（`just check-deps` 守护）；god-class 拆分（如 `BBDownUtil` 按归属拆分）、现代化命名、`System.Threading.Lock`、`[GeneratedRegex]`、`Nullable enable` + `TreatWarningsAsErrors`、net9.0 |
| **直播录制** | 无 | 新增独立直播链路，直播间地址直录（`live:` / `live.bilibili.com`），`--live-quality` 选清晰度（默认原画 10000，可选 250 超清 / 400 蓝光 / 15000 2K / 20000 4K / 30000 杜比），分段 FLV 落盘后合并为 mp4（`Ctrl+Break` 停录合并 / `Ctrl+C` 中断保留分段）；录制状态机具备断流退避重连、CDN failover、编码锁定 |
| **图形界面** | 无 | 新增 BBDown.GUI（WPF，仅 Windows）：单窗口封装下载，直接引用 `BBDown.Core` 下载库（非子进程调用 BBDown.exe），任务队列与并发控制（1–8）、日志实时显示、选项随 exe 便携保存；独立 CI（`gui.yml`）发布单文件自包含产物 |

## 2. 分主题详述

### 2.1 子命令与入口

- **`login`**：统一入口，无标志登录 WEB，加 `--tv` 登录 TV，加 `--app` 登录 APP（`BBDown/Program.cs`：`loginCommand` 的 `SetAction` → `Login.Web/TV/App`）。原版 `logintv` 已合并进 `login --tv`。
- **专栏导出无子命令**：主命令在 `RunApp` 顶部用 `OpusInputResolver.TryParse` 识别专栏地址并分流（`BBDown/Program.cs`：`RunApp` 的 `OpusInputResolver.TryParse(...)` 分支），不注册子命令。
- **`serve`**：服务器模式，选项含 `--listen` / `--serve-token` / `--work-dir` / `--host` / `--ep-host` / `--tv-host` / `--cors-origin` / `--max-concurrent`（`BBDown/Program.cs`：`BuildServeCommand`）。
- 主命令解析范围：`av` / `BV` / `ep` / `ss` / `md`、合集（`MediaList`）/ 系列（`Series`）、收藏夹（`Fav`）、空间（`Space`）、稍后再看（`WatchLater`）、cheese（`CheeseEp` / `CheeseSeason`），统一解析为 `ResourceId` 判别联合后由 `FetcherRegistry` 按子类型分发（`BBDown.Core/Pipeline/InputResolver.cs`：`ResolveIdAsync`）。

### 2.2 登录与凭据管理

- **三种扫码登录**共用同一轮询编排（`BBDown.Core/Auth/Login.cs`：`RunQrLoginAsync` + `QrLoginPlan`，`Login` 为按职责拆分的 partial class：`Login.Web.cs` / `Login.App.cs` / `Login.Refresh.cs` / `Login.Sign.cs`），仅生成/轮询/解释/落盘环节不同：
    - 轮询循环接入**全局取消**（`Ctrl+C` 立即终止扫码等待）与**失败重试**（单次轮询失败自动重试至多 3 次自愈，网络抖动不直接中断登录）。
    - WEB：`Login.Web`，扫码后从 query / `Set-Cookie` / crossDomain CookieContainer 多源合并出 `DedeUserID` `DedeUserID__ckMd5` `SESSDATA` `bili_jct` 四件套（`BuildWebCookieResilient`）；登录成功后再 best-effort 校验凭据并打印账号名（失败仅告警，不阻断）。
    - TV / APP：`Login.TV` / `Login.App`，用各自 `appkey`/`secret`（TV: 云视听小电视；APP: 手机粉版），扫码拿 `access_token`（`LoginWithAppKey`）。
- **WEB Cookie 主动续期**：仅当本地持有 `refresh_token` 时尝试；先问 `/x/passport-login/web/cookie/info` 是否需要刷新，需要才走 RSA-OAEP(SHA-256) 签名 `refresh_{ts}` → 取 `refresh_csrf` → POST `/x/passport-login/web/cookie/refresh` → confirm 全流（`Login.Refresh.cs`：`TryRefreshWebCookieIfStaleAsync` / `RefreshWebCookieAsync` / `MakeCorrespondPath`，公钥 `RefreshRsaPublicKey`）。任一步失败回退原 cookie，不阻断下载。
- **单一凭据文件**：`BBDown.data`，同一 JSON 对象含 `cookie` / `refresh_token` / `ts` / `tv_access_token` / `tv_ts` / `app_access_token` / `app_ts`（未登录字段为 `null`）；磁盘键 snake_case、属性 PascalCase（`BBDown.Core/Auth/CredentialStore.cs`：`Credential` 记录 + `CredentialJsonContext` 源生成器）。每次保存只更新对应字段并合并保留其余字段（`SaveWebCookie` / `SaveTvToken` / `SaveAppToken` 用 `with` 表达式）。类 Unix 落盘权限收紧为 `600`。
- **旧格式不兼容**：旧的纯 cookie 串、`access_token=` 前缀纯文本、`BBDownTV.data` / `BBDownApp.data` / `BBDownRefresh.data` 分离文件均不被识别，反序列化为非法 JSON 时一律按空凭据处理（`CredentialStore.LoadCredential` 的 `catch`）。

### 2.3 WBI 签名与风控规避

- **签名算法**（`BBDown.Core/Util/SignUtil.cs`：`WbiSign`）：剔除已有 `w_rid`，对含 `wts` 的参数按 key 升序排序，值做 encodeURIComponent 风格编码（保留 `A-Za-z0-9-_.~`，过滤 `! ' ( ) *`，其余 UTF-8 字节大写十六进制转义），末尾拼 `mixinKey` 取 MD5 得 `w_rid`。算法对齐 `bilibili-API-collect/docs/misc/sign/wbi.md`。
- **应用范围**：
    - playurl：`/x/player/wbi/playurl`（`BiliApi.PlayUrlWebPath`，`PlayUrlClient` 非番剧分支经 `SignUtil.WbiSign` 签名）。
    - view：`/x/web-interface/wbi/view`（`NormalInfoFetcher.cs`：`ViewWbi`）。
    - 字幕：`/x/player/wbi/v2`（`SubUtil.cs`：`PlayerWbiV2`）。
    - 空间列表：`/x/space/wbi/arc/search`（`SpaceListFetcher.cs`：`SpaceArcSearch`）。
- **退化条件**：`cfg.Wbi` 为空（未探测账号）时 `WbiSign` 直接原样返回，不做签名（`SignUtil.WbiSign`：`if (cfg.Wbi.Length == 0) return api;`）。
- **番剧/课程 playurl 不签名**：`PlayUrlClient.FetchAsync` 对 `IsBangumi` 分支走 `req.Api == ApiType.Tv ? BuildTvQuery : BuildWebQuery`，不经 `WbiSign`。
- **试看时长依赖 WBI**：充电试看判定需走 WBI 签名的 playurl 才能拿到真实试看时长；非 WBI 的 `x/player/playurl` 返回的 `timelength` 是声称的完整时长，会误判（见 2.7）。

### 2.4 serve 模式

- **路由与方法**（`BBDown/Serve/BBDownApiServer.cs`，按职责拆为 `BBDownApiServer.Endpoints.cs` / `BBDownApiServer.Tasks.cs` 三个 partial 文件）：
    - `GET /get-tasks/`、`/get-tasks/running`、`/get-tasks/finished`、`/get-tasks/{id}`（任务状态查询）。
    - `POST /add-task`（新增下载任务）。
    - `POST /remove-finished/`、`/remove-finished/failed`、`/remove-finished/{id}`（清理已完成任务）。
    - `POST /stop-task/{id}`（取消单个运行中 / 排队中任务，不影响其他任务）。
- **鉴权**：`FinalizeAuth(url)` 判定监听地址——回环地址（`IsLoopbackUrl`）免令牌；非回环地址 `authRequired = true`，请求须带 `X-BBDown-Token` 头或 `?token=` 查询参数，缺失/错误返回 401（中间件 `context.Response.StatusCode = 401`）。
- **SSRF 防护**（`BBDown/Serve/SsrfGuard.cs`，自 `BBDownApiServer` 抽出的静态防护类）：
    - `IsSafeWebHook(uri)` 仅允许公网 `http/https`，拒绝 `localhost`、回环与私网地址。
    - 专用 `WebHookClient` 关闭自动重定向，并在 `ConnectCallback` 中于建立 TCP 连接前对最终端点 IP 做 `IsPrivateAddress` 二次校验（消除 DNS 重绑定 TOCTOU 窗口），拒绝私网/回环/链路本地/未指定地址（`::`）。
- **服务端固定字段**：`host` / `ep-host` / `tv-host` 三兄弟与 `work-dir` 不再出现在请求 DTO（`ServeRequestOptions`），改由 serve 启动参数固定（聚合为 `ServeConfig` record，取代散参），避免请求不带 cookie 时回落本机 `SESSDATA` 被导向外部服务器（`BBDown/Serve/ServeConfig.cs`）。
- **CORS 默认关闭**：不指定 `--cors-origin` 时完全不注册 CORS；指定时仅允许该单一来源（`AddCors` → `AllowSpecificOrigin`）。
- **并发度**：`--max-concurrent N>0` 用 `SemaphoreSlim` 限制同时下载任务数，多余任务排队（`DownloadTask.Status == Queued`）；单个任务内部的下载并行度由多线程下载器自行决定，不再压到 1；`0`（默认）表示不限制（历史行为）。任务模型为 `DownloadTask` / `DownloadStatus`。

### 2.5 专栏/图文导出（Opus）

- **入口与分流**：主命令在 `Program.RunApp` 顶部用 `OpusInputResolver.TryParse` 识别专栏地址，早于 `WorkSetup.Build` 分流到 `OpusDownload.RunAsync`，因此不构造 `WorkContext`、不探测 `ffmpeg`、不经过音视频保存路径（`BBDown/Program.cs`：`RunApp` 的 `OpusInputResolver.TryParse(...)` 分支）。
- **地址解析**（`BBDown.Core/Opus/OpusInputResolver.cs`：`TryParse`）：
    - URL：`/opus/<id>`、`/read/cv<id>`、`/read/mobile/<id>`（支持 `//` 协议相对、`m.`、带 query）。
    - 简写：`opus:<id>`、`opus<id>`、`cv<id>`。
    - 裸数字一律拒绝（归属视频链路，避免 `av` 号简写被误判为专栏）。
- **抓取与解析**（`BBDown.Core/Opus/`）：`OpusFetcher` 为按职责拆分的 partial class（`OpusFetcher.cs` 网络编排与判定 / `OpusFetcher.Parse.cs` 文档级解析 / `OpusFetcher.Paragraph.cs` 段落与节点解析）：
    - 拉取 opus/detail（依赖 cookie 中的 `buvid3`，旁路后自行 `Buvid.InitAsync`）；`TryGetCvId` 按 `fallback.type == 2` 或 `item.type == 1`（专栏动态）取 cv 号，否则判定为**纯图文动态**（`item.type == 0`）直接按 `MODULE_TYPE_CONTENT` 正文导出 Markdown（此前误判为专栏导致请求 404）。
    - **顶部相册**：opus/detail 的 `MODULE_TYPE_TOP` 模块（`module_top.display.album.pics`）图片随正文一并下载，并置于 Markdown 文档最前（`ParseTopAlbum` / `PrependTopAlbum`）。
    - `OpusHtmlToMarkdown`：将专栏正文 HTML 转为 Markdown（标签模式用 `[GeneratedRegex]` 集中在 `OpusRegexes`）；旧版专栏（`data.type == 0`）HTML 降级转换采用**白名单策略**——链接/加粗/斜体/代码/引用/标题/列表/分割线等可靠标签转 Markdown，其余标签（img、span 样式、figure、table 等）原样保留（CommonMark 支持内嵌 HTML），仅解码正文文本段；产物标记 `IsRawMarkdown`，渲染时跳过行内转义（`OpusFetcher.Parse.cs`）。
    - `OpusMarkdownRenderer`：渲染为带 YAML front matter（标题/作者/段落数等，可用 `-W M` 关闭）的 Markdown，图片按 `OpusImageUtil` 下载到 `<标题>/images/` 并以相对路径内联。
    - `OpusImageUtil`：`NormalizeProtocol` 统一协议补全（`//` 补 https、http 升 https），`OpusHtmlToMarkdown` 与 `OpusMarkdownRenderer` 复用；按 SHA256 前 8 位命名去重、失败时保留远程链接。
- **产物**：`<标题>.md` + `<标题>/images/` 目录；已存在非空 `.md` 时跳过（与 `MuxFinish.TrySkipExisting` 同语义）。内容集沿用默认 `avmsCiM`（专栏下仅 `i` / `M` 生效）：`-W i` 保留远程图片链接，`-W M` 不加 front matter。

### 2.6 UP 主空间投稿列表

- **解析入口**（`BBDown.Core/Pipeline/InputResolver.cs`）：`space.bilibili.com/{mid}` 及其子路径（`/upload/video`、`/video?tid=0`、合集 `/lists/`、系列 /系列 `?type=series`）统一解析为 `ResourceId.Space(mid)`；另支持 `space{mid}` 简写。根命令裸数字解析为 `Ep(ep_id)`（av 号须带 `av` 前缀，无回归）。
- **抓取**（`BBDown.Core/Fetcher/SpaceListFetcher.cs`：`FetchAsync`）：
    - 分页调用 `x/space/wbi/arc/search`（带 WBI 签名，`ps=30`），内置风控守卫（`is_risk` / `gaia_res_type` 抛「被风控拦截」）、越界页兜底、`MaxPages=1000` 防死循环。
    - 接口只返回 `aid`，对每条稿件并发回填（并发度 8，`SemaphoreSlim`）一次 `wbi/view` 取 `cid` 并展开多 P，摊平为 `VInfo.PagesInfo`；用 `HashSet<Page>` 去重。
    - 过滤：课堂视频（`is_lesson_video`，wbi/view 必失败）、已转为番剧的稿件（提示用 ep 链接）、回填失败的稿件——统一降级为「跳过并提示」，不中断整体。
    - 候选链上移到 `FetcherRegistry`（`ListBizId` 解析失败时回退按系列重试），各 Fetcher 保持单向无互相调用。

### 2.7 充电专属试看识别

- **判定**（`BBDown.Core/Media/PageDownload.cs`：`IsTruncatedPreview`）：双条件——稿件 `is_upower_exclusive == true`（稿件属性，与账号无关）**且** playurl 下发时长 `actual < full * 0.9` 且 `full - actual >= 30`（秒）。30 秒下限用于避开 `timelength`(ms) 与 `duration`(整秒) 的固有封装误差。
- **异常与退出码**（`BBDown.Core/Download/ChargedPreviewException.cs`、`BBDown/Program.cs`：`RunApp` / `IsChargedPreviewOnly`）：
    - 命中且非 `--allow-preview`、非 `--info-only` 且内容集不含 `a` / `v`（仅信息 / 封面 / 弹幕等）时 → 抛 `ChargedPreviewException`，该分 P 跳过。
    - 全部所选分 P 均为试看 → 退出码 **2**；混合（部分试看 + 部分真实失败）→ 退出码 **1**（使 2 成为强断言）；`Ctrl+C` → 退出码 **130**。
    - `--allow-preview`：输出文件名末段加 `[试看]` 前缀（`SavePath.ApplyPreviewPrefix`），退出码 0。
    - 信息/封面/弹幕模式仅提示，不中止。
- **重试排除**：`ShouldRetry` 明确将 `ChargedPreviewException` 排除在可重试之外（充电权限不会因重试改变）。

### 2.8 断点续传

- **机制**（`BBDown.Core/Download/PartFile.cs`：`PartFile` / `PartManifest` / `Fingerprint`）：每条流维护 `<路径>.bbdown.part` 数据文件与 `<路径>.bbdown.json` **SHA256 指纹清单**（含文件大小、分片范围、校验和），下载前比对指纹决定是否续传。
- **粒度**：支持单流粒度续传，以及合集/多 P 粒度（每个分 P 独立清单）；中断后重跑命令可从上次进度继续。

### 2.9 文件名与模板

- **清洗与截断**（`BBDown.Core/Util/FileNameUtil.cs`：`GetValidFileName`）：
    - 非法字符（`" < > | : * ? \ /` 及 0–31 控制字符）替换为 `_`。
    - Windows 保留设备名（`CON`/`PRN`/`AUX`/`NUL`/`COM1-9`/`LPT1-9`，含任意扩展名）前加 `_`。
    - 首尾空格/点清理；开头点转为隐藏文件时加 `_` 前缀。
    - 按 **UTF-8 字节数截断，上限 `MaxBytes = 200`**，代理对整体保留避免切出无效字符。
- **日期模板**（`BBDown.Core/Download/SavePath.cs`：`Format`）：`<publishDate:格式>` / `<videoDate:格式>` 支持任意 .NET `DateTime` 格式串；未指定时默认 `yyyy-MM-dd_HH-mm-ss`。其余占位符：`videoTitle` `pageNumber` `pageNumberWithZero` `pageTitle` `bvid` `aid` `cid` `ownerName` `ownerMid` `dfn` `res` `fps` `videoCodecs` `videoBandwidth` `audioCodecs` `audioBandwidth` `apiType`。

### 2.10 课程（cheese）解析

- **消除冗余 `ss` 请求**：cheese 输入 `cheese/ss` 直接取 `season_id` 拉整季，不再「先请求取首集 ep_id、再拉整季」（`InputResolver.ResolveCheeseAsync`；`CheeseInfoFetcher.FetchAsync`）。
- **intl 自动回退 WEB**：`NormalizeOptionsAfterFetch` 在 cheese 场景下将 `--api intl` 回退为 WEB（`BBDown.Core/Pipeline/VideoInfo.cs`）。
- **过滤锁定分集**：`CheeseInfoFetcher.BuildPages` 跳过 `status == 2`（未购买/锁定）的分集；抽为纯函数便于单测。

### 2.11 封装格式与 API 选择

- **API 通道**（`BBDown.Core/ApiType.cs`：`ApiType` 枚举 + `ApiTypeUtil.TryParse`）：单选 `--api web|tv|app|intl`（忽略大小写），取代原多布尔开关；APP 分支由 `Parser.ExtractTracksAsync` 中 `req.Api == ApiType.App` 委派 `AppTrackReader.FetchAsync`。
- **DASH 封装**（`DashTrackReader.Collect`）：先按 `-q` 请求一次收集轨道，再额外以 `MaxQn(127)` 请求一次取原始画质视频轨，两次视频轨取并集；音轨取第二次结果（二次请求降级时回退首次结果的音轨，避免杜比/Hi-Res-only 片源被丢）。
- **FLV 封装**（`FlvTrackReader.Collect`）：强制 `qn = MaxQn(127)`，忽略 `-q`，只产出单一最高清流（仍可按 `-e` 选编码）。

### 2.12 归档记录

- **`--save-records`**（`BBDown/Cli/CommandLineInvoker.cs`：`SaveRecords` → `DownloadRequest.SaveArchivesToFile`）：下载成功后追加到 `BBDown.archives`，行格式为 Tab 分隔的 `<aid>\t<cid>\t<路径>`，键为 `(aid, cid)`（`BBDown.Core/Util/ArchiveLog.cs`）。下次运行对同 `(aid, cid)` 跳过下载。

### 2.13 测试与工程化

- **测试规模**：`BBDown.Core.Tests` 与 `BBDown.Tests` 合计 **950+ 单元测试**（按 `[Fact]`/`[Theory]` 展开后测试用例数），覆盖解析、混流、serve 鉴权与 SSRF、断点续传清单、文件名截断、cheese 过滤、WBI 签名、Opus 抓取与渲染、加密标记、空间列表与稍后再看等。
- **AOT 与现代化**：
    - `BBDown/Directory.Build.props`：`<PublishAot>true</PublishAot>`，直接 `dotnet publish BBDown -r <RID> -c Release`（CI 命令见 `.github/workflows/ci.yml`，RID 矩阵 8 个）。
    - `Directory.Build.props`：`<TargetFramework>net9.0</TargetFramework>`、`<Nullable>enable</Nullable>`、`<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`、`<AnalysisLevel>latest-all</AnalysisLevel>`。
    - `System.Text.Json` 源生成器（如 `CredentialJsonContext`、`AppJsonSerializerContext`、`PostProcessJsonContext`）替代反射，AOT 安全。
    - `[GeneratedRegex]` 集中声明正则（如 `OpusRegexes`、`InputResolver`）；`System.Threading.Lock` 替代 `object` 锁；god-class（如 `BBDownUtil`）按归属拆分。

### 2.14 直播录制

- **入口与分流**（`BBDown.Core/Pipeline/LiveDownload.cs`、`BBDown/Program.cs`：`RunApp` 的 `LiveInputResolver.TryParse(...)` 分支）：直播间地址（`live:` / `live.bilibili.com/{数字}` / `m.live.bilibili.com`）在 `WorkSetup.Build` 之前分流到 `LiveDownload.RunAsync`，是一条不经 `WorkContext` / 混流主干的独立链路；房间短号自动换算为真实 ID，裸数字不进入直播链路（归属视频 `ep`）。
- **地址解析**（`BBDown.Core/Live/LiveInputResolver.cs`：`TryParse`）：仅接受 `live.bilibili.com/{数字}`、`m.live.bilibili.com`、`live:{数字}` 及其协议相对 / https 形态。
- **拉流与录制**（`BBDown.Core/Live/LiveFetcher.cs` / `BBDown.Core/Live/LiveRecorder.cs`）：拉取 `http_stream` + `flv` 流地址，分段落盘 `<dest>.<NNN>.bbdown.part`；状态机处理断流退避重连、CDN failover、静默超时、磁盘满 / 连续失败保护，首段成功后锁定编码避免合并静默丢段。
- **产物命名**（`BBDown.Core/Live/LiveFileNaming.cs`）：`<主播名>-<标题>-<yyyyMMdd_HHmmss>.mp4`（主播名 / 标题按 UTF-8 字节截断）。
- **合并**（`BBDown.Core/Live/LiveMuxer.cs`）：`Ctrl+Break` 触发分段 FLV → 单个 mp4，按编码分派 bitstream filter（avc → `h264_mp4toannexb`、hevc → `hevc_mp4toannexb`，加 `+genpts`）；`Ctrl+C` 中断保留分段不合并。
- **清晰度**（`BBDown.Core/Live/LiveRoomInfo.cs`：`LiveQuality`）：`--live-quality` / `-lq` 取值，10000 原画（默认）/ 400 蓝光 / 250 超清 / 150 高清 / 80 流畅 / 15000 2K / 20000 4K / 30000 杜比；未登录通常只给到 250。

### 2.15 加密轨道与外部后处理

- **识别**（`BBDown.Core/PlayUrl/TrackFactory.cs`：`ReadEncrypted`）：playurl 逐流下发 `widevine_pssh` / `bilidrm_uri`，任一存在即视为受保护（协议字段）。加密标记挂到 `Video` / `Audio` 轨道（`BBDown.Core/Entity/Entity.cs` 的 `IsEncrypted`），不参与轨道相等比较。
- **主程序不内置解密**：密钥、通道与加密信息均由外部进程自行获取管理，主程序不感知其语义。
- **调起外部进程**（`BBDown.Core/Download/PostProcessClient.cs`：`Configure` / `TryProcessAsync`）：`--post-process <exe>` 由 `CommandLineInvoker` 注册；对带加密标记的轨写请求 JSON（`PostProcessRequest`：`Aid` / `Cid` / `Kind` / `TrackPath` / `DestPath` / `Ffmpeg`），以请求文件路径为唯一参数调起进程，20 秒超时。请求只携带轨道定位与本地路径，**不携带任何加密特征与凭据**。
- **接入点与降级**（`BBDown.Core/Media/DashDownload.cs`：`TryPostProcessAsync`）：DASH 轨下载完成后统一处理视频轨 / 音频轨 / 背景音 / 配音轨——进程退出码为 0 且产物非空时产物覆盖原轨参与混流；未配置 / 进程不可用 / 超时 / 失败一律静默保留原文件，加密流照常参与混流。FLV 分支与直播录制不经此路径（直播对带加密标记的流直接跳过）。

### 2.16 稍后再看列表

- **解析入口**（`BBDown.Core/Pipeline/InputResolver.cs`）：`https://www.bilibili.com/watchlater/`、`/watchlater/#/list`、`/list/watchlater` 等形态统一解析为 `ResourceId.WatchLater`；分享链接带 `bvid` / `oid` 参数时只下载该单个视频（`bvid` 优先，本地解码）。
- **抓取**（`BBDown.Core/Fetcher/WatchLaterFetcher.cs`）：走私有接口 `toview`，未登录（`-101`）时给出「通过 --cookie 或配置文件提供 SESSDATA」的可操作提示；多 P 视频并发（限流 8）回填 `wbi/view` 展开分 P，按添加顺序摊平为 `VInfo.PagesInfo`（`HashSet<Page>` 去重），支持 `-p` / `-iap`。列表为空时明确报错。

### 2.17 图形界面 BBDown.GUI

- **形态**（`BBDown.GUI/`，WPF，仅 Windows，`net9.0-windows`）：单窗口封装下载——直接引用 `BBDown.Core` 下载库，以库调用方式执行任务（非子进程调用 BBDown.exe），下载内容按 CLI 字符集（a / v / c / C / d / i / m / M / o / O / S / s）全量 CheckBox 配置，其余选项与 CLI 参数一一对应。
- **任务队列**（`QueueRunner` / `TaskParams`）：多任务排队与并发控制（1–8，运行中可调），「执行」与队列任务共享并发池；任务日志经 Core `Logger.Output` 回调进入窗口日志区，仍按级别着色。
- **便携配置**（`ConfigStore`）：面板选项随 exe 保存到 `BBDown.GUI.config.json`（不保存 url 与队列）。
- **发布**：独立 CI（`.github/workflows/gui.yml`）在 `win-x64` / `win-arm64` 上产出 framework-dependent 与自包含单文件（`PublishSingleFile` + `PublishReadyToRun`，非 AOT）并上传产物，可手动触发追加到最新 Release；主 CI（`ci.yml`）不再构建图形界面。

## 3. 关键改动核实点（源码位置）

- **子命令**：`BBDown/Program.cs`（`BuildLoginCommand` / `BuildServeCommand`；专栏导出无子命令，由 `RunApp` 内 `OpusInputResolver.TryParse` 分流）。
- **登录三态 + Cookie 续期**：`BBDown.Core/Auth/`（`Login.cs` 轮询编排 `QrLoginPlan` / `RunQrLoginAsync`；`Login.Web.cs` / `Login.App.cs` / `Login.Refresh.cs` / `Login.Sign.cs`，含 `TryRefreshWebCookieIfStaleAsync` / `RefreshWebCookieAsync` / `MakeCorrespondPath` / `RefreshRsaPublicKey`）。
- **凭据单文件 + 源生成器**：`BBDown.Core/Auth/CredentialStore.cs`（`Credential` / `CredentialJsonContext` / `SaveWebCookie` 等）。
- **WBI 签名**：`BBDown.Core/Util/SignUtil.cs`（`WbiSign` / `WbiEncodeValue`）；应用点 `NormalInfoFetcher.cs`、`SubUtil.cs`、`SpaceListFetcher.cs`、`BiliApi.cs`（`PlayUrlWebPath` / `ViewWbi` / `PlayerWbiV2` / `SpaceArcSearch`），playurl 侧由 `PlayUrlClient` 调用。
- **serve 安全**：`BBDown/Serve/`（`BBDownApiServer.cs` 与 `BBDownApiServer.Endpoints.cs` / `BBDownApiServer.Tasks.cs` 分部类；`SsrfGuard.cs`：`IsSafeWebHook` / `IsPrivateAddress` / `IsLoopbackUrl` / `WebHookClient`；`ServeConfig.cs` / `ServeRequestOptions.cs` / `ServeBindingResult.cs` / `ApiTypeJsonConverter.cs`）。
- **Opus 导出**：`BBDown.Core/Opus/`（`OpusFetcher` partial：`OpusFetcher.cs` / `OpusFetcher.Parse.cs` / `OpusFetcher.Paragraph.cs`；`OpusInputResolver` / `OpusHtmlToMarkdown` / `OpusMarkdownRenderer` / `OpusImageUtil` / `OpusRegexes` / `OpusDocument`）与 `BBDown.Core/Pipeline/OpusDownload.cs`。
- **空间列表**：`BBDown.Core/Fetcher/SpaceListFetcher.cs`、`BBDown.Core/Pipeline/InputResolver.cs`、`BBDown.Core/Fetcher/FetcherRegistry.cs`。
- **稍后再看**：`BBDown.Core/Fetcher/WatchLaterFetcher.cs`、`BBDown.Core/Pipeline/InputResolver.cs`、`BBDown.Core/IdPrefix.cs`（`WatchLater`）。
- **加密轨道后处理**：`BBDown.Core/PlayUrl/TrackFactory.cs`（`ReadEncrypted`）、`BBDown.Core/Entity/Entity.cs`（`IsEncrypted`）、`BBDown.Core/Download/PostProcessClient.cs`（`Configure` / `TryProcessAsync` / `PostProcessRequest`）、`BBDown.Core/Media/DashDownload.cs`（`TryPostProcessAsync`）。
- **图形界面**：`BBDown.GUI/`（`MainWindow` / `QueueRunner` / `TaskParams` / `ConfigStore` / `UrlDetector`）、`.github/workflows/gui.yml`。
- **充电试看**：`BBDown.Core/Download/ChargedPreviewException.cs`、`BBDown.Core/Media/PageDownload.cs`（`IsTruncatedPreview` / `ShouldRetry`）、`BBDown/Program.cs`（`IsChargedPreviewOnly` / `ApplyPreviewPrefix` 经 `SavePath.cs`）。
- **断点续传**：`BBDown.Core/Download/PartFile.cs`（`PartFile` / `PartManifest` / `Fingerprint`）。
- **文件名**：`BBDown.Core/Util/FileNameUtil.cs`（`MaxBytes = 200`）、`BBDown.Core/Download/SavePath.cs`（`Format` 的 `<publishDate:格式>` / `<videoDate:格式>`）。
- **cheese 增强**：`BBDown.Core/Fetcher/CheeseInfoFetcher.cs`（`BuildPages`）、`BBDown.Core/Pipeline/VideoInfo.cs`（`NormalizeOptionsAfterFetch`）。
- **封装/通道**：`BBDown.Core/PlayUrl/`（`DashTrackReader.Collect` / `FlvTrackReader.Collect` / `IntlTrackReader.Collect` / `AppTrackReader.FetchAsync`，请求由 `PlayUrlClient` 发出）；`Parser.ExtractTracksAsync` 负责编排；`ApiType` 枚举与解析在 `BBDown.Core/ApiType.cs`，`BBDown.Core/Config.cs`（`MaxQn`）。
- **归档**：`BBDown.Core/Util/ArchiveLog.cs`、`BBDown/Cli/CommandLineInvoker.cs`（`SaveRecords`）。
- **AOT/现代化**：`BBDown/Directory.Build.props`、`Directory.Build.props`（GUI 为独立 WPF 项目，单文件 + ReadyToRun，不参与 AOT）。
- **直播录制**：`BBDown.Core/Pipeline/LiveDownload.cs`、`BBDown.Core/Live/`（LiveInputResolver / LiveFetcher / LiveRoomInfo / LiveRecorder / LiveSegmentWriter / LiveProgress / LiveFileNaming / LiveMuxer / LiveSignal）。

## 4. 不兼容说明（升级注意）

- **凭据格式不兼容旧版**：不再识别旧的纯字符串 Cookie、`access_token=` 前缀纯文本、`BBDownTV.data` / `BBDownApp.data` / `BBDownRefresh.data` 分离文件，需重新 `login`。
- **归档格式不兼容旧版**：`--save-records` 现为 Tab 分隔的 `BBDown.archives`（`<aid>\t<cid>\t<路径>`），键为 `(aid, cid)`；旧版 `aid|...` 竖线格式不再识别。旧选项名 `--save-archives-to-file` 已更名为 `--save-records`。
- **`logintv` 子命令已合并**：原版 `logintv` 在本分支为 `login --tv`。
- **交互式选项更名**：`--interactive` / `-ia` → `--interactive-quality` / `-iaq`；`--select-page` / `-p` → `--pages` / `-p`；serve 请求契约字段同步由 `selectPage` 改为 `pages`。
- **`opus` 子命令已移除**：专栏 / 图文导出统一走根命令自动识别（`BBDown <专栏地址>`、`BBDown opus{id}`、`BBDown cv{id}`）；裸数字不再触发专栏，保留给视频 av 号简写。
- **serve 接口方法变更**：`/add-task` 与 `/remove-finished*` 均为 **POST**；`/get-tasks/*` 为 GET。旧调用方需相应调整。
- **CORS 默认关闭**：旧版可能默认放开跨域；本分支默认不开放，须显式传 `--cors-origin` 才对该单一来源开放。
- **解密能力外移**：旧版内置解密选项（`--drm-key`）已移除，改为 `--post-process <exe>` 外部后处理（见 2.15）；图形界面同样不再提供密钥输入项。
