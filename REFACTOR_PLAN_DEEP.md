# BBDown 深度激进重构计划（避免过度 OOP · 可控泄露抽象）

> 适用基线：Phase 0–5 已完成（API 迁移、功能缺陷修复、死代码清理、去重、零警告基线）。
> 约束（用户要求）：**避免过度 OOP**；**只做「可以手动控制泄露」的抽象**；动作可分步、可回滚、不破坏 CLI 行为兼容。
> 文档性质：计划。每个 Phase 独立 commit，构建必须 0 错误，冒烟通过后再合入下一 Phase。

---

## 0. 总纲：两条硬约束

### 0.1 避免过度 OOP
本项目当前最大的"屎山放大器"不是代码量，而是**为了共享而引入的 ceremony**：`IFetcher` 接口 + 8 个类 + `FetcherFactory` if/else、`Config` 静态全局可变、`Page` 5 个构造器、靠 `new XxxFetcher()` 跨类耦合。

本计划明确**不做**以下事：
- 不为单一实现写 `interface`（如 `IFetcher`）。
- 不写「仅为了复用代码」的 `abstract` 基类（用 `static` 方法 / 高阶函数 / 函数表替代）。
- 不用虚方法分派替代 `switch` / `Dictionary` 派发。
- 不引入 DI 容器 / Service Locator（全局可变状态才是真问题，靠**显式传参 / 不可变快照**解决，而非靠容器藏起来）。
- 不新建"层"（Service / Manager / Handler 等名词类）来包裹本来就平铺的逻辑。

本计划**倾向**的做法：
- 实体保持**纯数据**（`record` / 普通类，无方法），行为放**模块级纯函数**。
- 用 `partial class` / 多文件拆分巨型文件，**而非新建类型**。
- 派发用**函数表 / `switch` 表达式 / 模式匹配**，而不是类继承树。
- 共享用**组合 + 函数参数**，而不是基类。

### 0.2 「可控泄露的抽象」是什么
所有抽象都会泄露（Leaky Abstraction）。区别在于**你能不能手动戳穿它**：

- **可控泄露抽象**：默认隐藏复杂度，但保留一个**明确的逃生舱**，让你能拿到它封装的原始原语，而不必改写整个抽象。
  - 例：`Http` 模块封装常用请求，但额外暴露 `SendRaw(HttpRequestMessage) => HttpResponseMessage`，需要时自行改 Header / Range。
  - 例：Fetcher 注册表就是一张 `Dictionary<前缀, Func<...>>`，换一条规则 = 改一个字典项，不影响其他。
  - 例：JSON 解析返回裸 `JsonElement`，调用方自己取字段（"泄露"了 JSON 结构，但你能精确控制、不会卡在封装外）。
- **不可控抽象**（本计划反对）：深度继承 / `sealed` 封装 / 接口 + 工厂，想绕过就得反射或整体重写。

> 一句话：**抽象要薄、逃生舱要明、状态要显式。**

---

## 1. 现状快照（Phase 0–5 之后）

| 维度 | 现状 |
|---|---|
| 规模 | 2 项目（`BBDown` 主、`BBDown.Core` 库），约 36 个手写 `.cs`、~6760 行 |
| 构建 | `dotnet build` **0 警告 0 错误**，`--help` 正常 |
| 全局状态 | `BBDown.Core.Config` 是**静态可变全局**（`COOKIE/TOKEN/HOST/EPHOST/TVHOST/AREA/WBI/DEBUG_LOG` + `qualitys`） |
| Fetcher | `IFetcher` 接口 + 8 实现类（`Normal/Bangumi/IntlBangumi/Cheese/Space/Fav/Media/Series`）+ `FetcherFactory.CreateFetcher` 大段 if/else；`Fav`/`Media` 内部用 `new XxxFetcher().FetchAsync` 互相耦合 |
| 巨型方法 | `Program.DownloadPageAsync`（~430 行 / 2 goto）、`Parser.ExtractTracksAsync`（~342 行 / 2 goto）、`Utils.GetAvIdAsync`（~170 行 / 18 分支） |
| 实体 | `Entity` 已是纯数据袋（符合方向）；但 `Page` 有 5 个构造器 + 手写 `Equals/GetHashCode`；`Video/Audio/Subtitle/Clip/ViewPoint/AudioMaterialInfo` 有样板 |
| 入口 | `Program.Main` 同时承载 CLI 与 server（`BBDownApiServer`）两套流程，部分逻辑重复 |
| 登录 | `Login.Web` / `Login.TV` 重复度高，差异主要在 host + 二维码流程 |

---

