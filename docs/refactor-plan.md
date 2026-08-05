# BBDown 重构计划（依赖树化 · 纯函数/静态函数优先）

> 目标：把当前「全局可变单例 + 可变 God Object 贯穿 + 双向跨层引用」的结构，收敛为一棵**单向、无环、分层清晰**的依赖树；
> 改造全程保持 .NET 9 AOT 兼容（源生成器、`TreatWarningsAsErrors`、`Nullable`）。
> 原则：**偏好纯函数与静态函数**，不引入 DI 容器 / `I*Service` 接口 / 工厂类等过度包装。

---

## 0. 设计原则（落地为硬约束）

1. **依赖单向成树**：上层命名空间可 `using` 下层，反之禁止。任意 `using` 逆层即 CI 失败。
2. **请求级状态绝不进进程级可变全局**：凡是「每次下载都不同」的值（二进制路径、UA、Debug 开关、凭据）一律做成**不可变值对象**，由调用方作为参数显式传入；全局只允许存在「进程启动后不再变化」的只读常量。
3. **函数只拿它真正需要的入参**：用窄入参 record 取代「把整包 `DownloadOptions` 丢给每个阶段」。
4. **消除占位字段上下文**：上下文 record 不预留空字段等后续阶段回填；能一次算清的放进 `RunConfig`，算不清的作为该阶段返回值向上传递。
5. **分发用数据表，不用控制流**：Fetcher / API 模式选择等用「谓词表 + 静态函数」声明式表达，避免中央 `if/else` 长链。

---

## 1. 现状耦合诊断（按严重度排序）

| # | 问题 | 位置 | 为什么是耦合/缺陷 |
|---|------|------|------------------|
| C1 | **请求级状态泄漏到进程级可变全局** | `Muxer.ffmpeg/mp4box` (`Mux/Muxer.cs:22-23`)、`BBDownAria2c.aria2c` (`Download/BBDownAria2c.cs:15`)、`Config.DebugLog` (`Core/Config.cs:10,79`)、`HTTPUtil.UserAgent` (`Core/Util/HTTPUtil.cs:83`) | `WorkSetup.FindBinaries`/`Build` 在每次任务里 **写** 这些静态字段（`WorkSetup.cs:21,39,169-227`）。`serve` 开 `--max-concurrent N` 时，并发任务互相踩踏这些全局 → 数据竞争 + 跨任务污染（严重并发 bug）。 |
| C2 | **可变 God Object 贯穿全链路** | `DownloadOptions`（`DownloadOptions.cs`，70+ 可变属性） | 每个阶段只用到其中 2~5 个字段，却拿到整个可变对象；且对象在途被原地改写（`HandleConflictingOptions`、`ResolveWorkDir` 改 `myOption.WorkDir`，`WorkSetup.cs:232-271`）。调用方无法保证「传入后不被改」。 |
| C3 | **Pipeline ↔ Serve 双向依赖（破环）** | `DownloadPipeline.RunAsync(DownloadOptions, DownloadTask? relatedTask, ct)` (`Pipeline/DownloadPipeline.cs:15`)；`ARCHITECTURE.md` 明确「Pipeline 引用 Serve 的 DownloadTask」「Serve 引用 Pipeline」 | 下载主干反向依赖 serve 的状态类型，违反树状依赖；serve 类型被当作「出参」塞进主干。 |
| C4 | **字符串前缀中央分发** | `FetcherRegistry.FetchAsync` (`Core/Fetcher/FetcherRegistry.cs:11-62`) | 一长串 `id.StartsWith(...)` 顺序分支；新增输入类型必须改中央方法，且优先级隐含在分支顺序里，易错。 |
| C5 | **占位字段上下文（伪不可变）** | `WorkContext`（`WorkContext.cs`，19 字段，其中 `SavePathFormat/FetchedAid/VInfo/ApiType` 由后续阶段回填，`ARCHITECTURE.md` 自述「只给空值占位」） | record 名义不可变，实际靠 `with` 在阶段间反复重建补全，等价于可变上下文包；调用方不知道哪些字段「当下有效」。 |
| C6 | **HttpClient 经全局可变注入测试** | `HTTPUtil.AppHttpClient`（`Core/Util/HTTPUtil.cs:26`，`internal set`） | 测试靠 `InternalsVisibleTo` 改写全局静态来 stub；并发下不可控，且把「传输层」和「全局单例」绑死。 |
| C7 | **入口 God Method** | `Program.Main`（`Program.cs`，283 行） | 同时装配 root/login/opus/serve 命令与路由，CLI 装配与业务分发混在一起。 |
| C8 | **全局取消令牌** | `AppEnv.CancellationToken`（`AppEnv.cs`，进程级单例） | CLI 下合理；`serve` 多任务共用同一令牌，无法单独取消某个任务（只能整体 Ctrl+C）。 |

