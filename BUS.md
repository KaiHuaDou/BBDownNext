# BUS.md — 消息 / 进度 / 交互总线设计

> 设计文档（分析交付物，未提交）。日期：2026-08-20。
> 三大总线统一形态：静态门面 + 订阅 / 发布 + Scope 复用 + 判别联合事件。Core 只产生事件，展示与应答由宿主决定。
> 相关文档：`docs/message-bus-design.md`（消息总线设计）、`docs/progress-bus-design.md`（进度总线设计）、`docs/gui-progress-plan.md`（三接入点核查与交互总线化计划）。

---

## 0. 总览

| 总线 | 事件形态 | 频次 | 状态 |
| ---- | ---- | ---- | ---- |
| `MessageBus`（消息） | `LogMessage`（Level / Text / Time / Scope） | 低频语义 | 已实施 |
| `ProgressBus`（进度） | 阶段边界 `ProgressRangeStart / End` + 阶段内快照 `ProgressSample` | 低频边界 + 高频快照（200ms） | 已实施 |
| `AskBus`（交互） | 请求 `OptionRequestEvent` → 应答（结构化 `AskAnswer`） | 低频请求-应答 | 设计（计划 P3） |

共性：

- **静态门面**：`Subscribe / Unsubscribe` 委托组合（避免 CA1003 的 EventArgs 约束），`Publish` 无渲染无锁，同步回调订阅者（订阅端须短快）。
- **Scope**：唯一来源是 `MessageBus.BeginScope`（AsyncLocal 栈式恢复）。CLI 无 scope（单任务）；GUI / serve 以任务 id 为 scope，事件自动携带。
- **传输模型 == 帧协议**：事件复用 `WorkflowEvent` 判别联合（`[JsonPolymorphic]`，判别符 `type`），serve WebSocket 帧零转换。
- **阶段外 / 无宿主防护**：`ProgressBus.Publish` 阶段外静默忽略；`AskBus.Ask` 无订阅者立即回落（不等超时）。

---

## 1. MessageBus（消息）

### 1.1 事件模型

`BBDown.Core/Logging/LogMessage.cs`：`LogMessage(Level, Text, Time, Emphasized, Scope, Enter, ShowTime)`——Level 语义化（Debug / Info / Warn / Error），渲染器决定颜色。

### 1.2 总线

`BBDown.Core/Logging/MessageBus.cs`：`Subscribe / Unsubscribe / Publish / BeginScope`。`BeginScope` 返回 `ScopeLease`，Dispose 恢复旧 scope（可嵌套）。

### 1.3 发射端

`Logger` 纯发射门面：`Log / LogWarn / LogError / LogDebug / LogColor` → `MessageBus.Publish`。Debug 级由 `Config.DebugLog` 门控，不产生消息。

### 1.4 消费端

| 宿主 | 订阅位置 | 展示 |
| ---- | ---- | ---- |
| CLI | `BBDown/Cli/ConsoleMessageRenderer.cs` | 控制台渲染（颜色 / 时间戳 / `ConsoleHost.BeforeWrite` 擦行协作） |
| GUI | `BBDown.GUI/MainWindow.axaml.cs` `OnLogMessage` | 窗口日志区，`Scope`（任务序号）加前缀，Error 标红 |
| serve | `BBDown/Serve/Http/TaskMessageBridge.cs` `OnMessage` | 按 `Scope` 路由进任务上下文事件队列 → WebSocket `message` 帧 |

---

## 2. ProgressBus（进度）

### 2.1 事件模型（阶段化）

`BBDown.Core/Workflow/WorkflowEvent.cs`：

| 事件 | 字段 | 语义 |
| ---- | ---- | ---- |
| `ProgressRangeStartEvent` | `Scope, StageName` | 低频：阶段开始，宿主据此显示进度 UI |
| `ProgressSampleEvent` | `Scope, Ratio, TotalBytes, Speed, Detail` | 高频快照：阶段内样本，宿主可丢帧只渲染最新 |
| `ProgressRangeEndEvent` | `Scope` | 低频：阶段结束，宿主据此隐藏 / 收尾进度 UI |

### 2.2 总线

`BBDown.Core/Workflow/ProgressBus.cs`：`Subscribe / Unsubscribe / BeginStage(stageName) / Publish(ratio, bytesDelta, speed, detail) / Latest(scope)`。

- `BeginStage` 返回阶段句柄，Dispose 即结束阶段；同任务重入先结束旧阶段。
- `Publish` 传**字节增量**，阶段内累计由总线按 scope 维护（`Interlocked`，多下载器并发互不覆盖、不回退）。
- `Latest(scope)` 快照式消费（serve 周期帧用），阶段结束后条目移除。
- **方案 A（已实施）**：`BeginStage` 返回 `IDisposable`，`IProgressScope` 与 `ProgressStage.Report` 已删除（死 API + pass-through）。