## 2. 目标形态（最终态，分多个 Phase 逐步逼近，不一步到位）

```
App.Run(myOption)            // CLI 入口：解析 -> 选源 -> 下载 -> 合并
App.Serve(myOption)          // server 入口：复用 Run 的核心，不复制逻辑

Fetchers.Resolve(id, cfg)    // 函数表派发，返回 VInfo，无接口无工厂
Parser.Parse(id, cfg, opts)  // 纯函数：返回 ParsedResult，无状态

Http.GetJson / SendRaw       // 可控泄露：封装常用请求，但逃生舱暴露 HttpRequestMessage
Config -> AppConfig           // 不可变快照，显式传入，消灭全局可变
Entity 全 record             // 去 Equals/GetHashCode 样板，行为在模块函数里
```

---

## 3. 分阶段计划

### Phase A — 全局状态收口（Config → AppConfig 不可变快照）
- **目标**：消灭 `Config` 静态可变全局，让"配置从哪来、到哪去"可手动追踪。
- **改动**：
  1. 新增 `readonly record struct AppConfig(string Cookie, string Token, string Host, string EpHost, string TvHost, string Area, string Wbi)`（**不可变值对象快照**，`Empty` 静态只读实例给 `Login` 等无凭证上下文用）。
  2. `SetUpWork` 中一次性从 `MyOption` 构造 `AppConfig`，经 `GetVideoInfoAsync → DownloadPagesAsync/DownloadPageAsync → FetchPointsAsync/ExtractTracksAsync/GetSubtitlesAsync/SaveSubtitleAsync/DownloadFileAsync` 全链路**显式首参传入**；`GetAvIdAsync → GetEpidBySSIdAsync/...` 同样透传；`BBDownApiServer` 镜像；`Login` 用 `AppConfig.Empty`。
  3. 删除全部 `Config.COOKIE/TOKEN/HOST/EPHOST/TVHOST/AREA/WBI` 的 `set;`，`Config` 收敛为仅剩 `DEBUG_LOG` + `qualitys` 两个**进程级只读**字段。
- **可控泄露点**：配置是显式值对象，任何函数都能直接看到它从哪来；不藏进容器。
- **不变量**：运行期行为不变；仅请求路径上的 `Config.静态字段` 读取点改为读 `cfg`。
- **验收**：`dotnet build` 0 错误 0 警告；`--help` + 真实 aid 的 `-info` dry-run + 一次真实音频下载均通过（见下方执行记录）。

#### Phase A 设计偏差（已确认）
- 计划初稿 `AppConfig` 含 `bool DebugLog` 与 `qualitys`，**实际实现未纳入**，原因如下：
  - `qualitys` 是进程级只读对照表（画质 id↔描述），与单次请求上下文无关，纳入值对象反而制造逐请求拷贝噪声；
  - `DEBUG_LOG` 是进程级环境开关（`--debug`），到处透传 `cfg.DebugLog` 属于"为透传而透传"的过度仪式，违背「避免过度 OOP / 不要无意义 ceremony」的硬约束。
  - 二者保留在 `Config` 作为**进程级 ambient**（不是"请求上下文"），请求上下文字段（`COOKIE/TOKEN/HOST/EPHOST/TVHOST/AREA/WBI`）才进 `AppConfig`。这是"可控泄露"的边界判断，写进文档以便后续 Phase 一致。
- `Wbi` **纳入** `AppConfig`：它是 `CheckLogin` 的**计算副作用**（由 cookie 推导出 mixin key），本就随请求上下文流动，用 `cfg with { Wbi = wbi }` 更新，符合"数据即派发"思路。

#### Phase A 执行记录（已落地）
- 全链路 `AppConfig cfg` 透传编译通过（0 错误 0 警告）。
- 冒烟验证期间**发现并修复一组 CLI 迁移（`a6c3aa6`，System.CommandLine 2.0.10）遗留的预存在 bug**，否则 `-info`/下载在 `LoadCredentials` 与 `SetUpWork` 早期就崩溃，无法验证 Phase A：
  1. `CommandLineInvoker` 的 `Url` 位置参数**从未绑定到 `option.Url`**（定义并加入命令，但 `SetAction` 漏写赋值）→ 所有调用 `input` 为 null。已补 `option.Url = parseResult.GetValue(Url) ?? "";`。
  2. 全部 `Option<string>` 读取用 `parseResult.GetValue(X)!`，未传参时返回 `null`（原代码默认 `""`）→ `SelectPage.ToUpper()` 等 NRE。改为 `?? ""` 恢复空串默认。
  3. `ParseEncodingPriority` 在 `if (EncodingPriority != null)` 内无条件 `encodingPriorityTemp.First()`，空串清洗后列表为空即 `NoElements` → 改为 `FirstOrDefault( ) ?? ""`。
  4. `SetUpWork` 的 `Convert.ToInt32(myOption.DelayPerPage)` 遇空串 `FormatException` → 改为 `int.TryParse(...) ? ... : 0`。
  5. `LoadCredentials` 对可能为 null 的 `Cookie`/`AccessToken` 用 `.Replace` → 加 `?? ""` / `?.Replace`。
  - 这些修复**不改变任何公开 CLI 参数名/行为**，只是让"未传参"回到空串默认（与迁移前一致），属正确性修复，不计入 Phase A 重构本体，但合入同一 commit 以便回滚一致。