---

## 2. 目标依赖树（单向、无环）

```
Program  (入口: CLI 装配 / 信号 / 进程接线)            ← 根，仅引用下层，不被任何层引用
 ├─ BBDown.Cli          (命令行解析 → DownloadRequest)
 ├─ BBDown.Serve        (ASP.NET Minimal API；只调用 Pipeline，回调内自管 DownloadTask)
 ├─ BBDown.Pipeline     (下载编排主干；只通过“进度/结果回调”上报，不引用 Serve 类型)
 ├─ BBDown.Opus         (专栏导出旁路)
 └─ BBDown.Live         (直播录制旁路)
       │
       ├─ BBDown.Auth       (登录 / 凭据读写)
       ├─ BBDown.Media      (单分P: DASH/FLV/选轨/字幕/弹幕/评论)
       ├─ BBDown.Mux        (混流 / 收尾 / 章节)
       ├─ BBDown.Download   (传输层: 续传 / CDN / aria2c)
       │
       └─ BBDown.Core       (API 调用 / 解析 / Fetcher / PlayUrl / Entity / Util / Config)
             │
             ├─ BBDown.Core.Util / Entity / Config   (纯函数与只读常量，禁止依赖任何上层)
             └─ Logger                                (环境副作用汇点，全局静态，AOT 安全)
       │
       └─ BBDown.Util        (Utils / SavePath / ProgressBar；纯函数，禁止依赖上层)
```

**硬规则（写入 CI 守护）**

- `BBDown.Pipeline/**` 禁止 `using BBDown.Serve;`（改回调参数，见 Phase 3）。
- 除 `Program` 外，禁止任何层 `using` 引用 `Program` / `AppEnv`（AppEnv 仅作「进程关闭令牌」来源，由 `Program` 注入）。
- `BBDown.Core` 禁止依赖 `BBDown`（已满足，保持）；`*.Util / Entity / Config` 禁止依赖 `Media/Mux/Download/Pipeline/Serve`。
- 单向即树：不存在 `A→B` 且 `B→A`。

---

## 3. 重构路线（每阶段可独立合入、可回滚）

### Phase 0 — 建立依赖守护（防回归，先于一切）
- 在 `justfile` / CI 加一个廉价静态检查（grep 级，无需 Roslyn）：
  - `Pipeline/**` 出现 `using BBDown.Serve` → 失败；
  - 任何非 `Program` 文件出现 `using BBDown;` 且引用 `Program`/`AppEnv` → 失败；
  - `Core/**` 出现 `using BBDown;` → 失败。
- 在 `ARCHITECTURE.md` 第 2 节把「依赖方向」改成上面的树 + 硬规则。
- **验收**：守护脚本能在本仓库当前代码上通过（当前已满足方向约束，仅缺守护）。

### Phase 1 — 消除进程级可变全局（解决 C1/C6，最高优先级）
动机：这是真正的并发 bug，且是全局状态耦合的根源。

1. **`ToolPaths` 不可变 record**（新类型，置于 `BBDown` 根或 `BBDown.Mux`）：
   ```csharp
   internal readonly record struct ToolPaths(string Ffmpeg, string Mp4box, string? Aria2c);
   ```
   `WorkSetup` 的 `FindBinaries` 改为纯函数 `ToolPaths ResolveToolPaths(DownloadRequest req, string appDir)`，**返回值**而非写静态字段；`Muxer.ffmpeg/mp4box`、`BBDownAria2c.aria2c` 静态字段删除。
