# BUS.md — 消息 / 进度 / 交互总线

> 三大总线统一形态：静态门面 + 订阅 / 发布 + Scope 复用 + 判别联合事件。Core 只产生事件，展示与应答由宿主决定。
> 当前实现状态，日期：2026-09-05。WebSocket 帧协议与 serve REST 契约见 `docs/API.md`，宿主结构见 `docs/ARCHITECTURE.md`。

---

## 0. 总览

| 总线 | 事件形态 | 频次 | 状态 |
| ---- | ---- | ---- | ---- |
| `MessageBus`（消息） | `LogMessage`（Level / Text / Time / Scope） | 低频语义 | 已实施 |
| `ProgressBus`（进度） | 阶段边界 `ProgressRangeStart / End` + 阶段内快照 `ProgressSample` | 低频边界 + 高频样本（采样 125ms；serve 快照帧 200ms；CLI 渲染 125ms） | 已实施 |
| `AskBus`（交互） | 请求 `OptionRequestEvent` → 应答（结构化 `AskAnswer`） | 低频请求-应答 | 已实施 |

共性：

- **静态门面**：`Subscribe / Unsubscribe` 委托组合（避免 CA1003 的 EventArgs 约束），`Publish` 无渲染无锁，同步回调订阅者（订阅端须短快）。
- **Scope**：唯一来源是 `MessageBus.BeginScope`（AsyncLocal 栈式恢复）。CLI 无 scope（单任务）；GUI 以任务序号为 scope（`BeginScope(task.Index)`）；serve 以任务 id（`DownloadTask.Scope`，ResourceId 规范串）为 scope，事件自动携带。
- **传输模型 == 帧协议**：事件复用 `WorkflowEvent` 判别联合（`[JsonPolymorphic]`，判别符 `type`），serve WebSocket 帧零转换。
- **阶段外 / 无宿主防护**：`ProgressBus.Publish` 阶段外静默忽略；`AskBus.Ask` 无订阅者立即回落（不等超时）。

---

## 1. MessageBus（消息）

### 1.1 事件模型

`BBDown.Core/Logging/LogMessage.cs`：`LogMessage(Level, Text, Time, Emphasized, Scope, Enter, ShowTime)`——Level 语义化（Debug / Info / Warn / Error），渲染器决定颜色。

### 1.2 总线

`BBDown.Core/Logging/MessageBus.cs`：`Subscribe / Unsubscribe / Publish / BeginScope / CurrentScope`。`BeginScope` 返回 `ScopeLease`，Dispose 恢复旧 scope（可嵌套）；消息经 AsyncLocal 自动携带当前 scope。`Publish` 对单个订阅者的异常隔离（渲染故障不中断下载链路）。

### 1.3 发射端

`Logger` 纯发射门面：`Log / LogWarn / LogError / LogDebug / LogColor` → `MessageBus.Publish`。Debug 级由 `Config.DebugLog` 门控，不产生消息。

### 1.4 消费端

| 宿主 | 订阅位置 | 展示 |
| ---- | ---- | ---- |
| CLI | `BBDown/Cli/ConsoleMessageRenderer.cs` | 控制台渲染（颜色 / 时间戳 / 写前调 `ConsoleHost.BeforeWrite` 擦活动状态行） |
| GUI | `BBDown.GUI/MainWindow.axaml.cs` `OnLogMessage` | 窗口日志区，`[任务{scope}]` 加前缀，Error 标红，回投 UI 线程，上限 5000 行 |
| serve | `BBDown/Serve/Http/TaskMessageBridge.cs` `OnMessage` | 无 Scope 忽略；按 Scope 命中任务事件上下文 → 队列 → WebSocket `event` 帧（`type: message`） |

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

- `BeginStage` 返回 `IDisposable` 阶段句柄，Dispose 即结束阶段；同任务重入先结束旧阶段。
- `Publish` 传**字节增量**，阶段内累计由总线按 scope 维护（`Interlocked`，同一任务多下载器并发互不覆盖、不回退）；阶段外静默忽略。
- `Latest(scope)` 快照式消费（serve 周期帧用），阶段结束后条目移除。

### 2.3 采样

`BBDown.Core/Util/ProgressSampler.cs`：把下载线程高频的 `Report` 降频为每 **125ms** 一次 `(ratio, delta)` 回吐（每秒 8 次，与 CLI 渲染帧率一致）。速度类消费方按此周期把 delta 折算成每秒速率。

