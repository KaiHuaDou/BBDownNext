# BBDown

<p align="center">
  <b>BBDown</b> 是一个免费、便捷且高效的哔哩哔哩视频下载 / 解析命令行工具。
</p>

<p align="center">
  <a href="#特性">特性</a> ·
  <a href="#安装">安装</a> ·
  <a href="#快速开始">快速开始</a> ·
  <a href="#参数说明">参数说明</a> ·
  <a href="#配置文件">配置文件</a> ·
  <a href="#常见问题">常见问题</a>
</p>

---

## 特性

- 支持下载普通视频、番剧、课程、直播回放、收藏夹、合集等多种内容
- 支持 **TV / APP / INTL / WEB** 四种解析模式，自动应对不同区域的限制
- 支持 **DASH** 与 **FLV** 两种封装，可按清晰度、编码优先级自由选择
- 支持杜比视界、HDR、8K、高码率等高质量音视频流
- 支持弹幕（XML / ASS）、字幕、封面、AI 字幕的下载与嵌入
- 支持 aria2c 多线程加速、断点续传与自定义混流参数
- 纯命令行，跨平台（Windows / Linux / macOS），无图形界面依赖

## 安装

### 方式一：直接下载可执行文件

前往 [Releases](https://github.com/nilaoda/BBDown/releases) 页面，下载对应平台的最新 `2.0` 版本压缩包，解压后即可使用。

### 方式二：作为 .NET 全局工具安装

```bash
dotnet tool install --global BBDown
```

### 依赖

下载与混流依赖以下二进制工具（任选其一，建议使用 ffmpeg）：

- **ffmpeg**：用于音视频下载与混流（推荐）
- **mp4box**：可选，用于杜比视界等特殊封装的混流
- **aria2c**：可选，用于多线程加速下载

## 快速开始

```bash
# 下载一个视频（默认下载最高清晰度）
BBDown "https://www.bilibili.com/video/BV1uv411q7Mv"

# 仅解析，不下载，查看可用流
BBDown "BV1uv411q7Mv" --show-info

# 仅下载音频
BBDown "BV1uv411q7Mv" -a

# 指定清晰度与编码优先级
BBDown "BV1uv411q7Mv" -q "1080P 高码率" -e "avc,flac"
```

## 参数

| 参数                   | 简写     | 说明                                                                                      |
| ---------------------- | -------- | ----------------------------------------------------------------------------------------- |
| `--tv-api`             | `-tv`    | 使用 TV 端解析模式                                                                        |
| `--app-api`            | `-app`   | 使用 APP 端解析模式                                                                       |
| `--intl-api`           | `-intl`  | 使用国际版（东南亚视频）解析模式                                                          |
| `--mp4box`             |          | 使用 MP4Box 来混流                                                                        |
| `--encoding-priority`  | `-e`     | 视频及音频编码选择优先级，逗号分隔，如 `hevc,av1,avc,flac,eac3,m4a`                       |
| `--dfn-priority`       | `-q`     | 画质优先级，逗号分隔，如 `8K 超高清, 1080P 高码率, HDR 真彩, 杜比视界`                    |
| `--show-info`          | `-info`  | 仅解析而不进行下载                                                                        |
| `--all`                |          | 展示所有分 P 标题                                                                           |
| `--aria2c`             | `-aria2` | 调用 aria2c 进行下载                                                                      |
| `--interactive`        | `-ia`    | 交互式选择清晰度                                                                          |
| `--select-page`        | `-p`     | 选择指定分 P 或分 P 范围，如 `-p 8`、`-p 1,2`、`-p 3-5`、`-p ALL`、`-p LAST`、`-p 3,5,LATEST` |
| `--video-only`         | `-v`     | 仅下载视频                                                                                |
| `--audio-only`         | `-a`     | 仅下载音频                                                                                |
| `--danmaku-only`       | `-d`     | 仅下载弹幕                                                                                |
| `--cover-only`         | `-c`     | 仅下载封面                                                                                |
| `--sub-only`           | `-s`     | 仅下载字幕                                                                                |
| `--danmaku`            | `-dd`    | 下载弹幕                                                                                  |
| `--danmaku-formats`    | `-ddf`   | 指定需下载的弹幕格式，如 `xml,ass`                                                        |
| `--skip-mux`           |          | 跳过混流步骤                                                                              |
| `--skip-subtitle`      |          | 跳过字幕下载                                                                              |
| `--skip-cover`         |          | 跳过封面下载                                                                              |
| `--skip-ai`            |          | 跳过 AI 字幕下载                                                                          |
| `--multi-thread`       | `-mt`    | 使用多线程下载（默认开启）                                                                |
| `--file-pattern`       | `-F`     | 自定义单 P 存储文件名（支持内置变量）                                                       |
| `--multi-file-pattern` | `-M`     | 自定义多 P 存储文件名                                                                       |
| `--user-agent`         | `-ua`    | 指定 user-agent                                                                           |
| `--cookie`             |          | 设置字符串 cookie 用以下载会员内容                                                        |
| `--access-token`       | `-token` | 设置 access_token 用以下载 TV/APP 会员内容                                                |
| `--config`             |          | 读取指定的本地配置文件（默认为 `BBDown.config`）                                          |

> 完整参数请运行 `BBDown --help` 查看。

### 子命令

| 子命令    | 说明                                                |
| --------- | --------------------------------------------------- |
| `login`   | 通过 APP 扫描二维码登录 WEB 账号                    |
| `logintv` | 通过 APP 扫描二维码登录 TV 账号                     |
| `serve`   | 以服务器模式运行（默认监听 `http://127.0.0.1:23333`，接口无认证，切勿暴露公网） |

## 配置文件

BBDown 支持从配置文件读取参数，避免每次都在命令行重复输入。默认读取程序目录下的 `BBDown.config`，也可通过 `--config` 指定其他路径。

配置文件每行一个参数，以 `#` 开头的行为注释：

```ini
# BBDown.config 示例
--tv-api
--dfn-priority 1080P 高码率, 720P 流畅
--multi-thread
--cookie SESSDATA=xxxxxx
```

配置文件只补齐命令行未指定的选项，同一选项以命令行为准。视频地址也可以写在配置文件里。

## 常见问题

**Q：下载提示需要大会员？**

部分番剧、课程需要大会员权限。请使用 `login` / `logintv` 登录对应账号，并通过 `--cookie` 或 `--access-token` 传入凭据。

**Q：为什么有时只能下到最低清晰度？**

部分视频的非会员下载会被限制为低清晰度，这是 B 站服务端策略，与工具无关。登录大会员账号通常可解除限制。

**Q：如何让下载更快？**

可配合 `--aria2c` 调用 aria2c 多线程下载，或使用 `--multi-thread` 内置多线程（默认开启）。

## 许可证

本项目基于 [MIT](LICENSE) 许可证开源。

---

*BBDown 2.0 · 仅供个人学习与研究使用，请遵守相关平台的服务条款。*