2. **下游改写**：`Muxer`/`BBDownAria2c` 的 `RunExe`/`RunAsync` 增加 `ToolPaths tools` 参数；`PageDownload`/`DashDownload`/`FlvDownload` 把 `ToolPaths` 从 `WorkContext`/`RunConfig` 透传下去（纯参数，不落地为静态）。
3. **`Config.DebugLog` 改为进程级一次性初始化**：`Config.SetDebugLog` 只在 `Program.Main`/serve 启动处调用**一次**（来自启动配置），不再在 `WorkSetup.Build` 每任务调用 → 消除并发写竞争。`LogDebug` 仍读 `Config.DebugLog`（环境副作用，可接受）。
4. **`HTTPUtil` 去全局可变注入**：
   - `AppHttpClient`/`StreamHttpClient` 保留为 `static readonly`（进程单例，合理）；
   - 新增带 `HttpMessageHandler? handler = null` 的重载（默认走单例），测试传 stub handler，**不再**靠 `InternalsVisibleTo` 改写全局。
   - `UserAgent` 改由启动配置一次性设定；`WorkSetup.Build` 不再调 `HTTPUtil.SetUserAgent`。
- **关键文件**：`Mux/Muxer.cs`、`Download/BBDownAria2c.cs`、`Pipeline/WorkSetup.cs`、`Core/Config.cs`、`Core/Util/HTTPUtil.cs`。
- **验收**：`serve --max-concurrent 4` 并发下载不同清晰度/不同二进制路径不互相污染；测试不再改写全局 HttpClient。
- **风险**：改动面大但机械；用现有 `ResumeDownloadTests` 等回归。

> **落地情况（✅ 1/2/3 完成，4 部分完成）**
> - 新增 `BBDown/ToolPaths.cs`；`WorkSetup.FindBinaries` → 纯函数 `ResolveToolPaths`；删除 `Muxer.ffmpeg`/`Muxer.mp4box`/`BBDownAria2c.aria2c` 三个可变静态字段。
> - `ToolPaths` 挂在 `WorkContext.Tools` 上向下透传；`Muxer.MuxAV`/`MergeFLV`、`ChapterMeta.CheckFFmpegDOVI`、`LiveMuxer.MergeSegmentsAsync` 增加 `ToolPaths` 入参；aria2c 路径经 `DownloadConfig.Aria2cPath` 传递。
> - `Config.SetDebugLog` / `HTTPUtil.SetUserAgent` 从 `WorkSetup.Build`（每任务）与 `LiveDownload.RunAsync` 上移到 `Program.RunApp`（每进程一次）。
> - 回归：`ProgramTests.ResolveToolPaths_*` 四例（显式路径优先 / 无 aria2c 为 null / 缺 ffmpeg 且需混流时抛错 / 多次调用互不影响）。
> - **待办**：item 4 的 `HttpMessageHandler` 重载尚未做，测试仍靠 `InternalsVisibleTo` 改写全局 HttpClient（C6 未清）。

### Phase 2 — `DownloadOptions` → 不可变 `DownloadRequest` + 阶段化窄入参（解决 C2）
动机：God Object 是「函数耦合到全量状态」的根源。

1. 把 `DownloadOptions` 改为 **不可变 record** `DownloadRequest`（字段同原，去掉 setter；`WithSecretsRedacted` 改为返回新 record 的纯函数）。
2. `WorkSetup.Build` 输入 `DownloadRequest`、输出不可变 `RunConfig`（见 Phase 5），**不再原地改入参**（`HandleConflictingOptions`/`ResolveWorkDir` 改为返回「修正后的 request 副本 + 解析结果」，不突变）。
3. 各阶段只声明它真正需要的入参（用窄 record 承载，避免 6+ 个散参）：
   - `VideoInfo.FetchAsync(RunConfig cfg, AppConfig app, CancellationToken)`；
   - `PageQueue.RunAsync(RunConfig cfg, VInfo vinfo, ProgressSink progress, CancellationToken)`；
   - `DashDownload.RunAsync(PagePlan plan, ToolPaths tools, AppConfig app, CancellationToken)` …
4. CLI / serve 各自把 `DownloadOptions`/`ServeRequestOptions` 投影成 `DownloadRequest`（已是 DTO，转换纯函数）。
- **关键文件**：`DownloadOptions.cs`、`Pipeline/WorkSetup.cs`、`Pipeline/VideoInfo.cs`、`Pipeline/PageQueue.cs`、`Media/*`、`Cli/ConfigParser.cs`、`Serve/ServeRequestOptions.cs`。
- **验收**：grep 不到 `myOption.X =` 这类途中赋值；单元测试可按「窄入参 record」直接构造，无需装配整个 request。
- **风险**：参数面变宽，靠窄 record 收敛；不强求一步到位，可按阶段逐步收窄。

### Phase 3 — 解耦 Pipeline ↔ Serve（解决 C3，恢复树）
动机：当前下载主干反向依赖 serve 状态类型，破坏树状依赖。

