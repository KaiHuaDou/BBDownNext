# 全仓特判（HACK）清单

整理代码中全部绕过正常流程的特判逻辑、硬编码 workaround 与隐式行为，供审查与后续重构定位。每条注明位置、原因与现状。与 `AGENTS.md`「其他内容」已备案项不重复展开。

## 1. 已在 AGENTS.md 备案的特判

| 位置 | 内容 |
| --- | --- |
| `BBDown.Core/ResourceId.cs` | 嵌套 record 判别联合（「禁止嵌套类」的确认例外）；现已改用 C# 15 `closed` 修饰符，穷尽性由编译器验证 |
| `BBDown.Core/Parser.cs` ExtractTracksAsync | WEB 通道忽略 qn 参数、FLV 强制最高清晰度，交互所选清晰度不改变实际下载内容（故意设计） |

## 2. 网络层

| 位置 | 特判内容 | 原因 |
| --- | --- | --- |
| `BBDown.Core/Util/HTTPUtil.cs` AppHttpClient | `AllowAutoRedirect = false`，凭据请求由 `HttpTransfer.SendTrustGatedAsync` 手动逐跳跟随 | 自动重定向会把 Cookie 头带到重定向目标，凭据门只覆盖首跳 |
| `BBDown.Core/Util/HTTPUtil.cs` StreamHttpClient / `Download/DownloaderAdapter.cs` | 自动解压关闭（None） | 媒体流不是压缩内容，自动解压会破坏数据；同时依赖未压缩的 Content-Length 语义 |
| `BBDown.Core/Media/PageAssets.cs`（弹幕） | 弹幕 XML 不走通用下载器，强制经带自动解压的 AppHttpClient | `comment.bilibili.com` 端点无视 Accept-Encoding 协商、恒返回 deflate；下载器与 aria2c 无解压能力 |
| `BBDown.Core/Util/HTTPUtil.cs` GetPostResponseAsync | 仅对 gRPC 只读查询做有界重试，注释声明非幂等写操作勿复用 | 重试语义按幂等性收紧 |
| `BBDown.Core/Util/HTTPUtil.cs` GetWebLocationAsync | 无凭据手动跟随重定向，目标不受信任主机列表限制 | 无凭据请求无门禁意义 |

## 3. CDN 与下载层

| 位置 | 特判内容 | 原因 |
| --- | --- | --- |
| `BBDown.Core/Download/DownloadUtil.cs` ReplaceUrl | 默认把 bilivideo 的 https 强制改写为 http（`--no-force-http` 可关）；`*.mcdn.bilivideo.cn:` 域名除外 | B 站 CDN 证书链历史问题的兼容性 workaround；mcdn 节点只支持 https |
| `BBDown.Core/Download/DownloadUtil.cs` DownloadAsync | URL 含 `-cmcc-` 时强制单线程 | CMCC 节点不支持 Range / 多线程分片 |
| `BBDown.Core/Download/CdnHost.cs` | 默认把 upos host 强制替换为备用 host（`--no-force-host` 可关） | 部分 CDN 节点对非浏览器流量限速 / 失效 |
| `BBDown.Core/Download/DownloaderAdapter.cs` IsDownloadSuccess | downloader 库把「目标已存在即跳过」以 Failed 状态送达且保留文件，该组合判定为成功 | 库的隐式契约，已抽纯函数并锁定单测 |
| `BBDown.Core/Download/BBDownAria2c.cs` | 6 小时进程级硬超时 + `Kill(true)`（连带子进程） | 防 aria2c 僵死占住并发槽；aria2c 会派生子进程 |
| `BBDown.Core/Media/FlvDownload.cs` DownloadClipsAsync | 直接修改共享 `DownloadConfig.ParallelCount`（片段间并行 4 × 片段内 8 = 32） | 片段间与片段内连接合计不超过 DownloaderAdapter.MaxRangeConcurrency；依赖「FLV 之后无其它下载步骤」的时序契约 |
| `BBDown.Core/Media/FlvDownload.cs` IsCodecUnsupported | FLV 链路拒绝 HEVC / AV1（上游拦截） | FLV 容器无法承载；`MergeFLV` 的 `h264_mp4toannexb` 依赖此前提 |

