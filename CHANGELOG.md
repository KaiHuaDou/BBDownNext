# 更新日志

本项目的所有显著变更都将记录在此文件中。

本项目遵循 [语义化版本](https://semver.org/lang/zh-CN/) 约定，文件格式基于 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)。

本文件的内容基于对代码实际差异的比对（而非提交信息），以准确反映用户可见的行为变化。

## [v2.0.0-beta.2]

### 新增

- 多 P（总集数大于 1）且开启元数据混流（`-m`）时写入分 P 序号与总集数：FFmpeg 容器写 `track` / `track_total` 元数据，MP4Box 容器写 `tracknum` 元数据（`x/N`）。
- 稍后再看列表新增纯 `watchlater` 字符串入口：直接输入 `watchlater`（忽略大小写）即按整个列表下载（此前仅支持 `/watchlater/` 等 URL 形态）。
- 图形界面窗口尺寸与位置记忆：关闭时保存（最小化时按还原边界），启动时经虚拟屏幕范围校验后恢复，显示器变更后窗口不再落在屏幕外。
- 图形界面支持拖放输入：拖拽文本（如视频链接）到窗口即填入下载目标。
- 图形界面任务列表按状态着色（运行蓝 / 成功绿 / 失败红 / 取消灰），运行中任务可内联单独取消；队列状态栏实时显示等待 / 运行 / 完成计数，「移除」按钮仅在选中等待中任务时可用。
- 图形界面新增「打开输出目录」按钮：工作目录无效时回退打开 exe 所在目录。

### 修复

- serve 模式解析与播放信息获取接入取消令牌：`InputResolver.ResolveIdAsync` / `ChapterMeta.FetchPlayerV2Async` 补 `CancellationToken` 并沿调用链贯通，服务器关停（Ctrl+C）可中断排队中的任务解析与播放信息请求，不再悬挂。
- SSRF 防护补漏：回调地址校验对 IPv4-mapped IPv6（如 `::ffff:169.254.169.254`）先归一化为 IPv4 再判定私网，云元数据 / 内网地址不再绕过过滤。

### 变更

- 图形界面单文件发布启用压缩与原生库自解压，发布工作流补充 checkout 步骤。
- FFmpeg 混流时视频简介元数据键由 `description` 改为 `synopsis`（MP4 容器映射为 `ldes`）。
- 混流选项改为 `--mux` / `-m` 单值枚举：`none`（不混流）/ `mpeg4`（FFmpeg 混流为 MP4，默认）/ `mp4box`（MP4Box 混流）/ `mkv`（FFmpeg 混流为 Matroska，视频 `.mkv` / 纯音频 `.mka`），取代 `--skip-mux` 与 `--mp4box` 两个布尔开关；图形界面同步改为「混流方式」下拉框。
- serve 任务标识改为 `ResourceId` 规范字符串：`DownloadTask` 的 `Aid`（字符串）字段更名为 `Id`，JSON 序列化为规范编码（如 `season2539`、`av170001`、`fav100_200`）；`/get-tasks/{id}`、`/remove-finished/{id}`、`/stop-task/{id}` 路径参数使用同一编码。旧版「裸 AID 数字」路径与 `Aid` 字段废弃。
- 下载内容字符 `C` 由「封面混流」改称「封面嵌入」，CLI 帮助文本与图形界面同步更新。
- 图形界面日志区由 TextBox 改为 RichTextBox：逐行追加、错误行红色高亮、按段落数控制上限；勾选「调试」自动展开日志区。
- 图形界面文件 / 目录选择与错误提示统一改用 `ookii-dialogs-wpf`（`VistaFolderBrowserDialog` / `VistaOpenFileDialog` / `TaskDialog`），替代 Win32 旧对话框与 MessageBox。
- 图形界面样式抽取至 `Theme.xaml` 集中定义（字段标签 / 输入框 / 复选框行 / 操作按钮四类），窗口改为固定初始尺寸 1120×820（保留最小尺寸）。
- 图形界面分 P 间隔与评论条数失焦校验非负整数（无效回退上次有效值）；勾选「仅解析」时禁用下载内容 / 弹幕格式 / 评论格式选项，避免无效配置误导；常用输入框补 ToolTip 说明。
- 内部架构重构：下载处理层（下载管线、媒体下载、混流、DRM、直播、登录、工具）从 BBDown（CLI）下沉至 BBDown.Core，CLI 项目仅保留命令行解析、serve 模式与交互渲染；下载域模型统一归入 `BBDown.Core.Download` 命名空间，命名空间依赖调整为单向无环；对应测试迁移至 BBDown.Core.Tests。用户可见行为不变。

## [v2.0.0-beta.1]

### 新增

- DRM 解密支持：默认解析 playurl 中的 DRM 信息（`is_drm` / `drm_type` / `bilidrm_uri`），新增 `--drm-key` 直接传入解密密钥（`kid:key` 或纯 `key`，hex / base64 均可，可多次）。bili_drm 通道提供匹配 key 时自动解密后混流；未提供 key、Widevine 通道或解密失败时明确提示原因并**保留加密文件**（原始 `.m4s` 不删除，路径打印在日志中）。不内置 SPC/CKC 协议逆向，key 仅由用户提供。
- 图形界面 BBDown.GUI（WPF，仅 Windows）：单窗口封装命令行下载——子进程调用 BBDown.exe（stdout / stderr 重定向到日志区，按系统代码页解码，行带 `[任务N]` 前缀）；下载内容按 CLI 字符集（a / v / c / C / d / i / m / M / o / O / S / s）全量 CheckBox 配置，其余选项（布尔、输入、高级）与 CLI 参数一一对应；任务队列支持多任务排队与并发控制（1–8，运行中可调），「执行」与队列任务共享并发池；BBDown.exe 启动时自动检测（exe 同目录 → PATH，也可手动选择或自动检测）；面板选项随 exe 便携保存（`BBDown.GUI.config.json`，不保存 url 与队列）。
- HDR Vivid 画质档位：登记 qn=129（APP 端档位），APP 通道解析出的 HDR Vivid 轨道不再被当未知清晰度丢到排序末尾；APP playurl 请求能力位加 16384（HDR Vivid）。
- 播放信息失败可读报错：playurl 返回非 0 code（-400 / -404 / -352 等）时直接提示具体原因，风控（-352）额外建议稍后重试或补充已登录 Cookie，不再静默解析出空轨道。
- 收藏夹 / 稍后再看列表容错：单个视频被删除或触发风控时跳过该视频继续下载其余分 P（`Ctrl+C` 仍立即终止），列表分页整页失效时不再死循环。
- 交互选清晰度跨重试保留：DASH 手动选择的视频 / 音频轨序号与 FLV 选择的清晰度在下载失败重试时沿用，不再静默回落默认第 0 条轨道。
- 仅音频内容集（内容不含 v）时，评论区与字幕文件随产物改落 `.m4a` 基底，与混流产物扩展名一致。
- CLI 新增短选项：`-va` / `-aa`（视频 / 音频升序）、`-P`（允许试看）、`-L`（混流音频语言）、`-C`（cookie）、`-cwd`（工作目录）、`-c`（配置文件）。
- 未提供 Cookie 下载时自动补充 `gaia_source=pre-load` 参数（videostream_url.md 要求，缺失可能触发 v_voucher 风控）。

### 修复

- 修复图形界面任务执行崩溃：调度在后台线程读取界面控件（BBDown.exe 路径）触发跨线程异常，现经 UI 线程读取。
- 修复图形界面子进程日志乱码：BBDown.exe 按系统控制台代码页输出（中文系统为 GBK），读取端不再强制 UTF-8。
- 图形界面无法识别的下载目标不再进入队列；空队列点击「开始队列」仅提示队列为空，不再显示「调度已启动」。
- 修复弹幕颜色值超出 int 范围（8 位 ARGB）时弹幕解析中断，现跳过无效值并记日志。
- 修复 WBI 签名对已编码值二次编码：值含 `%XX` 时不再重复编码（否则 `%` 变 `%25` 与线上签名不符），未编码值仍按官方算法编码。
- 分 P 间隔（`--delay-per-page`）调整：首个分 P 与评论下载不参与等待，间隔仅在分 P 之间生效。
- 图形界面 UI 线程未处理异常不再直接崩溃退出：记录错误日志、提示后正常关闭。
- 图形界面全部取消勾选下载内容时显式传 `--get ""`（此前回落默认内容集导致全量下载）。

### 变更

- 图形界面：任务列表条目超长自动换行；选项布局调整（下载内容三列、常用输入整行化、任务列表宽度与日志高度可用分隔条调节、日志可用折叠完全隐藏、Aero 主题）；新增独立 CI（`gui.yml`：Windows 单文件自包含发布并上传产物，可手动触发追加到最新 Release），主 CI 不再构建图形界面。
- 图形界面 CI 构建矩阵扩展：`gui.yml` 增加 `win-arm64`，每个架构分别发布 framework-dependent（不含运行时）与自包含（sc，含运行时）双产物并上传。

## [v2.0.0-alpha.3]

### 新增

- 稍后再看列表下载：输入 `https://www.bilibili.com/watchlater/`、`https://www.bilibili.com/watchlater/#/list`、`https://www.bilibili.com/list/watchlater` 等地址时，把整个稍后再看列表按添加顺序当作一个大列表下载，多 P 视频自动展开分 P，支持 `-p` / `-iap`；接口私有，需要登录 Cookie（未登录时提示通过 `--cookie` 或配置文件提供 SESSDATA）。分享链接携带 `bvid` / `oid` 参数时只下载该单个视频（`bvid` 优先，本地解码）。
- 交互式逐集选择分 P：新增 `--interactive-pages` / `-iap`，下载前列出全部分 P 逐个确认是否下载（`[y]` 要，`[n]` 不要，`[a]` 剩余全部要，`[q]` 剩余全部不要，直接回车表示不要）；与 `--pages` 同时给出时以交互选择为准。
- Web 登录成功后自动校验凭据并打印账号名（best-effort，校验失败仅告警，不阻断登录）。
- Windows 7 兼容：`win-x64` 产物接入 YY-Thunks 与 VC-LTL（构建时 `-p:WindowsWin7Compat=true`），可在 Windows 7 上直接运行，无需安装 .NET 运行时；Windows 7 用户需先安装 [KB3140245](https://support.microsoft.com/help/3140245)（TLS 1.1 / 1.2 支持）。
- 纯图文动态导出：`opus` 子命令现支持非专栏的图文动态（`item.type == 0`），按其 `MODULE_TYPE_CONTENT` 正文导出 Markdown（此前会误判为专栏导致下载失败）。
- 顶部相册下载：专栏 / 图文动态的顶部相册（opus/detail 的 `MODULE_TYPE_TOP` 模块，前端样式 `.opus-module-top__album`）图片随正文一并下载，并置于 Markdown 文档最前。

### 变更

- 交互式选择清晰度更名：`--interactive` / `-ia` → `--interactive-quality` / `-iaq`。
- 手动分 P 选择更名：`--select-page` / `-p` → `--pages` / `-p`；serve 请求契约字段同步由 `selectPage` 改为 `pages`。
- 命令行选项按 README「参数说明」分组重排（解析模式 → 清晰度与编码 → 下载内容 → 直播录制 → 下载方式与性能 → 账号与凭据 → 文件、路径与调试），`--help` 显示顺序与文档一致。
- 扫码登录（Web / TV / APP）轮询接入全局取消与失败重试：`Ctrl+C` 可立即终止扫码等待，单次轮询失败自动重试至多 3 次，网络抖动不再直接中断登录。
- 旧版专栏（data.type == 0）HTML 降级转换策略调整：白名单标签（链接 / 加粗 / 斜体 / 代码 / 引用 / 标题 / 列表 / 分割线 / 段落换行）可靠转换为 Markdown，其余标签（img、span 样式、figure、table 等）原样保留——CommonMark 支持内嵌 HTML，保真优于剥壳；仅解码正文文本段，标签属性内的 HTML 实体（如 &quot;）保留原样；旧版转换产物标记 IsRawMarkdown，渲染时跳过行内转义。
- OpusImageUtil 抽出 NormalizeProtocol 统一协议补全（// 补 https、http 升 https），OpusHtmlToMarkdown 与 OpusMarkdownRenderer 复用，删除两处重复的 NormalizeUrl。
- 移除 `opus` 子命令：专栏 / 图文导出统一走根命令自动识别（`BBDown <专栏地址>`、`BBDown opus{id}`、`BBDown cv{id}`）；裸数字不再触发专栏，保留给视频 av 号简写。
- 列表型输入的「UP 主页」展示收紧：仅当列表内全部视频归属同一 UP 时才打印主页链接，混合多个 UP 的列表（稍后再看、收藏夹等）不再打印仅代表首个视频作者的误导性主页。

### 修复

- 修复交互式选择清晰度与 `--hide-streams` 的冲突消解失效：此前修正结果未贯穿到下载阶段，`-iaq -hs` 会不显示流列表却仍要求输入序号；现冲突在管道入口统一消解，手动选择时强制显示全部流。
- 修复 FLV 源交互式选择清晰度不生效：重解析后仍沿用首次解析的分段列表，实际下载的始终是默认清晰度；现按所选清晰度刷新分段列表。
- 修复 serve 任务可远程触发控制台输入阻塞：交互式选项（清晰度 / 逐集确认）依赖本地 stdin，现从 serve 请求契约移除，远程请求无法再让任务阻塞在 `Console.ReadLine`。
- 修复配音流序号越界崩溃：多角色配音流数少于主音频时，序号越界的角色不再抛异常，跳过该角色的配音并告警。
- 修复 article/view 版图片（para_type 3 的 line.pic）被误判为分割线、图片丢失的问题，现正确渲染为图片段落。
- 修复 link_card 无跳转地址（如 article/view 版角色卡只有 show_text）时输出 `> []( )` 空链接，改为输出带标题的引用文本。
- 修复旧版专栏 HTML 转换中 img / figure / 样式标签被剥壳消失的问题，现按原样保留。
- 修复纯图文动态 opus 下载失败：此前仅凭 `basic.rid_str` 判定专栏，动态的 rid_str 不是 cv 号，会请求正文接口返回 404；现按 `item.type` 区分专栏（1）与动态（0）。
- 修复图文动态正文解析失效：`MODULE_TYPE_CONTENT` 模块类型字段读取错误，导致动态正文被判为空。

## [v2.0.0-alpha.2]

### 新增

- 专栏 / 图文动态下载：新增独立 `opus` 子命令，支持图文导出；新增 `--no-images` 选项（导出专栏时不下载图片，Markdown 中保留远程图片链接）。
- UP 主空间投稿列表下载：URL 或 `space{mid}` 解析为空间投稿列表。
- 充电专属视频试看识别：命中试看片段时默认中止并提示，加 `--allow-preview` 可放行（输出文件名带 `[试看]` 前缀）。
- serve 新增 CORS 支持：仅当显式传入 `--cors-origin` 时才对该单一来源开放，默认关闭。
- 番剧详情页（`md{数字}`）解析为对应番剧，默认下载整季全部正片分集（`md2539` 或 `https://www.bilibili.com/bangumi/media/md2539`；内部编码为 `ep:ss{季_id}`，复用既有番剧整季链路）。
- 智能修复（AI 超分，qn=100）画质：番剧 / 课程 playurl 与番剧播放页请求改用含 8192 智能修复位的 fnval（12240）；WEB 端点按 PGC/UGC 分发（UGC 保持 4048，避免带该位返回 -400），TV 端点始终 4048。
- 智能修复权限提示：当 `support_formats` 声明该档但 dash 实际缺失对应轨道时，提示需登录大会员账号后重试。
- 评论区下载：新增 `--comment N`（默认 `0` 不下载，前 N 条）、`--comment-sort hot|time`（默认热度）、`--comment-formats json,txt`（默认两者都导出）、`--full-comment`（额外翻页抓全楼中楼）。走 `/x/v2/reply/wbi/main`（WBI 签名 + 游标分页），产物为 `<标题>.comments.json` / `<标题>.comments.txt`；按 `aid` 去重，与视频下载互不干扰，抓取失败降级为「拿到多少算多少」。`CommentFormat` 与弹幕格式解析逻辑各自独立。
- 直播录制：传入直播间地址（`live:` / `live.bilibili.com` / `m.live.bilibili.com`，房间短号自动换算）即可录制；新增 `--live-quality` / `-lq` 选项指定清晰度，默认原画（10000），可选 250 超清 / 400 蓝光 / 15000 2K / 20000 4K / 30000 杜比。录制为独立链路，不经 `WorkContext` 与音视频混流主干，拉取 `http_stream` + `flv` 流地址后分段落盘；录制中 `Ctrl+Break` 停录并合并为单个 mp4，`Ctrl+C` 中断则保留分段不合并。
- serve 单任务取消：新增 `POST /stop-task/{id}`，取消单个运行中 / 排队中的任务，不影响其余任务（全局 `Ctrl+C` 仍取消所有任务）。
- 内容组合选择：新增 `--get` / `-g`（默认 `avmsCiM`）、`--with` / `-w`、`--without` / `-W`，以字符集组合下载内容（get ∪ with − without），多个 `--get` / `--with` / `--without` 自动合并。字符含义：`a` 音频、`c` 独立封面、`C` 封面混流、`d` 弹幕、`i` 专栏图片、`m` 混流元数据、`M` 专栏 YAML front matter、`o` 评论、`O` 全部评论、`S` AI 字幕、`s` 字幕、`v` 视频。
- API 通道单选：新增 `--api` / `-a`（默认 `web`，可选 `web` / `tv` / `app` / `intl`，忽略大小写），取代 `--tv-api` / `--app-api` / `--intl-api` 三个独立开关。
- 评论选项更名：`--comments-count` / `-cn`、`--comments-sort` / `-cs`、`--comments-formats` / `-cf`（原 `--comment` / `-cm`、`--comment-sort` / `-cms`、`--comment-formats` / `-cmf`）。
- 仅解析选项更名：`--info-only` / `-i`（原 `--show-info` / `-info`）。

### 变更

- 主项目按职责拆分命名空间与子文件夹（`Cli` / `Pipeline` / `Media` / `Mux` / `Serve` / `Download` / `Auth` / `Util`），并引入 `DownloadSession` / `DownloadTask` / `PageOutcome` / `AppEnv` 等贯穿全链路的契约类型。
- CLI 与 serve 共用统一的三段式下载主干；分 P 下载改用 `DownloadSession` 传参。
- Core 接口层拆分出 `PlayUrl` / `SignUtil` / `ViewPointUtil`（内部架构调整）。
- serve 进一步加固（SSRF 防护等）。
- 进度条采样逻辑重构，进度更新上移至 `DownloadTask`。
- 直播录制状态机具备断流退避重连、CDN failover、静默超时检测与磁盘满 / 连续失败保护；首段成功后锁定编码，避免失败轮换串编码导致合并时静默丢段。分段 FLV 合并为 mp4 时按编码分派 bitstream filter（avc → `h264_mp4toannexb`、hevc → `hevc_mp4toannexb`）。
- serve 启动参数收窄为 `ServeConfig` record；任务持有与进程级关停令牌 `Link` 的 `CancellationTokenSource`（`Cts` 标记 `[JsonIgnore]`，不进入任务 DTO 序列化）。
- 统一 DASH / FLV 混流收尾逻辑；`TrackSelect` 收口交互选轨与 FLV 流信息打印。
- 文案打磨、启用 HTTP/2、收藏夹多 P 并行拉取。
- `ss{数字}` 番剧季号默认下载整季（原仅下载首集），与 `md` 入口行为对齐；`ss` / `md` 均编码为 `ep:ss{季_id}`，无需新增内部 id 前缀。
- 轨道排序改用 `Config.QualityRank` 权重（取代原先隐式 qn 数值降序）：默认原生 1080P 优先于智能修复，未收录档位按 qn 数值插入位而非一律甩到末尾。
- 选轨与 playurl 查询等内部方法开放为 `internal` 以便单元测试覆盖。
- CI：Release 说明改为从 `CHANGELOG.md` 抽取对应版本小节（不再依赖自动生成 notes）。
- 内容选择选项整体移除：`--video-only` / `--audio-only` / `--danmaku-only` / `--cover-only` / `--sub-only` / `--danmaku` / `--no-sub` / `--no-cover` / `--no-metadata` / `--full-comment` / `--allow-ai` / `--no-images`，改用 `-g` / `-w` / `-W` 字符集表达（如 `-g a` 仅音频、`-W s` 不下载字幕、`-w S` 下载 AI 字幕、`-w d` 附带下载弹幕、`-g O` 全量评论）。
- 评论下载触发条件变更：需内容集含 `o` / `O` **且** `--comments-count > 0` 才真正抓取；`O` 替代 `--full-comment` 控制楼中楼深度。
- 专栏导出行为变更：内容集默认含 `M`，默认输出 YAML front matter；图片下载由 `i` 控制（`-W i` 不下载图片、`-W M` 不输出 front matter）。
- 非法 `--api` 值在命令行报错退出；serve 请求体中的 `Api` / `Content` 使用字符串表达，非法值回落默认。

### 修复

- FLV 分支拒绝 HEVC / AV1 轨道。
- 修复交互选轨越界。
- 修复 `--cover-only` 与 FLV `isHevc` 判定问题。
- 修复番剧详情页 URL 正则误捕获 `md` 前缀，导致 `?media_id=md2539` 返回 `-400` 的解析失败。
- 番剧 / 季号接口查不到时不再因缺 `result` 抛出 `KeyNotFoundException`，改为带错误码的可读异常；`ss` 形态与非纯数字 id 不再静默回退课程接口，避免误命中 id 空间稠密、毫不相关的课程。
- 修复 `ep` / `ss` / `md` 番剧分集时长恒显示为 `00m00s`：此前构造分集时未读取接口返回的 `duration`；现按 PGC 的毫秒单位换算为秒后填入，分集列表与体积估算随之恢复正确。

## [v2.0.0-alpha.1]

### 新增

- 断点续传：基于显式分片清单 + SHA256 指纹（`PartFile`），新增 `--stop-on-error` 选项（遇错停止而非全程重试）。
- 凭据统一为单一 `BBDown.data` 文件（JSON 源生成器序列化，Cookie / refresh_token / TV / APP 凭据合并存储、互不覆盖）。
- 登录逻辑整合为统一的 `Login` 流程：Web / TV / APP 三态登录，扫码登录抽离为可测接口，落地 `refresh_token` 合并与主动续期。
- 新增 `InputResolver` 统一输入解析（av / BV / ep / ss / cheese / 收藏夹 / 合集 / 系列 / 空间等）。
- 重写配置文件解析为 `ConfigParser`，选项 `--config`（原 `--config-file`）。
- 新增选项：`--all`（下载全部分 P）、`--no-metadata`（不写元数据），以及短选项别名 `-a` / `-d` / `-s` / `-v`。
- 多 P 选择语法增强（`--select-page`）。
- 发布产物改为 AOT 单文件原生可执行（无需安装 .NET 运行时）。

### 变更

- 命令行解析迁移到 System.CommandLine。
- 大量 CLI 选项重命名 / 反转（升级时请注意）：
    - `--config-file` → `--config`
    - `--download-danmaku` → `--danmaku`（新增短选项 `-d`）；`--download-danmaku-formats` → `--danmaku-formats`
    - `--use-app-api` → `--app-api`；`--use-tv-api` → `--tv-api`；`--use-intl-api` → `--intl-api`；`--use-aria2c` → `--aria2c`；`--use-mp4box` → `--mp4box`
    - `--save-archives-to-file` → `--save-records`
    - `--language` → `--lang`
    - `--skip-cover` → `--no-cover`；`--skip-subtitle` → `--no-sub`
    - `--only-show-info` → `--show-info`
    - 选项反转：`--force-http` → `--no-force-http`；`--force-replace-host` → `--no-force-host`；`--multi-thread` → `--single-thread`；`--skip-ai` → `--allow-ai`
- WBI 签名与请求头对齐 bilibili-API-collect。
- 临时文件生命周期改为显式分片清单 + try/finally + 重试删除，避免音视频临时文件互相污染。
- Ctrl+C 优雅取消（`CancellationToken`）贯穿全链路（含外部子进程取消时杀进程）。
- aria2c / ffmpeg / mp4box 改用 `ArgumentList` 拼参。
- 字幕处理合并为候选表，语言映射改为数据表；番剧 → 课程回退改用语义化异常与候选链。
- 日志格式精简。
- 整库代码现代化、消除全部编译器警告、固化 `.editorconfig`、移除 ImplicitUsings 并补全显式 using、AOT 友好改造。

### 已弃用

- 删除兼容旧版本命令行参数的代码。

### 移除

- 彻底移除 Docker 支持（`Dockerfile` 删除）。
- 移除一批 CLI 选项：`--only-av1` / `--only-avc` / `--only-hevc`（编解码选择）、`--bandwith-ascending`、`--add-dfn-subfix`、`--aria2c-proxy`、`--no-padding-page-num`、`--show-all`、`--simply-mux`。
- 删除 `BBDownConfigParser` / `BBDownDownloadUtil` / `BBDownLoginUtil` / `BBDownMuxer` / `BBDownEnums` 等巨型 god-class（拆分为职责单一的模块）。
- 删除 `json-api-doc.md`。

### 修复

- 修复 Web 扫码登录取不到 Cookie / SESSDATA。
- 修复 gRPC 帧解析、收藏夹 id 拆分、bvid 转换等越界崩溃点。
- 重试合并为单层指数退避，异常不再静默吞掉。
- 未收录的 qn 不再抛 `KeyNotFoundException`。
- 修复 ASS 弹幕时间戳在分钟边界进位成 60 秒、颜色解析失败时产出畸形 ASS 标签。
- 修复混流失败此前永远检测不到、MergeFLV 绕过 `--ffmpeg-path`。
- 修复 serve 短任务导致接口 500（任务表改无锁并发容器）。
- 修复 dash 取流死循环。
- cheese：消除 ss 冗余请求、intl 自动回退、过滤锁定分集。
- 消除 JSON 子串嗅探，改用结构化判定。
- 修复 `--host` / `--ep-host` / `--tv-host` 默认值被覆盖为空的回归、以及配置文件完全失效。
- 补齐 `HttpResponseMessage` / `JsonDocument` 释放，消除跨文档悬垂 `JsonElement`。

### 安全

- 整体安全加固。
- serve 不再接受请求注入可执行路径、附加参数与工作目录；不再改写进程全局 `CurrentDirectory`，落盘路径全部绝对化。
- 可执行文件查找不再优先当前目录。
- aria2c / ffmpeg / mp4box 改用 `ArgumentList`，消除命令行参数注入。

[v2.0.0-alpha.1]: <https://github.com/KaiHuaDou/BBDownNext/compare/259a5558cee0a349a7ebb60bd31e40c88e5bc1ed...v2.0.0-alpha.1>
[v2.0.0-alpha.2]: <https://github.com/KaiHuaDou/BBDownNext/compare/v2.0.0-alpha.1...v2.0.0-alpha.2>
[v2.0.0-alpha.3]: <https://github.com/KaiHuaDou/BBDownNext/compare/v2.0.0-alpha.2...v2.0.0-alpha.3>
[v2.0.0-beta.1]: <https://github.com/KaiHuaDou/BBDownNext/compare/v2.0.0-alpha.3...v2.0.0-beta.1>
[v2.0.0-beta.2]: <https://github.com/KaiHuaDou/BBDownNext/compare/v2.0.0-beta.1...v2.0.0-beta.2>