1. 定义 `Pipeline` 自有的、**不依赖 Serve** 的进度/结果契约（置于 `BBDown.Pipeline` 或根层）：
   ```csharp
   internal sealed record PipelineProgress(string Aid, string? Title, int DonePages, int TotalPages);
   internal sealed record PipelineResult(int ExitCode, IReadOnlyList<string> OutputFiles);
   ```
2. `DownloadPipeline.RunAsync` 签名改为：
   ```csharp
   Task<PipelineResult> RunAsync(DownloadRequest req, Action<PipelineProgress>? onProgress = null, CancellationToken ct = default);
   ```
   用回调上报标题/进度，`relatedTask` 出参消失。
3. `Serve` 侧：在调用 `RunAsync` 时提供 `onProgress` 闭包，闭包内把 `PipelineProgress` 映射到自己的 `DownloadTask` 并维护状态——**Serve 的复杂度留在 Serve，不污染主干**。
4. 删除 `DownloadPipeline` 对 `BBDown.Serve` 的 `using`。
- **关键文件**：`Pipeline/DownloadPipeline.cs`、`Serve/BBDownApiServer*.cs`、`Serve/ServeRequestOptions.cs`、`DownloadTask.cs`（留在根层供 Serve 自用）。
- **验收**：`grep -rn "using BBDown.Serve" BBDown/Pipeline` 为空；`serve` 端到端功能不变。
- **风险**：serve 回填标题/进度的代码需要迁移进闭包，逻辑量小。

> **落地情况（✅ 完成，契约形状与原计划不同）**
> 实际用 `BBDown/PipelineSink.cs` 的三回调值类型，而非单个 `Action<PipelineProgress>`：
> ```csharp
> internal readonly record struct PipelineSink(
>     Action<VInfo>? Meta,          // 视频元信息就位（标题/封面/发布时间）
>     Action<string>? Saved,        // 又落盘一个文件
>     Action<double, long>? Sample);// 进度采样（ratio, bytesDelta）
> ```
> 改动理由：这三件事发生在**不同层、不同时机**（`DownloadPipeline` / `MuxFinish`+`DashDownload`+`CommentDownload` / `DownloadUtil`），塞进一个 `PipelineProgress` 记录会逼出一堆无意义的空字段与合成 union。用 `readonly record struct` + 可空委托，CLI 直接传 `default`（三个回调全 null，下层 `?.Invoke` 天然跳过），不需要 `None` 单例。
> - `DownloadPipeline`/`PageQueue`/`PageDownload`/`CommentDownload` 的 `DownloadTask? relatedTask` 参数、`DownloadSession.RelatedTask`、`DownloadConfig.RelatedTask` 全部替换（后者收窄为 `Action<double,long>? OnSample`——下载层只需要数字）。
> - `DownloadTask.cs` 从根层**迁入** `BBDown/Serve/`（`namespace BBDown.Serve`）：它现在只有 serve 引用，放在根层会诱导下游重新依赖。
> - serve 侧用 `BBDownApiServer.SinkFor(task)` 一处构造闭包，可变状态收束在 Serve 内部。
> - 回归：`BBDownApiServerTests.SinkFor_RoutesCallbacksIntoTask` / `DefaultSink_HasNoCallbacks`。

### Phase 4 — FetcherRegistry 声明式化（解决 C4）
动机：中央 `if/else` 长链 = 隐式优先级 + 改中央文件。

1. 定义静态只读分发表（数据，不是控制流）：
   ```csharp
   file delegate Task<VInfo> FetchFn(string id, AppConfig cfg, CancellationToken ct);
   private static readonly (Func<string, bool> Matches, FetchFn Fetch, bool Fallback)[] Routes =
   [
       (s => s.StartsWith(IdPrefix.Cheese),   CheeseInfoFetcher.FetchAsync,   false),
       (s => s.StartsWith(IdPrefix.EpColon),  FetchEpisode,                    false), // FetchEpisode 内部再做 bangumi/cheese 回退
       (s => s.StartsWith(IdPrefix.ListBizId),(s,c,t)=>MediaListFetcher.FetchAsync(s,c,t), true), // 末项回退到系列
       ...
   ];
   ```