## 4. 接口形态

| 位置 | 特判内容 | 原因 |
| --- | --- | --- |
| `BBDown.Core/Parser.cs` TryCollectIntlAsync | INTL 双请求（prefer_code_type 0/1）任一次缺 stream_list 即放弃 intl 通道，交回通用 dash / durl 解析 | 「等价点 B」既定降级路径（AGENTS.md 备案），勿当 bug 修 |
| `BBDown.Core/PlayUrl/PlayUrlClient.cs` | 大会员专享限制时从网页源码抠 `window.__playinfo__`（仅番剧 / 课程，播放页按 ep 构造） | playurl 接口对该场景不返回 dash |
| `BBDown.Core/Fetcher/SpaceListFetcher.cs` / `FavListFetcher.cs` | 翻页终止只看实际返回量（不满一页 / 空页），叠加 MaxPages=1000 硬上限 | media_count / has_more 异常时防止请求洪泛 |
| `BBDown.Core/Fetcher/SpaceListFetcher.cs` IsRisk | code=0 但存在 is_risk / gaia_res_type 时按风控拦截报错 | 风控响应「正常 code + 空 vlist」的特有形态 |
| `BBDown.Core/Util/BiliHeaders.cs` | 直播拉流头单独构造，部分 CDN 节点强制校验 Referer | 缺失直接 403 |
| `BBDown.Core/Media/DashDownload.cs` | 杜比视界（id=126）+ FFmpeg < 5.0 时混流方式降级为 MP4Box | 旧版 ffmpeg 对 DOVI 的 mp4 封装有缺陷 |
| `BBDown.Core/Opus/OpusFetcher.Paragraph.cs` DetectHeadingLevel | 字号 24 / 22 映射 H2 / H3 | 专栏编辑器字号体系无接口化字段 |

## 5. 并发与生命周期

| 位置 | 特判内容 | 原因 |
| --- | --- | --- |
| `BBDown/Program.cs` liveSessionId | Ctrl+Break 在录制直播时重载为「停录并混流」，非录制场景落回全局取消；volatile 保证 handler 可见性 | SIGQUIT 语义重载；已知窗口见源码注释 |
| `BBDown.Core/Live/LiveRecorder.cs` pinnedCodec | 首段成功后锁定编码，锁定编码消失时回退全集候选并 LogWarn | 合并阶段只对全部分段套同一 bsf，混入异编码分段会被 ffmpeg 静默丢弃（数据丢失） |
| `BBDown.Core/Logging/MessageBus.cs` Publish | 订阅者异常静默吞掉 | 渲染故障回打日志会递归，无上报通道 |
| `BBDown.Core/Util/Utils.cs` RunExe | 取消时 kill 进程树失败静默 | 尽力而为清理 |
| `BBDown.Core/Auth/CredentialStore.cs` HardenFilePermissions | 权限收紧失败静默 | 不应影响凭据保存主流程；Windows 不收紧是已文档化妥协 |
| `BBDown/Serve/SsrfGuard.cs` IsLoopbackHost | 刻意不做 DNS 解析 | 解析结果正是 DNS rebinding 能操纵的东西 |

## 6. 本次修复消除的特判

| 位置 | 消除内容 |
| --- | --- |
| `BBDown.Core/Mux/Muxer.cs` MergeFLV | 单段直接 `File.Move` 的分支（FLV 内容配 mp4 后缀的中间态），统一走转封装 + 拼接 |
| `BBDown.Core/Media/DashDownload.cs` / `FlvDownload.cs` | 弹幕产出块在两条链路的逐行重复（提取 `PageAssets.TryDownloadDanmakuAsync`） |
| `BBDown.Core/Workflow/ProgressBus.cs` | `EndStage` / `EndActive` 完全相同的两个方法 |
| `BBDown.Core/Util/HTTPUtil.cs` 等 4 处 | `BBDOWN_INSECURE_TLS` 判定的四份拷贝（统一为 `IsTlsAcceptable`） |
| `BBDown.Core/Parser.cs` | `TryCollectIntlAsync` 内的嵌套局部函数 |