### Phase B — 消除 IFetcher，改为「id → 函数」派发表
- **目标**：删掉 `IFetcher.cs` + `FetcherFactory.cs` + 8 个类的接口约束，用一张**函数表 / `switch`** 派发。
- **改动**：
  1. 每个 Fetcher 类改为 `static class XxxFetcher`（或 `static partial`），暴露 `static Task<VInfo> FetchAsync(string id)`，**去掉 `: IFetcher`**。
  2. 新增 `FetcherRegistry`（一张静态 `Dictionary<string, FetchFn>` 或 `ResolveFetcher(id, useIntlApi)` 的 `switch` 表达式），按前缀 `av/bv/ep/ss/md/space:/fav:/seriesBizId:/channel/` 等映射；未知默认 `NormalInfoFetcher`。
  3. `FavListFetcher` / `MediaListFetcher` 内部的 `new NormalInfoFetcher().FetchAsync(...)` 改为直接 `NormalInfoFetcher.FetchAsync(...)`（纯函数调用，消除 `new` 耦合）。
  4. `CreateFetcher` 的所有调用点改为 `FetcherRegistry.Resolve(...).FetchAsync(id)`。
- **可控泄露点**：注册表就是数据（字典/表达式），增删一种源 = 改一项，无虚拟分派、无工厂 if/else。
- **不变量**：每种源的返回 `VInfo` 结构与现在完全一致；`useIntlApi` 对 bangumi 的分支保留。
- **验收**：`dotnet build` 0 错误；`--help` 与各类型 id 解析冒烟（av/bv/ep/ss/space:/fav:/md）。

### Phase C — 巨型方法函数化（DownloadPageAsync / ExtractTracksAsync，去 goto）
- **目标**：把两个 ~400/340 行、含 `goto` 的怪物拆成**纯函数 / 本地函数**，消除 `goto`（downloadPage:/reParse:）。
- **改动**：
  1. `DownloadPageAsync` 拆为：`ResolvePage` → `BuildTracks` → `SelectTracks` → `ProcessDanmaku` → `DownloadAndMux` → `SaveOutputs`，均为 `static`（置于 `Program.Download.cs` 或独立 `Download` 静态类，**不新建继承体系**）。
  2. `goto downloadPage:` / `goto reParse:` 改为**显式两阶段**（先解析，必要时重解析）或带 `bool reparse` 的 `while` 循环；用早返回替代深层嵌套。
  3. `ExtractTracksAsync` 拆为 `ParseDash` / `ParseFlv` / `BuildVideoTrack` / `BuildAudioTrack`（Phase 3 已提取 `BuildUrlList`/`PickBaseUrl`，复用之）；消除残留重复。
- **可控泄露点**：拆分出的函数输入输出明确，测试/调试可直接调用单步；不引入"下载器基类"之类的封装。
- **不变量**：输出文件、命名、合并行为与现在逐字节一致（重点回归：DASH / FLV / 杜比 / Hi-Res / 互动视频分支）。
- **验收**：`dotnet build` 0 错误；真实视频下载冒烟（含一次重解析路径，验证 goto 改写等价）。

### Phase D — 入口 id 解析派发（GetAvIdAsync 18 分支 → 函数表 / 模式匹配）
- **目标**：`Utils.GetAvIdAsync` 的 18 分支 if 链改为**前缀派发 + 每情形一个小纯函数**。
- **改动**：
  1. 识别所有前缀/形态：`av/bv/BV/ep/ss/md/https?://.../space:/fav:/channel/.../纯数字`，每种写 `static Task<ResolvedId> ResolveXxx(string raw)`。
  2. 用 `switch` 表达式或字典派发；未知形态给出明确错误而非静默。
  3. 与 Phase B 的 `FetcherRegistry` 共用同一套"前缀识别"逻辑（抽出 `ParseInputId(raw) => (kind, id)` 单一真源）。