2. `FetchAsync` 改为遍历 `Routes`：首个 `Matches` 命中即调用；`Fallback=true` 的项作为「都没命中时的兜底」。回退链（合集→系列、番剧→课程）拆成独立的纯函数 `FetchEpisode`/`FetchListWithSeriesFallback`，不再藏在 `if` 里。
3. 各 Fetcher 保持**静态函数** `Task<VInfo> FetchAsync(string, AppConfig, CancellationToken)`，不引入 `IFetcher` 接口（那会是过度包装）。
- **关键文件**：`Core/Fetcher/FetcherRegistry.cs`、`Core/Fetcher/*.cs`。
- **验收**：新增一种输入类型只需往 `Routes` 加一行；行为等价原分支顺序。
- **风险**：回退语义需逐条对照单测（已有 `BangumiMdTests` 等可覆盖）。

> **落地情况（✅ 完成）**
> 六条路由进表，`NormalInfoFetcher` 作为「都没命中」的默认分支直接写在循环之后（比给表加 `Fallback` 布尔列更直白，少一个字段）。
> 委托签名带上了 `bool useIntlApi`（`ep:` 分支需要），不需要它的路由用 `_` 丢弃。回退链拆成私有纯函数 `FetchEpisodeAsync` / `FetchMediaListWithSeriesFallback`。未引入 `IFetcher`。

### Phase 5 — 上下文记录消肿（解决 C5）
动机：占位字段上下文让「有效字段集合」在阶段间漂移，等于隐式可变状态。

1. `WorkSetup.Build` 输出 **`RunConfig`**（不可变，一次性算清）：含 `EncodingPriority/DfnPriority/FirstEncoding/DownloadDanmaku*/Comment*/Input/WorkDir/Lang/Delay/ToolPaths/AppConfig` 等**启动即可确定**的全部值；不再有空占位。
2. 真正「跑中才得到」的（`VInfo`、`FetchedAid`、`ApiType`、`SavePathFormat`）**不作为上下文字段**，而是：
   - `VInfo` 由 `VideoInfo.FetchAsync` 返回，向下传给 `PageQueue`；
   - `ApiType`/`FetchedAid` 仅在需要它们的局部作用域内用返回值传递，不回填进共享 record。
3. `WorkContext` 类型可整体退役，替换为 `RunConfig` + 局部返回值；`PageContext` 保留（它字段都是「单分P 已知」的真实值，无占位，可不动）。
- **关键文件**：`WorkContext.cs`(删)、`Pipeline/WorkSetup.cs`、`Pipeline/VideoInfo.cs`、`Pipeline/PageQueue.cs`、`Pipeline/DownloadPipeline.cs`。
- **验收**：不存在「建空值占位再 `with` 补全」的 record；grep 不到 `SavePathFormat = ""` 这类初始化。
- **风险**：需同步改所有引用 `WorkContext` 的签名；可随 Phase 2 一起做。

