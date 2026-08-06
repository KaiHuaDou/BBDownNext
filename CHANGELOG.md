# 更新日志

本项目的所有显著变更都将记录在此文件中。

本项目遵循 [语义化版本](https://semver.org/lang/zh-CN/) 约定，文件格式基于 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)。

本文件的内容基于对代码实际差异的比对（而非提交信息），以准确反映用户可见的行为变化。

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

## [2.0.0-alpha.1] - 2026-08-03

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

[2.0.0-alpha.1]: https://github.com/KaiHuaDou/BBDownNext/compare/259a5558cee0a349a7ebb60bd31e40c88e5bc1ed...v2.0.0-alpha.1
[v2.0.0-alpha.2]: https://github.com/KaiHuaDou/BBDownNext/compare/v2.0.0-alpha.1...v2.0.0-alpha.2
