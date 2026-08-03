# BBDown

<p align="center">
  <b>BBDown</b> 是一个免费、便捷且高效的哔哩哔哩视频下载 / 解析命令行工具。
</p>

<p align="center">
  <img alt=".NET" src="https://img.shields.io/badge/.NET-9.0-512BD4?logo=dotnet&logoColor=white" />
  <img alt="License" src="https://img.shields.io/badge/license-MIT-green.svg" />
  <a href="https://github.com/KaiHuaDou/BBDown/releases"><img alt="Release" src="https://img.shields.io/github/v/release/KaiHuaDou/BBDown?label=release" /></a>
</p>

<p align="center">
  <a href="#特性">特性</a> ·
  <a href="#安装">安装</a> ·
  <a href="#快速开始">快速开始</a> ·
  <a href="#参数说明">参数说明</a> ·
  <a href="#子命令">子命令</a> ·
  <a href="#配置文件">配置文件</a> ·
  <a href="#服务器模式">服务器模式</a> ·
  <a href="#数据文件格式">数据文件格式</a> ·
  <a href="#常见问题">常见问题</a> ·
  <a href="#与原版-bbdown-的差异">与原版差异</a>
</p>

---

## 特性

- 支持下载普通视频、番剧、课程（cheese）、直播回放、收藏夹、合集 / 系列、UP 主空间列表等多种内容；多 P 与批量内容支持丰富的分 P 选择语法（`-p`，含单集 / 列表 / 区间 / `latest`）
- 支持 **TV / APP / INTL / WEB** 四种解析模式，自动应对不同区域限制，并兼容 BiliPlus 代理；WEB 模式自动 WBI 签名以降低风控
- 支持 **DASH** 与 **FLV** 两种封装，可按清晰度、编码优先级自由选择，并支持杜比视界、HDR、8K、高码率等高质量音视频流
- 支持交互式选择清晰度（`-ia`）；提供 `--show-info` 仅解析查看可用流而不下载
- 支持弹幕（XML / ASS）、字幕、封面、AI 字幕的下载与嵌入；混流时写入元数据与章节
- 支持 aria2c 多线程加速与内置默认多线程下载；支持**断点续传**，并可开启 `--save-records` 归档记录、跳过已下载内容
- 支持扫码登录 WEB / TV / APP 账号并自动保存凭据；WEB Cookie 过期时可用 `refresh_token` 自动续期。
- 高度可定制：自定义文件名与日期格式、配置文件补齐命令行选项、CDN / PCDN 自定义（`--upos-host` / `--allow-pcdn`）
- 支持服务器模式（`serve`），提供带鉴权令牌的 HTTP JSON API：回环地址免令牌、非回环强制令牌、回调做 SSRF 防护，便于与下载器 / 前端集成
- 纯命令行，跨平台（Windows / Linux / macOS），无图形界面依赖；面向 .NET 9 并兼容 AOT 发布

## 与原版 BBDown 的差异

参见 [与原版 BBDown 的差异对照](./docs/compared-to-upstream.md)。

## 安装