- **可控泄露点**：每种 id 形态是一个独立可测纯函数；派发表是数据，不是继承。
- **不变量**：解析结果与现在一致（重点回归：BV↔av 互转、ep/ss 跳集、URL 直链）。
- **验收**：`dotnet build` 0 错误；各形态 id 单测 + 冒烟。

### Phase E — HTTP 层收敛为可控泄露抽象
- **目标**：`HTTPUtil` 已集中 `HttpClient`，但 Cookie/Token/Host/Referer 注入散落各处；收敛为薄封装 + 明确逃生舱。
- **改动**：
  1. 提供 `GetJsonAsync(url, cfg) => JsonElement`（返回裸 JSON，调用方自己取字段——"泄露" JSON 结构但可控）。
  2. 提供 `SendRawAsync(HttpRequestMessage) => HttpResponseMessage`（逃生舱：需要时自行改 Header / Range / 平台分支）。
  3. 提供 `GetWithRangeAsync(url, from, to, cfg)` 给下载用；Cookie/Host/Token 从 `AppConfig` 注入，而非读 `Config` 静态。
  4. **不**引入 `IHttpService` 接口；用静态模块 + 可选委托参数即可。
- **可控泄露点**：封装常用请求，但 `SendRawAsync` 暴露 `HttpRequestMessage`，遇到奇葩接口能直接 `new HttpRequestMessage` 而不必绕过封装。
- **不变量**：请求头 / Cookie / UA 行为与现在一致（保留平台分支 `platform=android_tv_yst` / `android` 的免 Referer 逻辑）。
- **验收**：`dotnet build` 0 错误；下载 + 字幕 + 重定向（GetWebLocationAsync）冒烟。

### Phase F — 实体 record 化与去样板
- **目标**：消灭手写 `Equals`/`GetHashCode` 与 `Page` 5 构造器样板；保持纯数据。
- **改动**：
  1. `Video` / `Audio` / `Subtitle` / `Clip` / `ViewPoint` / `AudioMaterialInfo` 改为 `record`（自动值相等，删除样板）。`Audio.shortCodecs` 计算属性保留。
  2. `Page`：保留为 `class`（含 `bvid` 计算属性与"按 aid/cid/epid 相等"语义）；将 5 个构造器**收敛为 `required` 字段 + 对象初始化器**，调用点 `new Page(idx, aid, ...)` 改为 `new Page { index=..., aid=..., ... }`（调用点多，单独小步提交）。
  3. 不在实体里加方法（行为留在 `Parser` / `SortTracks` 等模块函数）。
- **可控泄露点**：`record` 的相等语义是显式、可预期的值相等，不藏比较逻辑。
- **不变量**：相等语义与现在一致（`Page` 仍按 aid/cid/epid 判等）。
- **验收**：`dotnet build` 0 错误；下载排序 / 去重冒烟。

### Phase G — 入口收敛（Program 拆 partial；server 复用 core；Login 合并）
- **目标**：`Program.Main` 巨型文件拆分；CLI 与 server 复用同一核心；`Login.Web/TV` 合并。
- **改动**：
  1. `Program.cs` 用 `partial class Program` 拆为：`Program.cs`（Main / 参数装配）、`Program.Download.cs`（DownloadPagesAsync / DownloadPageAsync 拆分后的函数）、`Program.Commands.cs`（login 等子命令）。
  2. `BBDownApiServer` 的 add-task 流程改为调用与 CLI 同一的 `Download.Run(...)` 核心，删除重复的选项处理。
  3. `Login.Web` / `Login.TV` 合并为 `Login.LoginAsync(MyOption, LoginMode)`，差异（host、二维码流程）用参数/小函数表达，**不建 Login 基类**。
- **可控泄露点**：拆分是 `partial`（同一类型多文件），不新建"入口层"类型；server 直接复用函数而非复制。
- **不变量**：CLI 与 server 行为不变；`--login` / `--logintv` 仍可用。
- **验收**：`dotnet build` 0 错误；CLI 下载 + server `add-task` 冒烟。

### Phase H — 命名一致性 + 残留注释清扫
- **目标**：统一命名与拼写，清掉遗留注释。
- **改动**：
  1. 内部字段 `bandwith` → `bandwidth`（**保留 `--bandwith-ascending` CLI 别名不变**，仅内部 rename，评估调用点后小步提交）。
  2. 拼写修正（`recevied`/`ponints` 已在 Phase 4 修，复盘有无遗漏）。
  3. 删除残留调试注释（与 Phase 2 同类，扫一遍 `//` 单行注释）。
