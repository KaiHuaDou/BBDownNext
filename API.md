# JSON API 文档

BBDown 的服务器模式（`BBDown serve`）会在本地启动一个 HTTP 服务器，对外暴露任务增删查的 JSON API。本文档描述这些接口的请求 / 响应格式、数据结构与使用注意事项。

> **⚠️ 安全警告：该接口没有任何认证机制。**
> 默认监听 `http://127.0.0.1:23333`，切勿直接暴露到公网。
> 需要跨机器访问时，请自行加反向代理并补上鉴权，再显式指定 `serve -l http://0.0.0.0:23333`。
> 此外，服务器默认开启了 CORS（`AllowAnyOrigin`），任意来源网页均可向该端口发请求，请勿在不可信网络下运行。

---

## 启动服务器

```bash
# 默认监听 http://127.0.0.1:23333
BBDown serve

# 指定监听地址与工作目录
BBDown serve -l http://0.0.0.0:23333 --work-dir "D:/Downloads"
```

| 参数         | 简写 | 说明                                                                      |
| ------------ | ---- | ------------------------------------------------------------------------- |
| `--listen`   | `-l` | 监听地址，默认 `http://127.0.0.1:23333`                                   |
| `--work-dir` |      | 所有任务的工作目录（请求体中的 `WorkDir` 字段会被忽略，一律以服务端为准） |

服务器启动后会一直运行，直到进程被终止（可用 `Ctrl+C` 优雅取消正在进行的下载）。

---

## 接口一览

所有响应均为 JSON。任务标识（`{id}`）使用视频的 **AID**（字符串）。

| 方法 | 路径                      | 说明                                                  |
| ---- | ------------------------- | ----------------------------------------------------- |
| GET  | `/get-tasks/`             | 获取运行中与已完成任务的整体快照                      |
| GET  | `/get-tasks/running`      | 获取正在运行的任务列表                                |
| GET  | `/get-tasks/finished`     | 获取已完成的任务列表                                  |
| GET  | `/get-tasks/{id}`         | 获取指定 AID 的任务详情                               |
| POST | `/add-task`               | 新增下载任务                                          |
| GET  | `/remove-finished`        | 移除所有已完成任务                                    |
| GET  | `/remove-finished/failed` | 移除所有已失败（`IsSuccessful == false`）的已完成任务 |
| GET  | `/remove-finished/{id}`   | 移除指定 AID 的已完成任务                             |

---

## 接口详情

### 获取任务快照

- **Endpoint：** `/get-tasks/`
- **Method：** GET
- **Description：** 获取运行中和已完成任务的整体快照。
- **Response：** JSON 格式的 `DownloadTaskSnapshot`，包含 `Running` 与 `Finished` 两个 `DownloadTask` 列表。

### 获取正在运行的任务列表

- **Endpoint：** `/get-tasks/running`
- **Method：** GET
- **Response：** JSON 格式的 `List<DownloadTask>`，即正在运行的任务列表。

### 获取已完成的任务列表

- **Endpoint：** `/get-tasks/finished`
- **Method：** GET
- **Response：** JSON 格式的 `List<DownloadTask>`，即已完成的任务列表。

### 获取特定任务

- **Endpoint：** `/get-tasks/{id}`
- **Method：** GET
- **Description：** 按视频 AID 获取任务详情（运行中的或已完成的均可）。
- **Parameters：**
    - `{id}`（路径参数）：视频的 AID。
- **Response：**
    - 找到匹配任务：返回 JSON 格式的 `DownloadTask`。
    - 未找到：返回 `404 Not Found`。

### 添加任务

- **Endpoint：** `/add-task`
- **Method：** POST
- **Description：** 向任务列表新增一个下载任务。
- **Request Body：** JSON 格式的任务信息，需符合 `ServeRequestOptions`（由 `DownloadOptions` 裁剪出的受控子集）。不要求包含所有字段，**只需有 `Url` 字段**即可；`Url` 支持与命令行相同的 `av|bv|BV|ep|ss` 编号。
- **Response：**
    - 请求有效并成功加入队列：`200 OK`。
    - 请求体无法解析：`400 Bad Request`，错误消息为 `"输入有误"`。

