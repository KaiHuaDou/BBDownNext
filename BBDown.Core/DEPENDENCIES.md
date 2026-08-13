# BBDown.Core 依赖架构

本文档描述 `BBDown.Core` 内部命名空间的依赖关系与约束，供二次开发与重构时参考。依赖方向以源码中的 `using` 为准。

## 命名空间依赖图

```mermaid
flowchart TD
    subgraph L1["编排层"]
        Pipeline["Pipeline"]
    end

    subgraph L2["能力层"]
        Media["Media"]
        Live["Live"]
        Auth["Auth"]
        Mux["Mux"]
        Fetcher["Fetcher"]
        Opus["Opus"]
        Comment["Comment"]
    end

    subgraph L3["下载模型与传输"]
        Download["Download"]
    end

    subgraph L4["基础设施底座"]
        Core["Core"]
        Entity["Entity"]
        Util["Util"]
        PlayUrl["PlayUrl"]
    end

    Pipeline --> Media
    Pipeline --> Live
    Pipeline --> Auth
    Pipeline --> Fetcher
    Pipeline --> Opus
    Pipeline --> Download

    Media --> Comment
    Media --> Download
    Media --> Mux

    Live --> Download
    Live --> Mux
    Mux --> Download

    Core -. 历史互依 .-> Util
    Util -. 历史互依 .-> Core
    Entity -. 历史互依 .-> Core
    Entity -. 历史互依 .-> Util
    PlayUrl -. 历史互依 .-> Core
```

## 分层职责

| 层 | 命名空间 | 职责 |
| --- | --- | --- |
| 编排层 | `BBDown.Core.Pipeline` | 下载主干编排：参数准备（WorkSetup）、信息解析（VideoInfo）、分 P 调度（PageQueue）、输入解析（InputResolver） |
| 能力层 | `BBDown.Core.Media` | 分 P 下载流程：DASH / FLV / 页面资源 / 选轨 / 评论下载 |
| 能力层 | `BBDown.Core.Live` | 直播录制：录流、分段、混流、信号控制 |
| 能力层 | `BBDown.Core.Mux` | 混流：FFmpeg / MP4Box 参数构造与执行 |
| 能力层 | `BBDown.Core.Auth` | 登录与凭据存取 |
| 能力层 | `BBDown.Core.Fetcher` / `Opus` / `Comment` | 各类信息获取与专栏、评论渲染 |
| 下载模型与传输 | `BBDown.Core.Download` | 下载领域模型（DownloadRequest、RunConfig、WorkContext、PipelineSink 等）与传输实现（DownloadUtil、PartFile、CdnHost、BBDownAria2c） |
| 基础设施底座 | `BBDown.Core` / `Entity` / `Util` / `PlayUrl` | API 入口、实体、通用工具、播放地址解析 |

## 宿主集成点

Core 通过以下显式注入点向宿主（CLI / GUI）开放能力，宿主无需改动下载链路即可定制输出与交互：

| 注入点 | 说明 |
| --- | --- |
| `Logger.Output` | 日志输出目标。null 时写控制台（含颜色与 `BeforeWrite` 钩子）；GUI 等无控制台宿主替换为窗口日志区回调（参数为级别 + 完整渲染文本，需自行保证线程安全）。`BeforeWrite` 仅对默认控制台路径生效 |
| `AppConfig.UserAgent` | 请求级 UA。`--user-agent` 由 `WorkSetup.ResolveConfig` 落入该字段，空串回落 `HTTPUtil.UserAgent` 进程级默认，并发任务互不覆盖 |

## 委托回调清单

下表记录 Core 与主项目 / GUI 中保留的委托（`Func` / `Action` / 自定义 delegate）回调。这些回调要么是宿主集成（Core 回吐进度/日志/交互给 CLI、Serve、GUI），要么是编排分离（高阶函数 / 模板方法），没有一处是 hack（不存在特判 / 绕过逻辑）。

### 宿主集成（Core 回吐到上层）