### 2.3 发射端（链路接入点）

| 位置 | 动作 |
| ---- | ---- |
| `Download/DownloaderAdapter.cs` | 采样回调 → `ProgressBus.Publish(ratio, delta, delta / SampleInterval)` |
| `Media/FlvDownload.cs` / `Media/DashDownload.cs` | `using var stage = ProgressBus.BeginStage("下载")` 包住主媒体下载段 |

直播（`LiveProgress`）与专栏（`OpusDownload`）不发射进度（延后项，见计划 G2 / G3）。

### 2.4 消费端

| 宿主 | 订阅位置 | 展示 |
| ---- | ---- | ---- |
| CLI | `BBDown/ProgressBar.cs` | 1/8 秒控制台进度条（ratio / speed / ETA / spinner），`Start` 显示 / `End` 隐藏，交互暂停保留，1 秒空闲兜底 |
| GUI | `BBDown.GUI/MainWindow.axaml.cs` `OnProgress` | 任务行 `ProgressBar`（`TaskState.Progress` 绑定）；样本 → 进度 / 速度 / ETA。**缺口 G1**：未消费阶段边界 |
| serve | `TaskMessageBridge`（Start / End → 事件帧）+ `TaskWorker`（样本 → `DownloadTask` REST 字段）+ `TaskSocketHub`（200ms 读 `Latest` → 快照帧） | WebSocket `event` / `snapshot` 帧 + `/api/v1/tasks` 契约 |

---

## 3. AskBus（交互，设计）

### 3.1 动机与现状

- 交互点（逐集确认 `PageSelect`、选轨 `TrackSelect`）现走静态 `Interaction.AskLine / AskIndex`（`BBDown.Core/Interaction.cs`，直连 `Console.ReadLine`）——消息 / 进度总线化后唯一残留的控制台直连通道。
- serve 的 `ChannelWorkflowContext.AskOptionAsync`（TCS 挂起 + `OptionRequestEvent` + `SubmitChoice`）已具备形状但**生产调用方 = 0**，`optionRequest` / `submitChoice` 帧链路不可达。

### 3.2 结构化模型

```csharp
// BBDown.Core/Workflow/AskOption.cs（新增）
/// <summary>可选项：Id 为稳定标识（CLI 输入映射 / serve 帧传输 / GUI 弹窗选择共用），Label 为展示文本。</summary>
public sealed record AskOption(string Id, string Label);

/// <summary>应答结果：OptionId 必须属于请求选项集合；RawInput 为宿主收到的原始输入（CLI 别名映射用，serve / GUI 为 null）。</summary>
public sealed record AskAnswer(string OptionId, string? RawInput = null);
```

### 3.3 总线 API

```csharp
// BBDown.Core/Workflow/AskBus.cs（已实施）
public static class AskBus
{
    public static void Subscribe(Action<OptionRequestEvent> handler);
    public static void Unsubscribe(Action<OptionRequestEvent> handler);

    /// <summary>
    /// 提问并挂起直到应答 / 超时 / 取消。返回 null 表示宿主不支持交互（无订阅者立即回落，同现状 ReadLine null）。
    /// defaultOptionId 为宿主无法解析输入时的回落选项（CLI 回车 / 非法输入），须属于 options。
    /// </summary>
    public static Task<AskAnswer?> Ask(string prompt, IReadOnlyList<AskOption> options, string? defaultOptionId = null, CancellationToken token = default);

    /// <summary>应答选项请求：校验 OptionId 属于请求选项集合；返回 false 表示请求不存在 / 已应答 / 选项非法。</summary>
    public static bool Answer(Guid requestId, AskAnswer answer);

    /// <summary>按 scope 取消全部挂起提问（任务停止 / 关服时调用），挂起的下载链路随之退出。</summary>
    public static void CancelPending(string scope);
}
```

内部：`ConcurrentDictionary<Guid, PendingAsk>`（Scope / Options / TCS）；`Answer` 校验 `OptionId ∈ Options`（`Ordinal`）；`Deadline` 超时回落 null；无订阅者立即回落（不等超时）。

### 3.4 事件演进

`OptionRequestEvent`（`WorkflowEvent` 判别联合成员，判别符 `optionRequest`）：