> **安全限制：** 出于安全考虑，请求体只接受受控子集字段，以下主机可控字段**不会**出现在 `ServeRequestOptions` 中（即便传入也会被忽略），一律以服务端启动时的配置为准：
> `FFmpegPath`、`Mp4boxPath`、`Aria2cPath`、`Aria2cArgs`、`WorkDir`、`FilePattern`、`MultiFilePattern`、`Debug`、`UserAgent`、`ConfigFile`。
> 工作目录请在启动服务时用 `serve --work-dir` 指定；ffmpeg / mp4box / aria2c 请放在 BBDown 同目录或系统 `PATH` 中。
>
> **回调：** 请求体可携带 `CallBackWebHook`（字符串），任务**完成**后会以 `POST` 方式向该地址回传 `DownloadTask` 的 JSON；留空或不传则不回调。

### 移除所有已完成任务

- **Endpoint：** `/remove-finished`
- **Method：** GET
- **Response：** `200 OK`。

### 移除所有已失败的已完成任务

- **Endpoint：** `/remove-finished/failed`
- **Method：** GET
- **Description：** 仅移除已完成且失败（`IsSuccessful == false`）的任务。
- **Response：** `200 OK`。

### 移除特定已完成任务

- **Endpoint：** `/remove-finished/{id}`
- **Method：** GET
- **Description：** 按视频 AID 移除对应的已完成任务。
- **Parameters：**
    - `{id}`（路径参数）：视频的 AID。
- **Response：** 无论是否找到对应任务，均返回 `200 OK`。

---

## 数据结构

### `DownloadTask`

表示一个下载任务。

| 属性                   | 类型                 | 说明                                                                                                                     |
| ---------------------- | -------------------- | ------------------------------------------------------------------------------------------------------------------------ |
| `Aid`                  | `string`             | 视频解析出的 AID，作为任务唯一标识。同一 AID 在运行中与已完成列表内各自唯一，重复提交同一 AID 会直接返回已有的运行中任务 |
| `Url`                  | `string`             | 任务请求时的 URL。不要求完整 URL，命令行支持的 `av\|bv\|BV\|ep\|ss` 均可                                                 |
| `TaskCreateTime`       | `long`               | 任务创建时间，Unix 时间戳，**精确到毫秒**，本机时区                                                                      |
| `Title`                | `string?`            | 视频标题                                                                                                                 |
| `Pic`                  | `string?`            | 视频封面图片链接                                                                                                         |
| `VideoPubTime`         | `long?`              | 视频发布时间，Unix 时间戳，精确到秒                                                                                      |
| `TaskFinishTime`       | `long?`              | 任务完成时间，Unix 时间戳，**精确到毫秒**，本机时区                                                                      |
| `Progress`             | `double`             | 下载进度，0–1 之间的小数                                                                                                 |
| `DownloadSpeed`        | `double`             | 下载速度，单位 Byte/s。下载中为最后一次更新的实时速度，完成后为平均速度                                                  |
| `TotalDownloadedBytes` | `double`             | 总下载字节数（Byte）；完成后的数值比实际文件略小（见下方注意事项）                                                       |
| `IsSuccessful`         | `bool`               | 任务是否成功完成                                                                                                         |
| `SavePaths`            | `Collection<string>` | 已生成文件的本地路径集合（可能包含视频、音频、弹幕、封面等）                                                             |

### `DownloadTaskSnapshot`

任务快照，对应 `/get-tasks/` 的响应。

| 属性       | 类型                          | 说明               |
| ---------- | ----------------------------- | ------------------ |
| `Running`  | `IReadOnlyList<DownloadTask>` | 正在运行的任务列表 |
| `Finished` | `IReadOnlyList<DownloadTask>` | 已完成的任务列表   |

### `DownloadOptions` / `ServeRequestOptions`

`DownloadOptions` 是贯穿解析与下载全流程的运行时配置，其字段与命令行参数几乎一一对应，取值使用命令行中会用的值即可。字段会随版本变化，请以对应版本的源码为准：

- [`BBDown/DownloadOptions.cs`](./BBDown/DownloadOptions.cs)：所有运行时配置字段定义。
- [`BBDown/Model/ServeRequestOptions.cs`](./BBDown/Model/ServeRequestOptions.cs)：serve 请求契约，是 `DownloadOptions` 的受控子集，并在其基础上新增 `CallBackWebHook`。