| 回调 | 签名 | 用途 |
| --- | --- | --- |
| `PipelineSink.Meta / Saved / Sample` | `Action<VInfo>` / `Action<string>` / `Action<double,long>` | 下载链路向调用方回吐进度；取代把 serve 的 `DownloadTask` 一路透传，依赖保持单向。CLI 传 `ProgressBar.OnSample`，serve/GUI 传各自回调 |
| `DownloadConfig.OnSample` / `ProgressSampler` | `Action<double,long>` | 每个采样周期回吐（总进度，本周期新增字节），供 serve 的下载任务观察；CLI 由 `BBDown.ProgressBar` 渲染器消费该回调 |
| `LiveSegmentWriter.onBytes` | `Action<long>` | 直播每读一块回吐字节数，供进度显示 |
| `LiveRecorder.onSegmentStart` | `Action<int>` | 每开始写一个分段回吐序号，供直播进度显示「第 N 段」 |
| `Login.showQr` | `Func<string,Task>` | 生成二维码后回调展示：CLI 落盘 + 打印 ASCII，GUI 弹窗 |
| `Login.onState` | `Action<QrState>` | 轮询状态回吐：CLI 不传（null），GUI 更新状态文本 |
| `QueueRunner.dispatch / Executor / Logger`（GUI） | `Action<Action>` / `Func<TaskState,CancellationToken,Task<int>>` / `Action<TaskState,string>` | UI 线程回投（Avalonia 线程模型必需）+ 队列调度与执行子进程解耦 + 异常日志回吐 |

### 编排分离（高阶函数 / 模板方法）

| 回调 | 签名 | 用途 |
| --- | --- | --- |
| `LiveRecorder.ResolveStream / WriteSegment` | 自定义 `delegate` | 状态机对「解析流 / 写分段」两个 IO 依赖的端口；生产注入 `LiveFetcher.FetchPlayInfoAsync` / `LiveSegmentWriter.WriteAsync` |
| `Login.QrLoginPlan.Generate / Poll / Interpret` | 3 × `Func` | 扫码登录模板方法：Web 与 TV/App 两套仅这三个环节不同，轮询循环共享 |
| `PageQueue.RunPagesAsync.run` | `Func<Page,CancellationToken,Task>` | 分 P 编排：本函数只管「遍历 + 聚合失败」，单页逻辑由调用方注入 |
| `SubUtil.TryFetchAsync.fetch` + `candidates[]` | `Func<Task<List<Subtitle>>>` | 字幕多候选接口逐个回退 |
| `SubUtil.FromJsonAsync.locate` | `Func<JsonElement,JsonElement>` | 不同接口的 JSON 定位路径不同 |
| `BBDownApiServer.RunGatedAsync.download` | `Func<Task>` | 任务级并发闸门与下载动作分离 |
| `CommandLineInvoker.GetRootCommand.action` | `Func<DownloadRequest,Task<int>>` | 命令行解析与执行分离（System.CommandLine 的 `SetAction` 结构使然） |

## 依赖约束

1. **依赖单向、无环**：下载域（Pipeline → Media/Live/Auth/Mux → Download → 底座）必须保持有向无环。新增代码禁止反向引用上层命名空间。
2. **禁止跨层引用**：能力层不得引用编排层（如 `Media` 不得 `using BBDown.Core.Pipeline`）；模型层不得引用能力层。
3. **底座互依为例外**：`Core` / `Entity` / `Util` / `PlayUrl` 之间的互相引用为既有事实（Logger、Config、HTTPUtil 等交织），新代码应尽量只依赖 `Core` 根，不再加深底座内部的耦合。
4. **外部程序执行统一走 `BBDown.Core.Util.Utils.RunExe`**，各能力层不得自建进程调用（原 `Muxer.RunExe` 已上提）。对外部进程的文件交换协议（`Download.PostProcessClient`）同样只依赖底座，不反向引用能力层。
5. **下载模型归位**：下载任务的入参/上下文/回调类型（DownloadRequest、RunConfig、FetchResult、WorkContext、PipelineSink、ToolPaths、LiveQuality、格式枚举）统一放在 `BBDown.Core.Download`，避免模型层反向引用能力层。

## 校验方式

新增或调整命名空间后，可通过分析全部 `.cs` 文件的 `using BBDown.Core.*` 生成依赖矩阵并做拓扑排序，确认下载域无环。