- 现状：`OptionRequestEvent(Guid RequestId, string Prompt, IReadOnlyList<string> Options, DateTimeOffset Deadline)`
- 演进（已实施）：`OptionRequestEvent(Guid RequestId, string Scope, string Prompt, IReadOnlyList<AskOption> Options, DateTimeOffset Deadline, string? DefaultOptionId = null)`——加 `Scope`（同 `ProgressRangeStartEvent`，取自 `MessageBus.CurrentScope`），`Options` 结构化（`AskOption`），`DefaultOptionId` 为宿主输入回落选项。
- WebSocket 帧协议：`options` 由字符串数组 → `{ id, label }` 对象数组；`submitChoice` 的 `choice` 即选项 Id（语义不变，API.md 同步）。

### 3.5 调用点改造

| 调用点 | 现状 | 改造 |
| ---- | ---- | ---- |
| `PageSelect.ResolveInteractive`（`PageSelect.cs:120`，同步） | `Interaction.AskLine(...)` 自由文本 y/n/a/q | 转 async → `AskBus.Ask(prompt, [y, n, a, q] 选项)`；按 `OptionId` 分支（y / a / q / n）；调用点 `PageQueue.cs:31` 加 await |
| `TrackSelect.PickTracks` / `PickDfn` | `Interaction.AskIndex(...)` 输入序号 | → `AskBus.Ask(prompt, [0..count) 序号选项)`；`OptionId` 解析为 int；`int.TryParse` 逻辑保留在调用点 |

CLI 输入规范化映射（实施细节，属 CLI 消费端职责）：`Trim` + 大小写归一 + 常见全拼缩写（YES→Y、ALL→A、QUIT→Q、NO→N）后匹配 `OptionId`，匹配失败回落（重问或默认，与现状行为一致）。

### 3.6 消费端

| 宿主 | 订阅位置 | 行为 |
| ---- | ---- | ---- |
| CLI | `BBDown/Cli/CliInteraction.cs`（新增，与 `ProgressBar` 同点装配） | 收到 `OptionRequestEvent` → 原 `Interaction.BeforeRead`（暂停进度条渲染）→ 打印提示 → `Console.ReadLine` → 规范化映射 → `AskBus.Answer` → `AfterRead` |
| serve | `TaskMessageBridge` 扩展订阅 → 按 `Scope` 路由 `ctx.EnqueueEvent` → WebSocket `optionRequest` 帧；`TaskSocketHub.SubmitChoiceAsync` 校验任务存在后 → `AskBus.Answer(requestId, new AskAnswer(choice))` | 远程枚举应答（`choice` 必须 ∈ 选项集合，帧协议既有安全限制） |
| GUI | 本轮不订阅（决策点 D5） | 无订阅者 → `Ask` 立即回落 null，行为与现状（`ReadLine` null 回落）一致；弹窗交互另列专项 |

### 3.7 连带收窄（D4 已确认）

- `ChannelWorkflowContext`：删 `AskOptionAsync / SubmitChoice / CancelPendingChoices`（TCS 机制上移 AskBus），收窄为「可靠事件队列」（`EnqueueEvent / EnqueueMessage / ReadAllAsync`）。
- `TaskStore.ReleaseContext`：删 `CancelPendingChoices` 调用 → `AskBus.CancelPending(scope)`。
- `IWorkflowContext` 收窄为具体类：`DownloadPipeline.RunAsync / ComposeSink` 参数类型 `IWorkflowContext?` → `ChannelWorkflowContext?`，删 `IWorkflowContext.cs`（交互总线化后接口只剩 `EnqueueMessage` 单成员，单实现接口违反约定）。
- `Interaction` 类退役：删 `AskLine / AskIndex / BeforeRead / AfterRead`（`BeforeRead / AfterRead` 能力移入 `CliInteraction`）。

---

## 4. 宿主接入矩阵

| 总线 | 事件 | CLI | GUI | serve |
| ---- | ---- | ---- | ---- | ---- |
| 消息 | `message` | ConsoleMessageRenderer | 日志区（任务序号前缀） | TaskMessageBridge → 事件帧 |
| 进度 | `progressStart / progressSample / progressEnd` + 快照 | ProgressBar（显示 / 更新 / 隐藏） | 任务行进度条（补 G1 后含阶段边界） | TaskMessageBridge + TaskWorker + TaskSocketHub |
| 交互 | `optionRequest` → `submitChoice` | CliInteraction（控制台读输入） | 不订阅（回落，D5 延后） | TaskMessageBridge + TaskSocketHub 应答 |

---

## 5. 与旧通道的关系

| 旧通道 | 去向 |
| ---- | ---- |
| `Logger` Console 渲染 | 迁 `ConsoleMessageRenderer`（CLI 装配） |
| `WorkflowContextHost` | 删除（MessageBus 接管） |
| `Interaction` 静态类 | 退役（AskBus + CliInteraction 接管） |
| `ChannelWorkflowContext.AskOptionAsync` | 上移 AskBus（TCS 机制） |
| `IProgressScope` | 删除（方案 A，`IDisposable` 接管） |
