# JSON API 文档

BBDown 的服务器模式（`BBDown serve`）会在本地启动一个 HTTP 服务器，对外暴露任务增删查的 JSON API。本文档描述这些接口的请求 / 响应格式、数据结构与使用注意事项。

> **⚠️ 安全警告：该接口默认免令牌即可调用，未指定 `--serve-token` 时即使绑定到非回环地址（如 `0.0.0.0`）也仅打印警告、不强制鉴权；显式指定 `--serve-token` 后所有接口强制令牌鉴权，客户端必须携带 `X-BBDown-Token` 请求头（WebSocket 握手经 `?token=` 查询参数，见 [WebSocket 事件流](#websocket-事件流)），否则返回 `401`。
> 令牌只防未授权调用、不验证调用方身份；服务器**默认仅对回环来源开放 CORS**（`127.0.0.1` / `localhost` 页面的跨源请求带 `Access-Control-Allow-Origin` 响应头），其余来源需显式 `--cors-origin <url>` 放行；恶意网页（非回环 `Origin`）依旧拿不到 CORS 头、被浏览器拦截。无论是否开 CORS，**切勿直接暴露到公网**；需要跨机器访问时，请自行加反向代理与 TLS，再显式指定 `serve -l http://0.0.0.0:23333`。

---

## 启动服务器

```bash
# 默认监听 http://127.0.0.1:23333
BBDown serve

# 指定监听地址与工作目录
BBDown serve -l http://0.0.0.0:23333 --work-dir "D:/Downloads"
```

| 参数               | 简写 | 说明                                                                                                                                                                          |
| ------------------ | ---- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `--listen`         | `-l` | 监听地址，默认 `http://127.0.0.1:23333`                                                                                                                                       |
| `--serve-token`    |      | 鉴权令牌；显式传入后才启用强制鉴权（所有接口均须携带 `X-BBDown-Token` 头，WebSocket 握手经 `?token=` 查询参数），未传入则默认免令牌开放并仅警告                                                                       |
| `--work-dir`       |      | 所有任务的下载输出目录（请求体中的 `WorkDir` 字段会被忽略，一律以服务端为准）                                                                                                     |
| `--max-concurrent` |      | 同时下载的任务数上限，默认 `0` 表示不限制；设为 `N > 0` 时最多 `N` 个任务同时下载，其余按提交顺序排队（`Status` 为 `Queued`），单个任务内部的下载并行度由多线程下载器自行决定 |

服务器启动后会一直运行，直到进程被终止（可用 `Ctrl+C` 优雅取消正在进行的下载）。

> **鉴权：** 默认免令牌即可调用，未指定 `--serve-token` 时仅打印警告；显式指定 `--serve-token` 后**所有接口**（含 `GET /api/v1/tasks*` 与 WebSocket 握手）均需携带鉴权令牌——HTTP API 用请求头 `X-BBDown-Token: <token>`，WebSocket 握手用查询参数 `?token=<token>`（浏览器无法自定义请求头，该例外仅限 `/hubs/tasks`），未携带或错误一律返回 `401`。令牌仅由 `--serve-token` 显式指定，未传入时即使绑定到非回环地址也仅警告、不自动生成令牌。

---

## 接口一览

所有响应均为 JSON。任务标识（`{id}`）为 **ResourceId**（见 [任务标识](#任务标识)）：在 `DownloadTask` 的 JSON 中序列化为规范字符串（如 `season2539`），路径参数使用同一编码。

| 方法   | 路径                            | 说明                                                  |
| ------ | ------------------------------- | ----------------------------------------------------- |
| GET    | `/api/v1/tasks`                 | 获取运行中与已完成任务的整体快照                      |
| GET    | `/api/v1/tasks/running`         | 获取正在运行的任务列表                                |
| GET    | `/api/v1/tasks/finished`        | 获取已完成的任务列表                                  |
| GET    | `/api/v1/tasks/{id}`            | 获取指定任务详情                                      |
| POST   | `/api/v1/tasks`                 | 新增下载任务（202 受理 / 200 命中已有 / 400 / 429）；`?mode=enqueue` 仅入暂停表不执行，待 `start` 触发 |
| POST   | `/api/v1/tasks/{id}/start`      | 启动 enqueue 暂停的任务（200 已启动 / 404 非暂停态 / 429 队列满） |
| DELETE | `/api/v1/tasks/finished`        | 移除所有已完成任务                                    |
| DELETE | `/api/v1/tasks/finished/failed` | 移除所有已失败（`IsSuccessful == false`）的已完成任务 |
| DELETE | `/api/v1/tasks/{id}`            | 移除指定已完成任务                                    |
| POST   | `/api/v1/tasks/{id}/stop`       | 取消指定运行中 / 排队中任务（不影响其他任务）         |
| GET    | `/healthz`                      | 健康检查（匿名放行，不要求令牌）                      |

---

## 接口详情

### 获取任务快照

- **Endpoint：** `/api/v1/tasks`
- **Method：** GET
- **Description：** 获取运行中和已完成任务的整体快照。
- **Response：** JSON 格式的 `DownloadTaskSnapshot`，包含 `Running` 与 `Finished` 两个 `DownloadTask` 列表。

### 获取正在运行的任务列表

- **Endpoint：** `/api/v1/tasks/running`
- **Method：** GET
- **Response：** JSON 格式的 `List<DownloadTask>`，即正在运行的任务列表。

### 获取已完成的任务列表

- **Endpoint：** `/api/v1/tasks/finished`
- **Method：** GET
- **Response：** JSON 格式的 `List<DownloadTask>`，即已完成的任务列表。

### 获取特定任务

- **Endpoint：** `/api/v1/tasks/{id}`
- **Method：** GET
- **Description：** 按任务 id 获取任务详情（运行中的或已完成的均可）。
- **Parameters：**
    - `{id}`（路径参数）：任务的规范 id（如 `av170001`、`season2539`），见 [任务标识](#任务标识)。
- **Response：**
    - 找到匹配任务：返回 JSON 格式的 `DownloadTask`。
    - 未找到：返回 `404 Not Found`。

### 添加任务

- **Endpoint：** `/api/v1/tasks`
- **Method：** POST
- **Description：** 向任务列表新增一个下载任务。
- **Request Body：** JSON 格式的任务信息，需符合 `ServeRequestOptions`（由 `DownloadRequest` 裁剪出的受控子集）。不要求包含所有字段，**只需有 `Url` 字段**即可；`Url` 支持与命令行相同的 `av|bv|BV|ep|ss` 编号。提交模式由查询参数 `?mode` 控制：缺省或 `execute` 受理即执行，任务初始 `Status` 为 `Queued`；`enqueue` 仅入暂停表不执行，任务初始 `Status` 为 `Pending`，待 `POST /api/v1/tasks/{id}/start` 触发。
- **Response：**
    - 新任务受理成功（`execute`）：`202 Accepted`，响应体为 `DownloadTask` JSON，`Location` 头指向 `/api/v1/tasks/{id}`；任务初始 `Status` 为 `Queued`（已受理、等待执行）。`enqueue` 模式受理成功同样返回 `202`，但任务初始 `Status` 为 `Pending`（等待 `start`）。
    - 重复提交同一资源：`200 OK`，响应体为**已有**的运行中任务（不会重复下载）。
    - 请求体无法解析：`400 Bad Request`，错误消息为 `"输入有误"`。
    - 受理队列已满：`429 Too Many Requests`。

> **安全限制：** 出于安全考虑，请求体只接受受控子集字段，以下主机可控字段**不会**出现在 `ServeRequestOptions` 中（即便传入也会被忽略），一律以服务端启动时的配置为准：
> `FFmpegPath`、`Mp4boxPath`、`Aria2cPath`、`Aria2cArgs`、`WorkDir`、`FilePattern`、`MultiFilePattern`、`Debug`、`UserAgent`、`ConfigFile`。
> 下载输出目录请在启动服务时用 `serve --work-dir` 指定；FFmpeg / MP4Box / aria2c 请放在 BBDown 同目录或系统 `PATH` 中。
>
> **回调：** 请求体可携带 `CallBackWebHook`（字符串），任务**完成**后会以 `POST` 方式向该地址回传 `DownloadTask` 的 JSON；留空或不传则不回调。

### 移除所有已完成任务

- **Endpoint：** `/api/v1/tasks/finished`
- **Method：** DELETE
- **Auth：** 显式传入 `--serve-token` 时需携带鉴权令牌（见上文鉴权说明）；未传入则默认免令牌。
- **Response：** `200 OK`。

### 移除所有已失败的已完成任务

- **Endpoint：** `/api/v1/tasks/finished/failed`
- **Method：** DELETE
- **Auth：** 显式传入 `--serve-token` 时需携带鉴权令牌（见上文鉴权说明）；未传入则默认免令牌。
- **Description：** 仅移除已完成且失败（`IsSuccessful == false`）的任务。
- **Response：** `200 OK`。

### 移除特定已完成任务

- **Endpoint：** `/api/v1/tasks/{id}`
- **Method：** DELETE
- **Auth：** 显式传入 `--serve-token` 时需携带鉴权令牌（见上文鉴权说明）；未传入则默认免令牌。
- **Description：** 按任务 id 移除对应的已完成任务。
- **Parameters：**
    - `{id}`（路径参数）：任务的规范 id（如 `av170001`、`season2539`），见 [任务标识](#任务标识)。
- **Response：** 无论是否找到对应任务，均返回 `200 OK`。

### 启动暂停的任务

- **Endpoint：** `/api/v1/tasks/{id}/start`
- **Method：** POST
- **Auth：** 显式传入 `--serve-token` 时需携带鉴权令牌（见上文鉴权说明）；未传入则默认免令牌。
- **Description：** 启动以 `?mode=enqueue` 提交、尚处 `Pending` 状态的任务，将其投入执行队列开始下载。
- **Parameters：**
    - `{id}`（路径参数）：任务的规范 id（如 `av170001`、`season2539`），见 [任务标识](#任务标识)。
- **Response：**
    - 任务在暂停表且已投入执行队列：`200 OK`，任务转为 `Queued` / `Running`。
    - 任务不在暂停表（已运行 / 未知 / 已结束）：`404 Not Found`。
    - 执行队列写满：`429 Too Many Requests`，任务保留 `Pending` 可重试。

### 取消单个任务

- **Endpoint：** `/api/v1/tasks/{id}/stop`
- **Method：** POST
- **Auth：** 显式传入 `--serve-token` 时需携带鉴权令牌（见上文鉴权说明）；未传入则默认免令牌。
- **Description：** 取消指定 id 的运行中或排队中任务，不影响其他任务。每个任务持有与进程级关停令牌 `Link` 的 `CancellationTokenSource`，调用该接口即触发其 `Cancel()`，只终止目标任务；`Ctrl+C`（全局令牌）仍会取消所有进行中的任务。
- **Parameters：**
    - `{id}`（路径参数）：任务的规范 id（如 `av170001`、`season2539`），见 [任务标识](#任务标识)。
- **Response：**
    - 找到匹配的运行中 / 排队中任务：取消该任务，返回 `200 OK`。
    - 未找到：返回 `404 Not Found`。

---

## WebSocket 事件流

任务事件流经 WebSocket 通道（`/hubs/tasks`）向外推送：任务产生消息 / 进度快照 / 选项请求，选项交互（逐集确认 / 选轨）可经 `submitChoice` 帧远程应答。该通道始终开启，服务端持续推送事件流，交互选项在无订阅者时回落非交互（仍需交互的任务按非交互默认值收尾）。

> **消息来源与展示：** Core 只产生消息（下载链路主动消息 + 日志系统的 Info / Warn / Error 业务消息），不决定展示方式。CLI 渲染到控制台（含颜色与进度条）、GUI 输出到窗口日志区、serve 经本通道推送到订阅者——`message` 事件即这两类业务消息的统一出口。

### 连接与鉴权

- **地址：** `ws://127.0.0.1:23333/hubs/tasks`（TLS 下为 `wss://`）。
- **鉴权：** 浏览器 WebSocket 无法自定义请求头，握手令牌经 query 传（`?token=<token>`）；该例外**仅限** `/hubs/tasks` 路径。HTTP API 端点不接受 query 传令牌。
- **Origin 校验（CSWSH 防线）：** 无 `Origin` 头（脚本 / 非浏览器客户端）放行；等于 `--cors-origin` 或回环来源放行；其余跨源握手拒绝（`403`）。
- **连接上限：** 每客户端 IP 最多 5 个并发连接，超限拒绝升级（`429`）；单帧消息上限 64 KB，超限关闭连接。

### 帧协议（UTF-8 JSON 文本帧）

**客户端 → 服务端：**

| `kind` | 字段 | 说明 |
| ------ | ---- | ---- |
| `subscribe` | `taskId` | 订阅任务（规范 id，如 `av170001`）；订阅后立即收到一次当前进度快照 |
| `unsubscribe` | `taskId` | 退订任务 |
| `submitChoice` | `taskId`、`requestId`、`choice` | 应答任务抛出的选项请求；`choice` 为选项 Id，必须属于选项集合 |
| `ping` | — | 保活探测（可选） |

**服务端 → 客户端：**

| `kind` | 字段 | 说明 |
| ------ | ---- | ---- |
| `event` | `taskId`、`event` | 可靠事件（`WorkflowEvent`，`type` 判别符区分 `message` / `progressStart` / `progressSample` / `progressEnd` / `optionRequest`）。进度是阶段性的：`progressStart`（阶段开始，含 `stageName`）与 `progressEnd`（阶段结束）为低频语义事件，宿主据此显隐进度；阶段内高频样本不进本通道 |
| `snapshot` | `taskId`、`snapshot` | 阶段内最新进度样本（`ratio` 0-1 / `totalBytes` / `speed` / `detail`），订阅时推一次，此后约每 200 ms 推变化帧；阶段结束后样本清空 |
| `choiceResult` | `requestId`、`ok`、`error?` | 选项应答结果；`ok=false` 表示任务不存在、选项非法或已应答 |
| `error` | `error` | 订阅失败（任务不存在、已结束或未启用交互） |

### 选项交互流程

1. 下载链路遇到交互点时抛出 `optionRequest` 事件（含 `requestId`、`scope`、`prompt`、`options`（`{id, label}` 对象数组）、`deadline`、`defaultOptionId?`），工作流挂起等待应答。
2. 客户端向该任务回 `submitChoice` 帧（`choice` 必须是 `options` 中某项的 `id`）。
3. 服务端校验后恢复工作流，回 `choiceResult` 帧。
4. 超时（`deadline`，默认 5 分钟）或任务被停止时，挂起的选项转为取消，任务按取消路径收尾。

> **安全限制：** 交互仅接受枚举选项（`options` 集合内），不接受任意输入；任何已认证客户端可应答任意任务的选项（单用户 / 可信局域网语义）。

---

## 任务标识

任务唯一标识为 `ResourceId`（`BBDown.Core.ResourceId`，判别联合），取代旧版的字符串 AID。同一资源在运行中与已完成列表内各自唯一，重复提交同一资源会直接返回已有的运行中任务；不同资源形态（番剧整季 / 空间 / 稍后再看等）与普通视频平权，不再是「AID 字符串」能表达的单一形态。

在 `DownloadTask` 的 JSON 中，`Id` 序列化为**规范字符串**（如 `"season2539"`），`/api/v1/tasks/{id}` 的路径参数使用**同一编码**，客户端拿到 `Id` 即可直接回显到路径：

| 类型                       | `Id`（JSON 字段与路径参数同一串） |
| -------------------------- | --------------------------------- |
| `av`（普通视频）           | `av170001`                        |
| `ep`（番剧单集）           | `ep2539`                          |
| `season`（番剧整季）       | `season2539`                      |
| `cheeseEp`（课程单集）     | `cheeseEp123`                     |
| `cheeseSeason`（课程整季） | `cheeseSeason123`                 |
| `fav`（收藏夹）            | `fav100_200`                      |
| `mediaList`（合集）        | `mediaList789`                    |
| `series`（系列）           | `series789`                       |
| `space`（UP 主空间）       | `space402787936`                  |
| `watchLater`（稍后再看）   | `watchLater`                      |

> 注意：旧版 `Aid` 字段（字符串）与「裸 AID 数字」路径参数已废弃。规范编码只接受上表形态，`/api/v1/tasks` 的 `Url` 仍使用命令行输入写法（`av|bv|BV|ep|ss` 等），两者互不通用。

---

## 数据结构

### `DownloadTask`

表示一个下载任务。

| 属性                   | 类型                 | 说明                                                                                                                                                   |
| ---------------------- | -------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `Id`                   | `string`             | 资源 id 的规范字符串（见 [任务标识](#任务标识)），作为任务唯一标识。同一 id 在运行中与已完成列表内各自唯一，重复提交同一 id 会直接返回已有的运行中任务 |
| `Url`                  | `string`             | 任务请求时的 URL。不要求完整 URL，命令行支持的 `av\|bv\|BV\|ep\|ss` 均可                                                                               |
| `TaskCreateTime`       | `long`               | 任务创建时间，Unix 时间戳，**精确到毫秒**，本机时区                                                                                                    |
| `Title`                | `string?`            | 视频标题                                                                                                                                               |
| `Pic`                  | `string?`            | 视频封面图片链接                                                                                                                                       |
| `VideoPubTime`         | `long?`              | 视频发布时间，Unix 时间戳，精确到秒                                                                                                                    |
| `TaskFinishTime`       | `long?`              | 任务完成时间，Unix 时间戳，**精确到毫秒**，本机时区                                                                                                    |
| `Progress`             | `double`             | 下载进度，0–1 之间的小数                                                                                                                               |
| `DownloadSpeed`        | `double`             | 下载速度，单位 Byte/s。下载中为最后一次更新的实时速度，完成后为平均速度                                                                                |
| `TotalDownloadedBytes` | `long`               | 总下载字节数（Byte）；完成后的数值比实际文件略小（见下方注意事项）                                                                                     |
| `ErrorMessage`         | `string?`            | 失败原因（本机绝对路径已替换为 `<redacted-path>`）；任务成功或未失败时为 `null`                                                                        |
| `IsSuccessful`         | `bool`               | 任务是否成功完成                                                                                                                                       |
| `Status`               | `string`             | 任务状态：`Pending`（已受理、等待手动启动，仅 `?mode=enqueue` 提交时出现）/ `Queued`（已提交执行、等待并发额度，仅 `--max-concurrent > 0` 时出现）/ `Running`（下载中）/ `Finished`（已结束，成败见 `IsSuccessful`）     |
| `SavePaths`            | `Collection<string>` | 已生成文件的本地路径集合（可能包含视频、音频、弹幕、封面等）                                                                                           |

### `DownloadTaskSnapshot`

任务快照，对应 `/api/v1/tasks` 的响应。

| 属性       | 类型                          | 说明               |
| ---------- | ----------------------------- | ------------------ |
| `Running`  | `IReadOnlyList<DownloadTask>` | 正在运行的任务列表 |
| `Finished` | `IReadOnlyList<DownloadTask>` | 已完成的任务列表   |

### `DownloadRequest` / `ServeRequestOptions`

`DownloadRequest` 是贯穿解析与下载全流程的运行时配置，其字段与命令行参数几乎一一对应，取值使用命令行中会用的值即可。字段会随版本变化，请以对应版本的源码为准：

- [`BBDown.Core/Download/DownloadRequest.cs`](./BBDown.Core/Download/DownloadRequest.cs)：所有运行时配置字段定义。
- [`BBDown/Serve/ServeRequestOptions.cs`](./BBDown/Serve/ServeRequestOptions.cs)：serve 请求契约，是 `DownloadRequest` 的受控子集，并在其基础上新增 `CallBackWebHook`。

---

## 注意事项

- **进度偏差：** 受 BBDown 下载进度回报频率所限，`TotalDownloadedBytes` 会比实际下载文件偏小，大约少等效于 1 秒下载速度的体积；文件本身极小时偏差比例会更明显。
- **单任务取消：** `POST /api/v1/tasks/{id}/stop` 可取消单个运行中 / 排队中的任务（不影响其他任务），详见 [接口详情](#取消单个任务)。终止整个服务器（`Ctrl+C`）仍会经全局令牌取消所有进行中的任务。
- **并发控制：** 默认**不限制**同时执行的下载任务数，短时间内频繁提交任务会同时拉起大量下载，可能耗尽带宽 / 系统资源。启动时加 `--max-concurrent N`（`N > 0`）即可限流：最多 `N` 个任务同时下载，多余任务按提交顺序排队（`Status` 为 `Queued`，排队期间即可查询到该任务）；每个任务内部的下载并行度（分片并发）由多线程下载器自行决定，不受此上限约束。注意：`POST /api/v1/tasks` 返回 `202` 只代表任务已受理，不代表已开始下载；受理队列有长度上限，写满时返回 `429`；同一资源重复提交仍会被去重（返回已有任务）；若请求中开启了 `UseAria2c`，实际连接由 aria2c 自行管理，不受此上限约束。
- **断点续传：** 下载统一走 downloader 库，数据先写入 `<目标路径>.download` 临时文件，续传元数据内嵌在文件末尾周期性刷新。**重跑同一条命令即可从断点继续**——既能在单条流粒度续传，也能在合集 / 多 P 粒度续传：某分 P 的视频轨下完但音频轨失败，重跑时视频轨会被直接跳过、只补下音频轨。下载失败或被 `Ctrl+C` 中断时，临时文件会保留，重跑即可续上；服务端内容变化（如换画质）时自动删除临时文件重下。所有分片（连同边下边混流的临时文件）都成功后才清理这些临时文件。
- **`--save-records`：** 归档以 `(aid, cid)` 为键（同一 `aid` 的不同分 P 互不干扰，旧版仅按 `aid` 记录会导致多 P 从第 2 P 起被误跳过），并且**只有整段（含混流）成功后才写入**；记录的文件被删除 / 移动后会重新下载。旧版 `aid|` 拼接格式已失效，启动时遇到会被忽略并提示一次。
- **`--stop-on-error`：** 默认关闭，即某个分 P 下载失败时会继续下载其余分 P，最后汇总失败清单并以非零状态码退出；开启后遇到第一个失败的分 P 立即停止。
- **`--max-retry`：** 每个下载项在首次尝试之外的额外重试次数，默认 3；非必要项（字幕 / 封面 / 弹幕 / 配音 / 评论）耗尽仅跳过该项，必要项（音视频 / 混流）耗尽则该分 P 失败。serve 请求体字段为 `MaxRetry`（对应 `ServeRequestOptions`）。
- **`AllowPreview`：** 请求体可携带该布尔字段（对应命令行 `--allow-preview`）。充电专属稿件在无充电权限时接口照常返回成功但只下发试看片段，默认会被识别并跳过，任务表现为 `IsSuccessful == false`；传 `true` 则保留试看片段，输出文件名带 `[试看]` 前缀。
- **CORS：** 服务器**默认仅对回环来源开放**（`127.0.0.1` / `localhost` 页面的跨源请求放行，与本机页面直连 serve 的场景对齐）；其余来源需显式 `--cors-origin <url>` 放行。非回环 `Origin` 的浏览器请求依旧拿不到 `Access-Control-Allow-Origin` 头、被浏览器拦截（CSRF 面不因此扩大），仅建议在本地 / 可信网络下使用。
- **专栏导出：** `POST /api/v1/tasks` 接受专栏（opus / cv）地址，与音视频链路共用同一受理队列与并发闸门，经 `OpusArticle` 路由到专栏导出链路。专栏模式仅 `i`（专栏图片）与 `M`（YAML front matter）内容标志生效，其余标志（a / v / m / s / C / d / o / O / S）自然失效，任务日志会给出调试提示。默认内容集 `avmsCiM` 已包含 `i` / `M`，即默认导出图片与 front matter。
- **评论下载（`--comment`）：** 请求体可携带 `CommentCount` / `CommentSort` / `CommentFormats` / `FullComment` 四个字段（与命令行选项同名同义，默认 `CommentCount=0` 即不下载）。`CommentCount > 0` 时评论区按 `aid` 去重抓取（多 P 同稿只抓一次），产物为与主文件同目录的 `<标题>.comments.json` / `<标题>.comments.txt`。注意：加 `FullComment`（额外翻页抓全楼中楼）会随评论条数线性放大请求量，显著拉长单个任务的耗时，请按需使用。

---

## 使用例

> 以下示例使用默认的 `23333` 端口；若以其他地址启动 `serve`，请相应替换 URL。

### 用 BV 号添加任务

```shell
curl -X POST -H 'Content-Type: application/json' \
  -d '{ "Url": "BV1qt4y1X7TW" }' \
  http://localhost:23333/api/v1/tasks
```

### 用 av / ep / ss 编号添加任务

```shell
curl -X POST -H 'Content-Type: application/json' \
  -d '{ "Url": "av170001" }' \
  http://localhost:23333/api/v1/tasks
```

### 用 opus / cv 专栏地址添加任务

```shell
curl -X POST -H 'Content-Type: application/json' \
  -d '{ "Url": "https://www.bilibili.com/opus/1230485246732926996" }' \
  http://localhost:23333/api/v1/tasks
```

仅导出专栏图片、不要 YAML front matter：

```shell
curl -X POST -H 'Content-Type: application/json' \
  -d '{ "Url": "cv123", "Content": "i" }' \
  http://localhost:23333/api/v1/tasks
```

### 下载到指定目录

Windows：

```shell
curl -X POST -H 'Content-Type: application/json' \
  -d '{ "Url": "BV1qt4y1X7TW", "FilePattern": "C:\\Downloads\\<videoTitle>[<dfn>]" }' \
  http://localhost:23333/api/v1/tasks
```

Unix-Like：

```shell
curl -X POST -H 'Content-Type: application/json' \
  -d '{ "Url": "BV1qt4y1X7TW", "FilePattern": "/Downloads/<videoTitle>[<dfn>]" }' \
  http://localhost:23333/api/v1/tasks
```

### 带任务完成回调

```shell
curl -X POST -H 'Content-Type: application/json' \
  -d '{ "Url": "BV1qt4y1X7TW", "CallBackWebHook": "http://my-service.example.com/bbdown/callback" }' \
  http://localhost:23333/api/v1/tasks
```

### 查询任务列表

```shell
# 整体快照（运行中 + 已完成）
curl http://localhost:23333/api/v1/tasks

# 仅运行中
curl http://localhost:23333/api/v1/tasks/running

# 仅已完成
curl http://localhost:23333/api/v1/tasks/finished

# 指定任务详情
curl http://localhost:23333/api/v1/tasks/av12345678
```

### 清理已完成任务

```shell
# 清空所有已完成
curl -X DELETE http://localhost:23333/api/v1/tasks/finished

# 仅清理由失败的任务
curl -X DELETE http://localhost:23333/api/v1/tasks/finished/failed

# 按 id 删除某个已完成任务
curl -X DELETE http://localhost:23333/api/v1/tasks/av12345678
```
