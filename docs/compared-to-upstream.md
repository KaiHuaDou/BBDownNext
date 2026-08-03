# 与原版 BBDown 的差异对照

本仓库是 [nilaoda/BBDown](https://github.com/nilaoda/BBDown) 的一个增强分支（fork，远程 `KaiHuaDou/BBDown`）。
本文档逐项列出本分支相对原版的新增能力与行为改进，供选用 / 迁移时参考。

> 对照基准：原版 `nilaoda/BBDown` 提交 `259a5558cee0a349a7ebb60bd31e40c88e5bc1ed` 的 README 与源码（功能清单、参数表、TODO）。
> 能力声明均已对照本仓库源码核实（见各条目后的核实点）。

## 能力对照表

| 维度 | 原版 nilaoda/BBDown | 本分支（KaiHuaDou/BBDown） |
| --- | --- | --- |
| **WEB Cookie 续期** | ❌ 仅列于 TODO（「自动刷新 cookie」未实现） | ✅ 登录保存 `refresh_token`，下载前尝试用 **RSA-OAEP** 加密请求主动续期 `cookie`（`Login.TryRefreshWebCookieIfStaleAsync`） |
| **凭据存储** | 分离文件 `BBDownTV.data` / `BBDownApp.data`（APP 还需抓包后复制） | 单一 **`BBDown.data`**（同一 JSON 对象，源生成器序列化，AOT 安全）；WEB/TV/APP 分别落盘、互不覆盖 |
| **APP 端登录** | 无法自动获取，需抓包 `authorization: identify_v1` 并写入 `BBDownApp.data` | `login --app` **扫码登录** APP 账号，自动保存 |
| **TV 端登录** | 独立 `logintv` 子命令 | `login --tv`（与 `login` / `login --app` 统一为一个子命令的可选标志） |
| **AOT 原生发布** | 未提供；依赖运行时反射 | 代码已改造为 **AOT 安全**（`System.Text.Json` 源生成器替代反射），可 `dotnet publish -c Release -r <RID> /p:PublishAot=true` 产出单文件原生二进制 |
| **文件名日期格式** | 固定 `yyyy-MM-dd_HH-mm-ss` | 支持自定义 `<publishDate:格式>` / `<videoDate:格式>`（任意 .NET `DateTime` 格式串） |
| **文件名长度** | 无特殊处理，超长路径易写入失败 | 按 **UTF-8 字节数截断，上限 200 字节**（约 66 汉字），并清理非法字符 / 保留设备名 |
| **serve 鉴权** | 基础令牌 | 回环地址免令牌；非回环地址强制令牌（`X-BBDown-Token` 头或 `?token=` 查询），否则 401 |
| **serve 安全** | 请求体基本透传 | 请求契约收窄为**受控子集 DTO**（`ServeRequestOptions`），主机可控字段一律以服务端为准；回调地址 **SSRF 防护**（拒绝内网 / 回环）；**工作目录强制服务端控制**（请求体不含该字段） |
| **断点续传** | 基础续传 | 每条流维护 `<路径>.bbdown.part` 数据 + `<路径>.bbdown.json` **SHA256 指纹清单**，支持单流粒度与合集 / 多 P 粒度续传 |
| **cheese 课程** | 仅 Web；存在冗余 `ss` 请求 | 消除冗余 `ss` 请求；`--intl-api` 对其**自动回退 WEB**；**过滤锁定分集**（`BuildPages`） |
| **解析模式优先级** | 未明确文档化 | 明确 `DetermineApiType` 优先级 **TV > APP > INTL > WEB**；`--app-api --intl-api` 同给走 APP |
| **FLV / DASH 封装** | 通用说明 | 明确：DASH 先按 `-q` 请求再额外以 `MaxQn(127)` 取原始画质轨（两次并集）；FLV 固定 `qn=127`、忽略 `-q` |
| **测试覆盖** | 较少 | **480+ 单元测试**（Core 222 + BBDown.Tests 260，含 gRPC 打包往返、cheese 过滤、serve 安全等） |
| **代码现代化** | 传统结构 | god-class 拆分（如 `BBDownUtil` 按归属拆分）、命名收敛、`System.Threading.Lock`、`Nullable enable` + `TreatWarningsAsErrors` |

## 关键改动核实点（源码位置）

- **凭据单文件 + 源生成器**：`BBDown/CredentialStore.cs`（`CredentialJsonContext`）、`BBDown/ServeRequestOptions.cs`。
- **Cookie 续期**：`BBDown/Login.cs`（`RefreshRsaPublicKey`、`/x/passport-login/web/cookie/refresh`）。
- **APP 扫码登录**：`BBDown/Program.cs`（`login` 子命令的 `--app` 选项 → `Login.App()`）。
- **AOT 发布**：`BBDown/Directory.Build.props`（`<PublishAot>true</PublishAot>`）、`Directory.Build.props`（`<TargetFramework>net9.0</TargetFramework>`）。
- **文件名截断 / 日期格式**：`BBDown.Core/Util/FileNameUtil.cs`（`MaxBytes = 200`）、`Program.Download.cs`（`<publishDate:格式>` 解析）。
- **serve 安全**：`BBDown/BBDownApiServer.cs`（`FinalizeAuth` / `IsLoopbackUrl` / `IsSafeWebHook` / 受控子集）。
- **断点续传**：`BBDown/PartFile.cs`（`PartFile` / `PartManifest` / `Fingerprint`）。
- **cheese 增强**：`BBDown.Core/Fetcher/CheeseInfoFetcher.cs`、`BBDown/Program.cs`（`NormalizeOptionsAfterFetch` 的 intl 回退）。

## 不兼容说明（升级注意）

- **凭据格式不兼容旧版**：不再识别旧的纯字符串 Cookie、`access_token=` 前缀纯文本、`BBDownTV.data` / `BBDownApp.data` / `BBDownRefresh.data` 分离文件，需重新 `login`。
- **归档格式不兼容旧版**：`--save-archives-to-file` 现为 Tab 分隔的 `BBDown.archives`（`<aid>\t<cid>\t<路径>`），键为 `(aid, cid)`；旧版 `aid|...` 竖线格式不再识别。
- **`logintv` 子命令已合并**：原版 `logintv` 在本分支为 `login --tv`。

## 小结

本分支相对原版的主要变化：Cookie 自动续期、单文件凭据、APP 扫码登录、AOT 原生发布、自定义文件名日期格式、200 字节截断、serve 安全加固、断点续传与 cheese 增强，以及代码层面的现代化改造。升级前请注意上文的「不兼容说明」。