- **不变量**：CLI 参数名完全不变；仅内部标识符改名。
- **验收**：`dotnet build` 0 错误；`--help` 选项名与现在一致。

### Phase I — 安全网：为「手动可控的纯函数」加轻量单测
- **目标**：在动手激进重构前/中，给"可控泄露的纯函数"补少量单测作回归网。
- **改动**：
  1. 新增 `BBDown.Core.Tests`（xUnit 或项目内 `[Fact]`），仅覆盖纯函数：`GetValidFileName`、`ParseEncodingPriority`、`ParseInputId`（Phase D）、`PickBaseUrl`、`BuildUrlList`、`BilibiliBvConverter` 往返、关键正则、`SortTracks`。
  2. **不追求全量覆盖**；只为"手动可控、无副作用"的纯函数加，作为后续 Phase 的护栏。
- **可控泄露点**：测试直接调用纯函数，不依赖容器/ mock。
- **验收**：`dotnet test` 全绿。

### Phase J — 收尾：.editorconfig 增强 + 最终验证
- **目标**：固化风格，最终交付。
- **改动**：
  1. `.editorconfig` 增补：`csharp_style_namespace_declarations = file_scoped:suggestion`；将 `CA` 系列常见噪音设为 `none`（与现有 `CA1862/RCS1155` 一致）；确认 `CS860x/SYSLIB0014` 维持 `warning`。
  2. 全量 `dotnet build` 0 警告 0 错误；`dotnet test` 全绿；真实视频下载 + server add-task 端到端冒烟。
  3. 打一个重构完成 tag / 汇总 commit。
- **验收**：构建/测试/冒烟全通过；提交历史每个 Phase 独立、可逐条回滚。

---

## 4. 反模式 ↔ 替代方案对照表

| 现状反模式 | 本计划替代 | 为什么更"可控泄露" |
|---|---|---|
| `IFetcher` 接口 + 8 类 + 工厂 if/else | `Dictionary<前缀, Func<...>>` / `switch` 派发 | 注册表是数据，增删一种源 = 改一项 |
| `Config` 静态可变全局 | `AppConfig` 只读快照，显式传参 | 配置流向可见、可追踪 |
| `Page` 5 构造器 + 手写相等 | `required` + 对象初始化器 / `record` | 相等语义显式、无隐藏逻辑 |
| `goto downloadPage/reParse` | 两阶段 / `while` + 早返回 | 控制流线性、可单步、可测 |
| `GetAvIdAsync` 18 分支 if | 前缀派发 + 每情形纯函数 | 每情形独立可测，派发表是数据 |
| `new XxxFetcher().FetchAsync` 跨类耦合 | 直接 `XxxFetcher.FetchAsync` 静态调用 | 无 `new`、无生命周期管理 |
| `Login.Web` / `Login.TV` 复制 | `LoginAsync(mode, ...)` 参数化 | 差异显式，不建基类 |
| `IHttpService` 封装 | `Http` 静态模块 + `SendRaw` 逃生舱 | 需要时拿 `HttpRequestMessage` 自行改 |

---

## 5. 执行纪律与回滚

- **每 Phase 独立 commit**：`refactor(phase-X): ...`，便于 `git revert` 单步回滚。
- **每个 Phase 结束**：`dotnet build` 必须 **0 错误**（保持 Phase 5 的零警告基线）；至少跑 `--help` 与一个真实 aid 的下载/解析冒烟。
- **goto / 重解析等高风险点**（Phase C）：先用 Phase I 的纯函数单测覆盖相关解析路径，再改写，确保行为等价。
- **CLI 兼容铁律**：`--bandwith-ascending` 等所有公开参数名**绝对不变**；仅内部标识符可 rename。
- **不过度**：Phase G/H/J 若某步风险收益比不佳，可暂缓并在文档标注"待定"，不强行推进。

## 6. 验收清单（全部 Phase 完成后）

- [ ] `dotnet build`：0 警告 0 错误
- [ ] `dotnet test`：全绿（Phase I 纯函数网）
- [ ] `--help` 输出与重构前一致（参数名/别名不变）
- [ ] 真实视频下载端到端通过（DASH / FLV / 杜比 / Hi-Res / 互动视频 / 番剧 / 收藏 / 空间 / 课程各一例）
- [ ] server 模式 `add-task` 端到端通过
- [ ] `git log` 显示 Phase A–J 各自独立 commit，可逐条回滚
- [ ] 无 `IFetcher` / `FetcherFactory` / `Config` 静态可变 / `goto` 残留