前往 [Releases](https://github.com/KaiHuaDou/BBDown/releases) 页面，下载最新发布版本。

前往 [Actions](https://github.com/KaiHuaDou/BBDown/actions) 页面，下载构建版本。

## 构建

需要先安装 [.NET SDK](https://dot.net)（版本 ≥ 9.0，具体版本以仓库 `global.json` 为准）。

```bash
git clone https://github.com/KaiHuaDou/BBDown.git --depth 1
cd BBDown

dotnet build -c Release
# 或 dotnet publish BBDown -r <RID> -c Release
```

构建产物位于各项目的 `bin/Release/net9.0/` 目录下

特定平台细节可参考 [ci.yml](https://github.com/KaiHuaDou/BBDown/blob/master/.github/workflows/ci.yml)

## 依赖

- **FFmpeg**：用于音视频下载与混流（推荐）。BBDown 会在 `PATH` 与程序所在目录中自动查找；也可用 `--ffmpeg-path` 显式指定。
- **MP4Box**：可选，用于杜比视界等特殊封装的混流。可用 `--mp4box` 切换为 MP4Box 混流，或用 `--mp4box-path` 指定路径。
- **aria2c**：可选，用于多线程加速下载。可用 `--aria2c` 启用，或用 `--aria2c-path` 指定路径。

> 放在 BBDown 同目录或系统 `PATH` 中即可被自动识别

## 快速开始

```bash
# 下载一个视频（默认下载最高清晰度）
BBDown "https://www.bilibili.com/video/BV16h4y137YS"

# 仅解析，不下载，查看可用流
BBDown "BV16h4y137YS" --show-info

# 仅下载音频
BBDown "BV16h4y137YS" -a

# 指定清晰度与编码优先级
BBDown "BV16h4y137YS" -q "1080P 高码率" -e "avc,flac"

# 下载番剧 / 课程（需要会员凭据）
BBDown "ep68540" --tv-api --access-token "你的token"
```

### 支持的输入

- **视频页 URL**：`https://www.bilibili.com/video/BV...`（可用 `?p=` 指定分 P）
- **短链**：`b23.tv/...`
- **裸编号**：`av{数字}`、`BV{字符}`、`ep{数字}`、`ss{数字}`
- **番剧 / 影视 / 课程**：`/bangumi/play/...`、`/cheese/...`、番剧 `md{数字}` 详情页、`/ss{季_id}`
- **合集 / 系列**：UP 主空间的 `lists/` 页面（`business=space_collection` 为合集，`business=space_series` 为系列）
- **收藏夹**：UP 主空间的 `favlist` 页面
- **空间投稿列表**：UP 主空间首页

> 命令行、配置文件与 `serve` 接口使用同一套写法。

## 参数说明

### 解析模式

| 参数         | 简写    | 说明                                                         |
| ------------ | ------- | ------------------------------------------------------------ |
| `--tv-api`   | `-tv`   | 使用 TV 端解析模式（用于番剧 / 大会员等内容）                |
| `--app-api`  | `-app`  | 使用 APP 端解析模式（gRPC，番剧仅 HEVC）                     |
| `--intl-api` | `-intl` | 使用国际版（东南亚视频）解析模式                             |
| `--host`     |         | 指定 BiliPlus host（详见脚注 [^host]）                       |
| `--ep-host`  |         | 指定 BiliPlus EP host（详见脚注 [^ep-host]）                 |
| `--tv-host`  |         | 自定义 TV 端接口请求 Host（用于代理 `api.snm0516.aisee.tv`） |
| `--area`     |         | 使用 BiliPlus 时必选，指定区域：`hk` / `tw` / `th`           |

> 同时指定多个解析模式时，优先级为 **TV > APP > INTL > WEB**（见 `DetermineApiType`）。例如 `--app-api --intl-api` 同时给出时实际走 **APP**。

### 清晰度与编码

| 参数                  | 简写    | 说明                                                     |
| --------------------- | ------- | -------------------------------------------------------- |
| `--encoding-priority` | `-e`    | 视频及音频编码选择优先级（详见脚注 [^encodingpriority]） |
| `--dfn-priority`      | `-q`    | 画质优先级（详见脚注 [^dfnpriority]）                    |
| `--video-ascending`   |         | 视频升序（最小体积优先）                                 |
| `--audio-ascending`   |         | 音频升序（最小体积优先）                                 |
| `--interactive`       | `-ia`   | 交互式选择清晰度                                         |
| `--hide-streams`      | `-hs`   | 不显示所有可用音视频流                                   |
| `--show-info`         | `-info` | 仅解析而不进行下载                                       |
| `--all`               |         | 展示所有分 P 标题                                        |

> 同时指定 `-e` 与 `-q` 时，以命令行书写的先后为准（写在前的优先）。`-q` 仅作用于清晰度筛选，编码仍由 `-e` 控制。

> **封装对 `-q` 的影响：**
>
> - **DASH**：先按 `-q` 请求一次，再额外以最高清晰度（qn=127）请求一次以取得「免二压 / 原始画质」视频轨（两次结果取并集）。因此 DASH 比 FLV 多一次播放地址请求。
> - **FLV**：固定以最高清晰度（qn=127）请求播放地址，用户通过 `-q` 指定的清晰度优先级对它**不生效**——FLV 只会产出单一最高清视频流（仍可按 `-e` 选编码）。

### 下载内容

| 参数                | 简写   | 说明                                               |
| ------------------- | ------ | -------------------------------------------------- |
| `--video-only`      | `-v`   | 仅下载视频                                         |
| `--audio-only`      | `-a`   | 仅下载音频                                         |
| `--danmaku-only`    | `-d`   | 仅下载弹幕                                         |
| `--cover-only`      | `-c`   | 仅下载封面                                         |
| `--sub-only`        | `-s`   | 仅下载字幕                                         |
| `--danmaku`         | `-dd`  | 下载弹幕（与音视频一并下载）                       |
| `--danmaku-formats` | `-ddf` | 指定需下载的弹幕格式（详见脚注 [^danmakuformats]） |
| `--skip-mux`        |        | 跳过混流步骤                                       |
| `--no-sub`          |        | 跳过字幕下载                                       |
| `--no-cover`        |        | 跳过封面下载                                       |
| `--allow-ai`        |        | 下载 AI 字幕（默认不下载，加此选项才下载）         |
| `--no-metadata`     |        | 精简混流，不写入描述、作者等元数据                 |
| `--lang`            |        | 设置混流音频语言代码，如 `chi`、`jpn` 等           |

### 下载方式与性能

| 参数               | 简写     | 说明                                                              |
| ------------------ | -------- | ----------------------------------------------------------------- |
| `--aria2c`         | `-aria2` | 调用 aria2c 进行下载（需自行准备二进制）                          |
| `--aria2c-args`    |          | 调用 aria2c 的附加参数（详见脚注 [^aria2cargs]）                  |
| `--single-thread`  | `-st`    | 使用单线程下载，用于不支持 Range 的服务器；不加此选项即默认多线程 |
| `--delay-per-page` |          | 分 P 之间下载的间隔时间，单位秒，默认 `0`（无间隔）               |
| `--upos-host`      |          | 自定义 upos（CDN）服务器                                          |
| `--no-force-host`  |          | 不强制替换下载服务器 host（默认强制替换，加此选项才不替换）       |
| `--allow-pcdn`     |          | 不替换 PCDN 域名，仅在正常情况与 `--upos-host` 均无法下载时使用   |
| `--no-force-http`  |          | 下载音视频默认以 HTTP 替换 HTTPS；加此选项则不降级（保持 HTTPS）  |

### 账号与凭据

| 参数             | 简写     | 说明                                           |
| ---------------- | -------- | ---------------------------------------------- |
| `--cookie`       |          | 字符串 cookie，用于下载网页接口的会员内容      |
| `--access-token` | `-token` | access_token，用于下载 TV / APP 接口的会员内容 |
| `--user-agent`   | `-ua`    | 指定 user-agent；不指定则使用随机 user-agent   |

> 推荐用 `login`（WEB）/ `login --tv` / `login --app` 扫码登录后自动保存凭据，避免手动粘贴 `--cookie` / `--access-token`。

### 文件、路径与调试

| 参数                   | 简写 | 说明                                                                 |
| ---------------------- | ---- | -------------------------------------------------------------------- |
| `--file-pattern`       | `-F` | 自定义单 P 存储文件名（支持内置变量，见下）                          |
| `--multi-file-pattern` | `-M` | 自定义多 P 存储文件名（支持内置变量，见下）                          |
| `--select-page`        | `-p` | 选择分 P（语法详见脚注 [^selectpage]）                               |
| `--work-dir`           |      | 设置程序工作目录                                                     |
| `--ffmpeg-path`        |      | 指定 FFmpeg 路径                                                     |
| `--mp4box`             |      | 使用 MP4Box 来混流                                                   |
| `--mp4box-path`        |      | 指定 MP4Box 路径                                                     |
| `--aria2c-path`        |      | 指定 aria2c 路径                                                     |
| `--save-records`       |      | 将下载过的视频记录到本地文件，用于后续跳过同一视频                   |
| `--stop-on-error`      |      | 遇到分 P 下载失败时立即停止（详见脚注 [^stoponerror]）               |
| `--config`             |      | 读取指定的 BBDown 本地配置文件（默认为程序目录下的 `BBDown.config`） |
| `--debug`              |      | 输出调试日志                                                         |

#### 文件名内置变量

单 P 默认文件名：`<videoTitle>`；多 P 默认文件名：`<videoTitle>/[P<pageNumberWithZero>]<pageTitle>`。

可用变量：

| 变量                   | 含义                                             |
| ---------------------- | ------------------------------------------------ |
| `<videoTitle>`         | 视频主标题                                       |
| `<pageNumber>`         | 分 P 序号                                        |
| `<pageNumberWithZero>` | 分 P 序号（前缀补零）                            |
| `<pageTitle>`          | 分 P 标题                                        |
| `<bvid>`               | 视频 BV 号                                       |
| `<aid>`                | 视频 aid                                         |
| `<cid>`                | 视频 cid                                         |
| `<dfn>`                | 视频清晰度                                       |
| `<res>`                | 视频分辨率                                       |
| `<fps>`                | 视频帧率                                         |
| `<videoCodecs>`        | 视频编码                                         |
| `<videoBandwidth>`     | 视频码率                                         |
| `<audioCodecs>`        | 音频编码                                         |
| `<audioBandwidth>`     | 音频码率                                         |
| `<ownerName>`          | 上传者名称                                       |
| `<ownerMid>`           | 上传者 mid                                       |
| `<publishDate>`        | 收藏夹 / 番剧 / 合集发布时间                     |
| `<videoDate>`          | 视频发布时间（分 P 视频与 `<publishDate>` 相同） |
| `<apiType>`            | API 类型（TV / APP / INTL / WEB）                |

> **自定义日期格式**：`<publishDate>` / `<videoDate>` 后可接 `:` + .NET 的 `DateTime` 格式串，例如 `<publishDate:yyyyMMdd>`、`<videoDate:yyyy-MM-dd HH:mm>`。省略格式串时使用默认格式。
>
> **长度限制**：最终文件名按 **UTF-8 字节数截断，上限 200 字节**（约 66 个汉字），超出部分会被裁掉，避免过长路径导致写入失败。

示例：

```bash
# 单 P：标题 + 清晰度
BBDown "BV1xx" -F "<videoTitle>[<dfn>]"

# 多 P：按序号子目录归档
BBDown "BV1xx" -M "<videoTitle>/[P<pageNumberWithZero>]<pageTitle>"

# 用发布日期组织目录
BBDown "BV1xx" -M "<publishDate:yyyy>/<publishDate:MMdd> <pageTitle>"
```

## 子命令

| 子命令  | 说明                                                                                          |
| ------- | --------------------------------------------------------------------------------------------- |
| `login` | 通过 APP 扫描二维码登录账号（默认 WEB；加 `--tv` 登录 TV，加 `--app` 登录 APP），凭据自动保存 |
| `serve` | 以服务器模式运行，提供带鉴权令牌的 HTTP JSON API（详见 [API.md](./API.md)）                   |

### `serve` 参数

| 参数            | 简写 | 说明                                                                                                          |
| --------------- | ---- | ------------------------------------------------------------------------------------------------------------- |
| `--listen`      | `-l` | 监听地址，默认 `http://127.0.0.1:23333`。回环地址免令牌；绑定非回环地址（如 `0.0.0.0`）时**强制令牌鉴权**     |
| `--serve-token` |      | serve 鉴权令牌；未提供且绑定到非回环地址时自动生成并打印，客户端需带 `X-BBDown-Token` 头或 `?token=` 查询参数 |
| `--work-dir`    |      | 所有任务的工作目录                                                                                            |

```bash
# 以默认地址启动服务器（本地回环，免令牌）
BBDown serve

# 指定监听地址与工作目录（非回环将自动要求令牌）
BBDown serve -l http://0.0.0.0:23333 --work-dir "D:/Downloads"

# 显式指定令牌
BBDown serve --serve-token "你的令牌"
```

## 配置文件

BBDown 支持从配置文件读取参数，避免每次都在命令行重复输入。默认读取程序目录下的 `BBDown.config`，也可通过 `--config` 指定其他路径。

配置文件每行一个参数（与命令行写法一致），以 `#` 开头的行为注释。视频地址也可写在配置文件里。**配置文件只补齐命令行未指定的选项**，同一选项以命令行为准。

```ini
# BBDown.config 示例
--tv-api
--dfn-priority 1080P 高码率, 720P 流畅
--cookie SESSDATA=xxxxxx

# 也支持把地址写进配置文件
BV1uv411q7Mv
```

> 带空格的参数（如 `--dfn-priority 1080P 高码率`）在配置文件中直接按原样书写即可，BBDown 会自动按空格切分并去除引号。

## 服务器模式

`BBDown serve` 会在本地启动一个 HTTP 服务器，对外暴露任务增删查的 JSON API，适合与下载器面板、自动化脚本集成。完整接口定义、数据结构与请求示例见 **[API.md](./API.md)**。

> ⚠️ **鉴权说明**：默认监听 `http://127.0.0.1:23333`（回环地址）时**免令牌**即可调用，便于本机脚本使用。一旦绑定到**非回环地址**（如 `0.0.0.0`），BBDown 会强制要求令牌鉴权：若未通过 `--serve-token` 指定，会自动生成一个令牌并打印到控制台，客户端必须携带 `X-BBDown-Token` 请求头或 `?token=` 查询参数；令牌不匹配一律返回 `401`。即使如此，服务器仍默认开启 CORS（`AllowAnyOrigin`），且令牌只防未授权调用、不验证调用方身份，因此**切勿直接暴露到公网**。需要跨机器访问时请自行加反向代理与 TLS，并显式指定 `serve -l http://0.0.0.0:23333`。

## 数据文件格式

### 凭据：`BBDown.data`

WEB / TV / APP 三类凭据**全部合并进同一个 `BBDown.data` 的同一个 JSON 对象**，由 `login`（默认 WEB）、`login --tv`、`login --app` 扫码登录后写入。对应类型未登录时其字段为 `null`，结构如下：

```json
{
  "cookie": "DedeUserID=xxx; DedeUserID__ckMd5=xxx; SESSDATA=xxx; bili_jct=xxx",
  "refresh_token": "WEB 登录时获取的刷新令牌",
  "ts": 1700000000,
  "tv_access_token": "TV 扫码登录获取的令牌（未登录为 null）",
  "tv_ts": 1700000000,
  "app_access_token": "APP 扫码登录获取的令牌（未登录为 null）",
  "app_ts": 1700000000
}
```

| 字段               | 类型    | 说明                              |
| ------------------ | ------- | --------------------------------- |
| `cookie`           | string? | 完整 Cookie 字符串                |
| `refresh_token`    | string? | 刷新令牌，用于主动续期 Cookie     |
| `ts`               | number? | WEB 凭据签发时间戳（Unix 秒）     |
| `tv_access_token`  | string? | TV 扫码登录获取的 `access_token`  |
| `tv_ts`            | number? | TV 凭据签发时间戳（Unix 秒）      |
| `app_access_token` | string? | APP 扫码登录获取的 `access_token` |
| `app_ts`           | number? | APP 凭据签发时间戳（Unix 秒）     |

### 下载归档记录：`BBDown.archives`

启用 `--save-records` 后写入，纯文本，**每行一条记录，字段以制表符（Tab，`\t`）分隔**：

```
<aid>\t<cid>\t<保存路径>
```

| 字段         | 说明                                                                             |
| ------------ | -------------------------------------------------------------------------------- |
| `<aid>`      | 视频 av 号（数字字符串）                                                         |
| `<cid>`      | 分 P 的 cid                                                                      |
| `<保存路径>` | 该分 P 完整下载成功（含混流）后的本地文件路径；可选，记录被删/移动后会被重新下载 |

- 仅在该分 P 完整成功（含混流）后才**追加**写入；键为 `(aid, cid)`，同一视频不同分 P 互不干扰。
- 再次运行同一视频时，`CheckArchive` 会跳过已记录且文件仍存在的分 P；文件被删/移走则视为未下载，重新下载。

## 常见问题

**Q：下载提示需要大会员？**

部分番剧、课程需要大会员权限。请使用 `login`（WEB）/`login --tv`/`login --app` 登录对应账号，或通过 `--cookie` / `--access-token` 传入凭据。

**Q：为什么有时只能下到最低清晰度？**

部分视频的非会员下载会被限制为低清晰度，这是 B 站服务端策略，与工具无关。登录大会员账号通常可解除限制。

**Q：FLV 模式下 `-q` 怎么没生效？**

FLV 封装固定以最高清晰度（qn=127）请求播放地址，用户的清晰度优先级对它不生效；如需按清晰度选择，请使用默认的 **DASH** 封装，并通过 `-q` 指定。

**Q：如何让下载更快？**

可配合 `--aria2c` 调用 aria2c 多线程下载，或使用内置的默认多线程（不加 `--single-thread` 即多线程）。分 P 视频可用 `--delay-per-page` 控制间隔以免触发风控。

**Q：aria2c 怎么用？**

下载 aria2c 二进制并放在 BBDown 同目录或 `PATH` 中，然后加 `--aria2c` 即可。可用 `--aria2c-args` 追加自定义参数（默认已含 `-x16 -s16 -j16 -k 5M`）。

**Q：配置文件和命令行的优先级？**

命令行未显式给出的选项，才会由配置文件补齐；命令行已给出的以命令行为准。

## 许可证

本项目基于 [MIT](LICENSE) 许可证开源。

---

*BBDown 2.0 · 仅供个人学习与研究使用，请遵守 B 站相关服务条款，勿将下载内容用于商业或侵权用途。*

[^selectpage]: 选择分 P 语法：

    - `all` 全部
    - `8` 单集
    - `1,2,5` 逗号列表
    - `3-5` 闭区间（含两端：3,4,5）；`3-3` 仅第 3 集
    - `16-` 开区间，到末集
    - `-22` 开区间，从首集到 22
    - `1,2,3-3,4-5,6-10,15-latest` 混合写法
    - `latest` / `new` 最后一集（最新一集）
    - `last` / `LAST` 倒数第二集
    - 关键字大小写不敏感；`latest` 可写作 `new`；越界数字自动夹紧到有效边界；非法项忽略并提醒
    - 以 `-` 开头的表达式需在命令行加引号：`-p "-22,25-27,33"`

[^aria2cargs]: 调用 aria2c 的附加参数，含空格的参数用引号包裹。默认参数包含 `-x16 -s16 -j16 -k 5M`。

[^encodingpriority]: 视频及音频编码的选择优先级，逗号分隔。例：`hevc,av1,avc,flac,eac3,m4a`

[^dfnpriority]: 画质优先级，逗号分隔。例：`8K 超高清, 1080P 高码率, HDR 真彩, 杜比视界`

[^danmakuformats]: 指定需下载的弹幕格式，逗号分隔，默认全部下载（如 `xml,ass`）。

[^stoponerror]: 遇到分 P 下载失败时立即停止，而不是继续下载其余分 P。默认继续，并在末尾汇总失败的分 P 后非零退出。

[^host]: 指定 BiliPlus host。使用 BiliPlus 需要 access_token、不需要 cookie；解析服务器能够获取你账号的大部分权限，请谨慎使用！

[^ep-host]: 指定 BiliPlus EP host，用于代理 `api.bilibili.com/pgc/view/web/season`；大部分解析服务器不支持代理该接口。