> **落地情况（✅ 完成；含 Phase 2 item 3 深层收尾）**
> - `DownloadOptions` 改名为不可变 record `DownloadRequest`（`git mv DownloadOptions.cs → DownloadRequest.cs`，全仓 29 个文件做 `DownloadOptions`/`DownloadRequest` 及 `DownloadOptionsJsonContext`/`DownloadRequestJsonContext` 重命名）；所有属性 `init`，`WithSecretsRedacted` 返回 `with` 副本。
> - 在途修正一律返回「修正后的副本」而非原地改写入参（C2）：`HandleConflictingOptions`、`NormalizeOptionsAfterFetch`（TV/INTL 在拿到视频信息后翻转 `UseTvApi/UseIntlApi`）、`ApplyServeWorkDir`、`ApplyServeHost`、`ResolveWorkDir` 全部改为 `with` 返回新副本；调用方信赖「传入后不被改」。
> - `WorkSetup.Build` 输入 `DownloadRequest`、输出不可变 `RunConfig`（仅装启动即可确定的值：优先级表、弹幕/评论格式、Input、Lang、Delay、ToolPaths、WorkDir）；`VideoInfo.FetchAsync` 返回 `(DownloadRequest Effective, FetchResult Fetch)`，`FetchResult` 携带「跑中才得到」的 `VInfo/Cfg/FetchedAid/ApiType`，向下透传而非回填（C5）。
> - `WorkContext` 仍在 `PageQueue.RunAsync` 中**一次性组装**（消除了原 `ctx = ctx with { SavePathFormat = ... }` 的空占位 + 补全漂移），`SavePathFormat` 用 `SavePath.Resolve` 算清一次后直接传入。
> - serve 投影：`ServeRequestOptions.ToDownloadRequest` 经 JSON 往返 + `with` 显式把主机可控字段回落安全默认值（FFmpegPath/Mp4boxPath/Aria2cPath/Aria2cArgs/WorkDir/FilePattern/MultiFilePattern/UserAgent → `""`，Host/EpHost/TvHost → `BiliApi` 官方默认）。**注意**：record 经 STJ 反序列化时字段初始化器被跳过（改用生成构造器、字符串参数默认 `null`），故必须在 `with` 里兜底，否则会回流 `null`；并顺手让 `BBDownAria2c.SplitArgs` 对 `null` 入参返回空列表，避免深链路 NRE。
> - 回归：`ProgramTests.HandleConflictingOptions_*`、`BBDownApiServerTests.ServeRequestOptions_ToDownloadRequest_IgnoresHostControlledInjection`；`dotnet test` 全绿（BBDown.Tests 451 / BBDown.Core.Tests 421），`just check-deps` 通过，Debug/Release 0 警告 0 错误。
> - **Phase 2 item 3 深层收尾（✅ 已完成）**：`Muxer.MuxAV` 的 20 余入参收敛为不可变 `MuxRequest` record（`Muxer.MuxAV`/`BuildFFmpegArgs`/`BuildMp4boxArgs` 统一消费 `MuxRequest req`，`MuxFinish` 负责组装并 `with` 折叠 `AudioOnly`/`VideoOnly` 后的路径）；`PageAssets.PrepareAsync` 收窄为接收窄 `DownloadSession`（与已有的 `DownloadDanmakuAsync` 一致），`PageDownload` 调整顺序——先建 `DownloadSession`（含 `IsPreview` 最终值），再 `PrepareAsync` 填字幕，再 `with { Subtitles = ... }`，保证 `SavePath.cs` 经 session 读到的 `IsPreview` 正确。`DashDownload`/`FlvDownload` 本已采用窄 `DownloadSession`，无需改。Debug/Release 0 警告 0 错误，`just check-deps` 通过，测试全绿（BBDown.Tests 451 / BBDown.Core.Tests 421）。

### Phase 6 — `Program.Main` 拆分命令构造器（解决 C7）
动机：283 行 God Method 同时做装配与路由。

1. 抽静态构造器（返回 `Command`/`RootCommand`，无业务逻辑）：
   - `RootCommand BuildRootCommand(Func<DownloadRequest,bool,bool,Task<int>> run)`（原 `GetRootCommand` 已近此形，保留）
   - `Command BuildLoginCommand()`、`Command BuildServeCommand(ServeConfig)`、`Command BuildOpusCommand()`
2. `Main` 只做：接线信号处理 → 装配命令 → 解析 → 调 `RunApp`。`RunApp` 的异常/退出码映射保持为一个纯函数 `int MapExitCode(Exception, bool isChargedOnly)`。
- **关键文件**：`Program.cs`、`Cli/CommandLineInvoker.cs`。
- **验收**：`Main` 行数显著下降；各命令可独立单测构造。
- **风险**：低。

### Phase 7 — `serve` 单任务取消 + 收尾（解决 C8）
动机：多任务共用全局令牌无法单独取消。

1. `Serve` 为每个任务建 `CancellationTokenSource taskCts`，与 `AppEnv.CancellationToken`（进程关闭）`Link` 合并后作为 `ct` 传入 `RunAsync`；停止任务 = `taskCts.Cancel()`。主干仍只认 `CancellationToken`，不感知 serve。
2. 全量回归：AOT 发布（`dotnet publish -p:PublishAot=true`）通过；`BBDown.Tests` / `BBDown.Core.Tests` 全绿。
- **关键文件**：`Serve/BBDownApiServer.cs`、`AppEnv.cs`（保持全局仅作关闭源）。
- **验收**：serve 可单独取消某任务而不影响其他任务；Ctrl+C 仍整体退出。

---

## 4. 反模式 ↔ 正例对照

**C1 全局可变单例 → 显式不可变入参**
```csharp
// 反例（当前）
Muxer.ffmpeg = myOption.FFmpegPath;        // 进程级可变，并发互相踩
await Muxer.RunExe(Muxer.ffmpeg, args, ct);
// 正例
ToolPaths tools = ResolveToolPaths(req, AppEnv.AppDir);   // 纯函数，返回值
await Muxer.RunExe(tools.Ffmpeg, args, ct);
```

