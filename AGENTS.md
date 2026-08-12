# AGENTS

将 DRM 相关所有逻辑都集中在这个插件内，主程序一点 DRM 不许沾！

## 概览

BBDown.DRM 是 BBDown 主程序的外部后处理插件（独立 git 仓库），为下载到的加密轨道执行解密。
以独立进程被主程序调起，文件交换协议见 README「协议」节。

## 强制要求

- **DRM 边界**：所有解密逻辑（取钥、解密、密钥/设备配置）只允许存在于本仓库；主程序的请求 JSON 只含轨道定位与本地路径，不携带任何加密特征与凭据。
- **代码规范**：沿用主仓库 AGENTS.md 的编码与文档规范（中文注释、命名、行数限制、无嵌套函数、去 OOP 包装、提交格式），不重复列出。
- **相对路径**：`ProjectReference` 指向 `..\..\..\BBDown.Core`，本仓库必须位于主仓库的 `Plugins/BBDown.DRM` 位置才能编译；CI 与本地目录布局保持一致。
- **proto**：协议文件在仓库根 `Proto/`，`csharp_namespace = BBDown.DRM.Proto`，只声明用到的字段，字段编号与 Widevine 协议标准一致。
- **发布**：AOT 原生单文件（`PublishAot`，配置与主程序 `BBDown/Directory.Build.props` 一致）；`device.wvd` 随发布产物附带（csproj `CopyToOutputDirectory`），与 exe 同目录；`KeyConfig.FindWvdPath` 按 `BBDOWN_WVD_PATH` > exe 同目录顺序查找。
- **序列化**：请求 JSON 与配置文件反序列化必须走 `DrmJsonContext`（源生成器），运行时反射序列化在 AOT 下会被裁剪。

## 结构约定

- 通道差异收敛在 `DrmKeys`（取钥统一入口），`DrmDecryptor` 只做「取钥 → ffmpeg 解密」，不感知通道类型。
- 取钥失败语义：`KeyMissing`（bili_drm 无 key）/ `DeviceMissing`（缺 wvd / pssh）/ `FetchFailed`（CDM 交互失败），失败时调用方保留加密原件。
- 禁止在解密执行层做通道特判（`drmType == "widevine"` 三元），新通道的取钥实现接入 `DrmKeys` 即可。
- bili_drm 通道的 SPC/CKC 结构、固定 salt/header 与 padding（OAEP-SHA1）为实测逆向结果，改动会使服务器拒绝请求（`get assetId failed` / `Not Found the Key ID`）。

## 其他内容

AGENT 对此文档的修改只能添加在本节，在本节添加内容无需经过批准。其他节不许动。
