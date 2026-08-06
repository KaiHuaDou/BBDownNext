# TODO / 发展路线

以下方向可用于后续规划（不代表已实装，具体以各版本 Release Notes 与源码为准）：

- [ ] /medialist/play/
- [ ] 普通 UGC 的 APP 解析改用`bilibili.app.playerunite.v1.Player/PlayViewUnite`，按照编码优先级请求并合并 AVC、HEVC 和 AV1 视频流；PGC 番剧仍使用原接口。
    - APP 最高仅 480P，或低于`--dfn-priority`明确请求的档位时，额外比较一次 WEB 结果。只有 WEB 视频档位更高才合并 WEB 视频，APP 普通、杜比和 Hi-Res 音频保持不变；WEB 比较失败继续使用已有 APP 结果。
    - 集成方可通过隐藏参数`--app-buvid`传入每账号稳定的 37 位 APP 设备标识；未传或格式无效时仅在当前进程内生成稳定临时值。
    - PlayerUnite 默认请求最高档位，交互模式重新解析具体画质时仍尊重原有`qn`。
- [ ] 增量订阅下载：在 `--save-records` 归档之上做「稍后再看 / 收藏夹 / 专栏」的增量同步（`sub` / `watchlater`），只拉新内容。
- [ ] 原生 DRM 解密：把 Widevine 一类的解密做成纯托管实现，免去外部依赖与抓包步骤。
- [ ] serve 能力补全：支持在 `serve` 里提交专栏（`opus`）导出任务、用 SQLite 替代扁平归档文件以支撑海量收藏。
    - 取消单个任务已在 `941ffd1` 实现（`POST /stop-task/{id}`，任务持有与进程级关停令牌 `Link` 的 `CancellationTokenSource`），从本列表中移除。
- [ ] 解析韧性增强：APP gRPC 番剧目前仅 HEVC 且码率为估算，可探索更完整画质；TV/APP 凭据目前无 refresh，可研究其续期路径。
- [ ] UP 主/合集/收藏夹订阅。
- [ ] 合集下载 P1 给到时间最早。
- [ ] Opus 更好的 HTML 转 Markdown 策略
- [ ] 评论图片下载支持
