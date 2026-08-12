# 更新日志

本项目的所有显著变更都将记录在此文件中。

本项目遵循 [语义化版本](https://semver.org/lang/zh-CN/) 约定，文件格式基于 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)。

本文件的内容基于对代码实际差异的比对（而非提交信息），以准确反映用户可见的行为变化。

## [v1.0.0-beta.1]

### 新增

- 外部后处理插件：经主程序 `--post-process` 调起，为下载到的加密轨道执行解密；请求以 JSON 文件交换，退出码 0 且产物存在视为成功，失败静默保留加密原件。
- bili_drm 通道解密：从 playurl 提取 `bilidrm_uri` 中的 KID，按自管密钥表（环境变量 `BBDOWN_DRM_KEYS` 或 exe 同目录 `BBDown.DRM.json`）匹配 key，经 ffmpeg 执行 cbcs 解密。
- Widevine 通道解密：内置 Widevine CDM，解析 PSSH 提取 KID，以设备私钥对 LicenseRequest 签名（RSASSA-PKCS1-v1_5 + SHA-1）后向 B 站 license 服务器取钥，校验响应签名后解出内容密钥，再经 ffmpeg 执行 cbcs 解密；需要 `device.wvd`（环境变量 `BBDOWN_WVD_PATH` 或 exe 同目录）。
- 取钥失败原因区分：bili_drm 无匹配 key 报密钥缺失；widevine 缺设备文件报设备缺失，license 交互失败报取钥失败。
- 发布产物附带仓库根目录 `device.wvd`，与 exe 同目录，widevine 通道开箱即用。
- GitHub Actions 流水线：三平台跑测试，三平台发布自包含单文件并上传产物（含 `device.wvd`）。

### 修复

### 变更

- 内部重构：Widevine 取钥链路拆分为 PSSH 解析、license 交互、密钥派生三部分；两条通道的取钥收敛至统一入口，解密执行层不再感知通道差异。