### 2.4 发射端（链路接入点）

| 位置 | 阶段 / 动作 |
| ---- | ---- |
| `Download/DownloaderAdapter.cs` | 采样回调 → `ProgressBus.Publish(ratio, delta, delta / SampleInterval)`（阶段外静默忽略，封面 / 弹幕等附属下载不发射） |
| `Media/FlvDownload.cs` / `Media/DashDownload.cs` | `BeginStage("下载")` 包住主媒体下载段 |
| `Pipeline/AudioDownload.cs` | `BeginStage("下载音频")` |
| `Pipeline/OpusDownload.cs` | `BeginStage("下载图片")`：图片有明确总量，按张数上报 ratio，detail 为「图片 i/n」 |
| `Pipeline/LiveDownload.cs` | `BeginStage("录制")`：直播无总量，Ratio 恒 0，detail 承载时长 / 分段 / 清晰度，累计字节经 `ProgressSampler.Report` 上报 |

### 2.5 消费端

| 宿主 | 订阅位置 | 展示 |
| ---- | ---- | ---- |
| CLI | `BBDown/ProgressBar.cs` | 1/8 秒（125ms）控制台进度条（ratio / speed / ETA / spinner），阶段开始显示 / 结束清行隐藏，超过 1 秒无采样兜底清行，交互读输入前暂停渲染（`CliInteraction.BeforeRead / AfterRead`） |
| CLI（直播） | `BBDown/Cli/LiveProgress.cs` | 0.5 秒单行状态（detail + 体积 + 速度），重定向到文件时改 60 秒落一行日志 |
| GUI | `BBDown.GUI/MainWindow.Progress.cs` `OnProgress` | 阶段开始 → 重置该任务 ETA 基准；样本 → 任务行进度 / 速度 / ETA（detail 优先展示）；阶段结束 → 无动作，任务收尾随状态隐藏 |
| serve | `TaskMessageBridge`（阶段边界 → 事件帧）+ `TaskWorker.OnProgress`（样本 → `DownloadTask` 的 REST 字段 `Progress` / `DownloadSpeed` / `TotalDownloadedBytes`）+ `TaskSocketHub.ForwardSnapshots`（200ms 轮询 `Latest(scope)`，样本引用变化才推 `snapshot` 帧） | WebSocket `event` / `snapshot` 帧 + `/api/v1/tasks` 契约 |

---

## 3. AskBus（交互）

### 3.1 结构化模型

`BBDown.Core/Workflow/AskOption.cs`：

```csharp
/// <summary>可选项：Id 为稳定标识（CLI 输入映射 / serve 帧传输 / GUI 弹窗选择共用），Label 为展示文本。</summary>
public sealed record AskOption(string Id, string Label);

/// <summary>应答结果：OptionId 必须属于请求选项集合；RawInput 为宿主收到的原始输入（CLI 别名映射用，serve / GUI 为 null）。</summary>
public sealed record AskAnswer(string OptionId, string? RawInput = null);
```

### 3.2 总线 API

`BBDown.Core/Workflow/AskBus.cs`：

```csharp
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

内部：`ConcurrentDictionary<Guid, PendingAsk>`（Scope / Options / TCS）；`Answer` 校验 `OptionId ∈ Options`（`Ordinal`）；超时 5 分钟回落 null；无订阅者立即回落（不等超时）。

### 3.3 事件

`OptionRequestEvent`（`WorkflowEvent` 判别联合成员，判别符 `optionRequest`）：

```csharp
OptionRequestEvent(Guid RequestId, string Scope, string Prompt,
    IReadOnlyList<AskOption> Options, DateTimeOffset Deadline, string? DefaultOptionId = null)