---

## 注意事项

- **进度偏差：** 受 BBDown 下载进度回报频率所限，`TotalDownloadedBytes` 会比实际下载文件偏小，大约少等效于 1 秒下载速度的体积；文件本身极小时偏差比例会更明显。
- **无法取消单个任务：** 目前内部机制没有可靠的方法取消单个下载任务，任务提交后只能等待其失败或完成（`serve` 模式同样如此）。终止整个服务器（`Ctrl+C`）会取消所有进行中的任务。
- **并发无上限：** 服务器当前未对同时执行的下载任务数量做任何限制。短时间内频繁 `add-task` 会同时拉起大量下载，可能耗尽带宽 / 系统资源，请自行控制节奏（未来版本计划加入下载队列）。
- **断点续传：** 每条流下载时先写入 `<目标路径>.bbdown.part`，并维护一份 `<目标路径>.bbdown.json` 清单（记录 URL 指纹 / 各分片已完成字节 / 服务器校验器）。**重跑同一条命令即可从断点继续**——既能在单条流粒度续传，也能在合集 / 多 P 粒度续传：某分P 的视频轨下完但音频轨失败，重跑时视频轨会被直接跳过、只补下音频轨。下载失败或被 `Ctrl+C` 中断时，临时文件会保留，重跑即可续上；所有分片（连同边下边混流的临时文件）都成功后才清理这些临时文件。
- **`--save-archives-to-file`：** 归档以 `(aid, cid)` 为键（同一 `aid` 的不同分P 互不干扰，旧版仅按 `aid` 记录会导致多 P 从第 2 P 起被误跳过），并且**只有整段（含混流）成功后才写入**；记录的文件被删除 / 移动后会重新下载。旧版 `aid|` 拼接格式已失效，启动时遇到会被忽略并提示一次。
- **`--stop-on-error`：** 默认关闭，即某个分P 下载失败时会继续下载其余分P，最后汇总失败清单并以非零状态码退出；开启后遇到第一个失败的分P 立即停止。
- **CORS：** 服务器默认允许任意来源跨域请求（`AllowAnyOrigin`），仅建议在本地 / 可信网络下使用。

---

## 使用例

> 以下示例使用默认的 `23333` 端口；若以其他地址启动 `serve`，请相应替换 URL。

### 用 BV 号添加任务

```shell
curl -X POST -H 'Content-Type: application/json' \
  -d '{ "Url": "BV1qt4y1X7TW" }' \
  http://localhost:23333/add-task
```

### 用 av / ep / ss 编号添加任务

```shell
curl -X POST -H 'Content-Type: application/json' \
  -d '{ "Url": "av170001" }' \
  http://localhost:23333/add-task
```

### 下载到指定目录

Windows：

```shell
curl -X POST -H 'Content-Type: application/json' \
  -d '{ "Url": "BV1qt4y1X7TW", "FilePattern": "C:\\Downloads\\<videoTitle>[<dfn>]" }' \
  http://localhost:23333/add-task
```

Unix-Like：

```shell
curl -X POST -H 'Content-Type: application/json' \
  -d '{ "Url": "BV1qt4y1X7TW", "FilePattern": "/Downloads/<videoTitle>[<dfn>]" }' \
  http://localhost:23333/add-task
```

### 带任务完成回调

```shell
curl -X POST -H 'Content-Type: application/json' \
  -d '{ "Url": "BV1qt4y1X7TW", "CallBackWebHook": "http://my-service.example.com/bbdown/callback" }' \
  http://localhost:23333/add-task
```

### 查询任务列表

```shell
# 整体快照（运行中 + 已完成）
curl http://localhost:23333/get-tasks/

# 仅运行中
curl http://localhost:23333/get-tasks/running

# 仅已完成
curl http://localhost:23333/get-tasks/finished

# 指定 AID 详情
curl http://localhost:23333/get-tasks/12345678
```

### 清理已完成任务

```shell
# 清空所有已完成
curl http://localhost:23333/remove-finished

# 仅清理由失败的任务
curl http://localhost:23333/remove-finished/failed

# 按 AID 删除某个已完成任务
curl http://localhost:23333/remove-finished/12345678
```
