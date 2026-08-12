# BBDown.DRM

BBDown 主程序的外部后处理插件：为下载到的加密轨道执行解密，作为独立进程按需被主程序调起。

## 构建

需要本地存在主仓库（相对路径引用 `..\..\..\BBDown.Core\BBDown.Core.csproj`）：

```bash
dotnet publish BBDown.DRM -c Release -r win-x64 --self-contained true
```

产物为单文件 `BBDown.DRM.exe`，并附带仓库根目录的 `device.wvd`（widevine 通道直接命中 exe 同目录）。

## 配置密钥

主程序不感知密钥，由本插件自管，二选一：

- 环境变量 `BBDOWN_DRM_KEYS`：分号或逗号分隔的 `kid:key` 条目
- exe 同目录 `BBDown.DRM.json`：`{ "keys": ["kid:key", ...] }`

`key` / `kid` 均为 16 字节，可用 32 位 hex 或 base64 编码。纯 `key` 作为全局默认，用于所有未绑定 KID 的加密轨。

## Widevine 通道

加密通道为 `widevine`（playurl 下发 `widevine_pssh`）时，密钥经 Widevine CDM 向 B 站 license 服务器自动获取，无需配置密钥表。需要提供设备文件 `device.wvd`：

- 环境变量 `BBDOWN_WVD_PATH` 指定路径
- 或 exe 同目录 `device.wvd`

取钥流程：解析 PSSH 提取 KID → 构建 LicenseRequest 并以设备私钥签名（RSASSA-PKCS1-v1_5 + SHA-1）→ 请求 license 服务器 → 校验响应签名后解出内容密钥，随后与 bili_drm 通道同样以 ffmpeg 执行 cbcs 解密。缺少 `device.wvd` 或取钥失败时按通道不支持处理，保留加密原件。

## 协议（与主程序的文件交换）

主程序对加密轨写请求 JSON（PascalCase，含 `Aid` / `Cid` / `Kind` / `TrackPath` / `DestPath` / `Ffmpeg`），以请求文件路径为唯一参数调起本插件：

```
BBDown.DRM <请求JSON路径>
```

退出码与产物语义：

| 退出码 | 含义 | 主程序行为 |
|--------|------|-----------|
| 0 且 `DestPath` 存在 | 解密成功 | 产物覆盖原轨参与混流 |
| 0 且无产物 | 轨道无加密信息 | 原文件照常混流 |
| 1 | 解密失败 / 通道不支持 / 密钥缺失 | 保留加密原件，静默降级 |
| 2 | 未配置密钥 / 请求无效 | 保留加密原件，静默降级 |

加密信息由本插件自行重新抓取（web 通道 playurl + WBI 签名），登录态经主程序的 `BBDown.data` 凭据文件获取；主程序不传任何加密特征与凭据。

## 使用

```bash
BBDown --post-process <BBDown.DRM.exe 路径> <视频链接>
```