```

`Scope` 取自 `MessageBus.CurrentScope`（同 `ProgressRangeStartEvent`）；`Options` 为结构化 `AskOption`；`DefaultOptionId` 为宿主输入回落选项。WebSocket 帧协议：`options` 为 `{ id, label }` 对象数组，`submitChoice` 的 `choice` 即选项 Id（详见 `docs/API.md`）。

### 3.4 调用点

| 调用点 | 行为 |
| ---- | ---- |
| `Pipeline/PageSelect.cs` `ResolveInteractiveAsync` | 对每个分 P 提问 y / n / a / q，回车（default= n）跳过，按 `OptionId` 分支累计选中集 |
| `Media/TrackSelect.cs` `PickTracksAsync` / `PickDfnAsync` | 选项 Id 即序号字符串，`PickIndexAsync` 应答回落 0（同现状非法输入回落 0） |

CLI 输入规范化映射（属 CLI 消费端职责）：`Trim` + 大小写归一 + 常见全拼缩写（YES→Y、ALL→A、QUIT→Q、NO→N）后匹配 `OptionId`，匹配失败回落（默认选项或首选项）。

### 3.5 消费端

| 宿主 | 订阅位置 | 行为 |
| ---- | ---- | ---- |
| CLI | `BBDown/Cli/CliInteraction.cs`（CLI 运行入口最早装配，先于一切下载链路） | 收到请求 → `BeforeRead`（暂停进度条渲染）→ 打印提示 → `Console.ReadLine` → 规范化映射 → `AskBus.Answer` → `AfterRead` |
| GUI | `BBDown.GUI/MainWindow.Ask.cs` `OnAsk` | 回投 UI 线程弹 `AskDialog`，选择后应答；窗口关闭（未选）回落默认选项；关窗时对全部任务序号 `AskBus.CancelPending` |
| serve | `TaskMessageBridge.OnAsk` → 按 Scope 入任务事件队列 → WebSocket `optionRequest` 帧；`TaskSocketHub.SubmitChoiceAsync` → 校验任务存在后 `AskBus.Answer`（`choice` 必须 ∈ 选项集合，帧协议既有安全限制）→ 回执 `choiceResult` 帧 | 远程枚举应答 |

自适应回落：无订阅者时 `Ask` 立即返回 null，调用点按「不交互」处理（逐集全跳过、选轨落回默认序号）。

### 3.6 连带收窄（已完成）

- `ChannelWorkflowContext` 收窄为「可靠事件队列」：`EnqueueMessage / EnqueueEvent / ReadAllAsync`（有界 1024，写满降级丢弃，不阻塞下载链路）；`AskOptionAsync / SubmitChoice / CancelPendingChoices` 已删，TCS 机制整体上移 AskBus。
- `IWorkflowContext` 接口已删（单实现接口），`DownloadPipeline.RunAsync / ComposeSink / WorkerDispatcher.RunAsync / SpaceDynamicDownload.RunAsync` 参数类型为 `ChannelWorkflowContext?`，null 表示 CLI 路径。
- `Interaction` 静态类已退役：`AskLine / AskIndex` 删除，`BeforeRead / AfterRead` 能力移入 `CliInteraction` 静态属性（供 ProgressBar / LiveProgress 注册钩子）。
- `TaskStore.ReleaseContext` 删除 `CancelPendingChoices` 调用 → `AskBus.CancelPending(scope)`。

---

## 4. 宿主接入矩阵

| 总线 | 事件 | CLI | GUI | serve |
| ---- | ---- | ---- | ---- | ---- |
| 消息 | `message` | ConsoleMessageRenderer | 日志区（[任务 N] 前缀） | TaskMessageBridge → 事件帧 |
| 进度 | `progressStart / progressSample / progressEnd` + 快照 | ProgressBar（视频 / 图片 / 音频）+ LiveProgress（直播） | 任务行进度条（含阶段边界，ETA 重置） | TaskMessageBridge + TaskWorker + TaskSocketHub |
| 交互 | `optionRequest` → `submitChoice` | CliInteraction（控制台读输入） | AskDialog（弹窗，默认回落） | TaskMessageBridge + TaskSocketHub 应答 |

---

## 5. 与旧通道的关系（迁移已完成）

| 旧通道 | 去向 |
| ---- | ---- |
| `Logger` Console 渲染 | 迁 `ConsoleMessageRenderer`（CLI 装配） |
| `WorkflowContextHost` | 删除（MessageBus 接管） |
| `Interaction` 静态类 | 退役（AskBus + CliInteraction 接管） |
| `ChannelWorkflowContext.AskOptionAsync` | 上移 AskBus（TCS 机制） |
| `IProgressScope` / `ProgressStage.Report` | 删除（`BeginStage` 返回的 `IDisposable` 接管） |
| `IWorkflowContext` 接口 | 删除（参数类型换为具体类 `ChannelWorkflowContext?`） |