**C2 可变 God Object → 不可变 request + 窄入参**
```csharp
// 反例：函数签名只用到 3 个字段却拿到整个可变体
void Stage(DownloadOptions o) { if (o.AudioOnly) ... }
// 正例：只声明所需
void Stage(bool audioOnly, bool videoOnly, ToolPaths tools, CancellationToken ct)
// 或包成窄 record： Stage(PagePlan plan, CancellationToken ct)
```

**C3 双向依赖 → 回调解耦**
```csharp
// 反例：主干引用 Serve 的 DownloadTask 作为出参
Task RunAsync(DownloadOptions o, DownloadTask? relatedTask, CancellationToken ct);
// 正例：主干只认自己的进度契约，Serve 在闭包里自行映射
Task<PipelineResult> RunAsync(DownloadRequest req, Action<PipelineProgress>? onProgress, CancellationToken ct);
```

**C4 字符串前缀分发 → 声明式路由表**
```csharp
// 反例：中央 if/else 长链，顺序即优先级
if (id.StartsWith(Cheese)) return CheeseInfoFetcher.FetchAsync(...);
if (id.StartsWith(EpColon)) return FetchEpisodeAsync(...);
...
// 正例：数据表，新增类型只加一行
private static readonly (Func<string,bool> Matches, FetchFn Fetch, bool Fallback)[] Routes = [ ... ];
```

---

## 5. 实施顺序与里程碑

| 里程碑 | 包含 Phase | 状态 | 可交付 / 可回滚点 |
|--------|-----------|------|------------------|
| M0 | Phase 0 | ✅ 已完成 | 依赖守护上线（`just check-deps`），零行为变更 |
| M1 | Phase 1 | ✅ 已完成 | `ToolPaths` 不可变快照取代 `Muxer.ffmpeg`/`mp4box`/`BBDownAria2c.aria2c`；`Config.DebugLog`/`HTTPUtil.UserAgent` 收敛到 `RunApp` 单次设置，`serve` 并发安全 |
| M2 | Phase 2 + 5 | ✅ 已完成 | request 不可变化（`DownloadRequest` 不可变 record）+ 上下文消肿（`RunConfig`/`FetchResult` 拆分、`WorkContext` 一次组装） |
| M3 | Phase 3 | ✅ 已完成 | `PipelineSink` 回调取代 `DownloadTask` 透传；`DownloadTask` 迁入 `BBDown.Serve`，依赖树闭合 |
| M4 | Phase 4 | ✅ 已完成 | `FetcherRegistry` 声明式路由表（无 `IFetcher` 接口） |
| M5 | Phase 6 + 7 | ⬜ 未开始 | 入口拆分 + serve 单任务取消 |

**总原则**：每个里程碑结束都保持 `dotnet build` + 测试绿 + AOT 发布可过；不追求一步到位，按里程碑小步合入。

### 已落地的守护（`just check-deps`）

| 断言 | 防止的回归 |
|------|-----------|
| `BBDown/{Pipeline,Media,Download,Mux}` 不含 `using BBDown.Serve;` | C3 反向依赖复发 |
| `BBDown/{Pipeline,Media,Download,Mux}` 不出现 `DownloadTask` | 下载链路重新持有 serve 可变对象 |
| 仅 `Program.cs` 可 `using BBDown.AppEnv;` | C8 全局取消令牌扩散 |
| `BBDown.Core` 不含 `using BBDown;` | Core 反向依赖宿主 |
| 全仓不出现 `static string ffmpeg/mp4box/aria2c` | C1 进程级可变静态字段复活 |

---

## 6. 不做的（避免过度重构 / 过度 OOP）

- **不引入** `IServiceProvider` / `IHttpClientFactory` / 仓储 / 单位-of-work 等 DI 与抽象层——与「纯函数/静态函数优先」相悖。
- **不把** 每个静态类改成实例化服务；`HTTPUtil`/`Muxer`/`Utils` 等保持静态，仅把「状态」从静态字段改为入参。
- **不为** Fetcher 抽 `IFetcher` 接口；`AppConfig` 已是不可变 record，保持「值透传」而非「对象持有依赖」。
- **不**把 `Logger` 改成可注入 `ILogger` 并逐层透传——日志是环境副作用，保留全局静态汇点（`Log`/`LogWarn`/`LogDebug`），符合 AOT 与简约原则。
