# 更新日志

本项目的所有显著变更都将记录在此文件中。

本项目遵循 [语义化版本](https://semver.org/lang/zh-CN/) 约定，文件格式基于 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)。

本文件的内容基于对代码实际差异的比对（而非提交信息），以准确反映用户可见的行为变化。

## [v1.0.0-beta.2]

### 修复

- 修复无法编译的问题：Widevine 取钥请求的 User-Agent 改用 `BiliHeaders.UserAgent`（主仓库已将 UA 从 `HTTPUtil` 移至 `BiliHeaders`）。
- 修复请求 JSON 损坏时进程直接崩溃的问题：现输出失败原因并按用法错误返回退出码 2。
- 修复残留产物被误判为解密成功的问题：处理开始前删除 `DestPath` 旧文件，解密失败时删除 ffmpeg 留下的不完整产物；上次运行残留的文件此前会在插件返回退出码 0 时被主程序当作解密产物覆盖原轨。

### 变更

- 主程序对外部后处理进程的超时由 20 秒放宽至 60 秒：本插件需完成 playurl 抓取（无加密特征时含 `drm_tech_type=2` 重试）、license 取钥与 ffmpeg 解密，原 20 秒在慢网络下会中断处理；需搭配更新后的主程序生效。

## [v1.0.0-beta.1]

### 新增

- 外部后处理插件：经主程序 `--post-process` 调起，为下载到的加密轨道执行解密；请求以 JSON 文件交换，退出码 0 且产物存在视为成功，失败静默保留加密原件。
- bili_drm 通道解密：从 playurl 提取 `bilidrm_uri` 中的 KID，经 ffmpeg 执行 cbcs 解密。
- biliDRM（clearkey）自动取钥：构造 SPC（RSA-OAEP-SHA1 加密会话密钥 + AES 加密 KID 上下文）提交 `bvc-drm.bilivideo.com/bilidrm`，解析 CKC 得内容密钥；仅需公开 RSA 公钥，零配置，失败时回退密钥表。
- Widevine 通道解密：内置 Widevine CDM，解析 PSSH 提取 KID，以设备私钥对 LicenseRequest 签名（RSASSA-PKCS1-v1_5 + SHA-1）后向 B 站 license 服务器取钥，校验响应签名后解出内容密钥，再经 ffmpeg 执行 cbcs 解密；需要 `device.wvd`（环境变量 `BBDOWN_WVD_PATH` 或 exe 同目录）。
- 取钥失败原因区分：bili_drm 无匹配 key 报密钥缺失；widevine 缺设备文件报设备缺失，license 交互失败报取钥失败。
- 发布产物附带仓库根目录 `device.wvd`，与 exe 同目录，widevine 通道开箱即用。
- GitHub Actions 流水线：三平台跑测试，四组发布矩阵（win-x64 / win-arm64、osx-x64 / osx-arm64、linux-x64 / linux-arm64、linux-musl-x64 / linux-musl-arm64），AOT 单文件产物（含 `device.wvd`）上传。

### 修复

- 修复 WBI 签名未按键名升序构造 canonical 的问题：`drm_tech_type=2` 重试（widevine 通道）时 `w_rid` 与协议规范不一致，可能被服务端拒绝；签名逻辑改为复用主仓库 `SignUtil`（排序 + 编码）。
- 密钥配置文件 `BBDown.DRM.json` 反序列化改为大小写不敏感，README 的 `keys`（小写）示例现在可直接生效。
- Widevine 取钥返回多个内容密钥时，优先选择与 PSSH KID 匹配的 key，避免首个 key 与当前轨道 KID 不符导致解密失败。

### 变更

- 内部重构：Widevine 取钥链路拆分为 PSSH 解析、license 交互、密钥派生三部分；通道取钥收敛至统一入口，解密执行层不再感知通道差异。
- 发布改为 AOT 原生单文件（配置与主程序一致：Speed 优化、裁剪、去全球化依赖）；请求 JSON 与密钥配置的反序列化改用源生成器上下文，运行时反射序列化在 AOT 下不可用。
- 发布产物不再附带 `device.wvd`：csproj 移除复制、CI artifact 不含；wvd 仍保留在仓库内，README 提供仓库内文件链接，用户自行下载后放置（exe 同目录或 `BBDOWN_WVD_PATH`）。
- CI 触发分支改为 `plugins/DRM`（原 `main`）；README 协议描述对齐最新主程序行为（对所有 DASH 轨调起，加密与否由插件判断）并修正退出码语义。
