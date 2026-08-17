using System;

using BBDown.Core.Entity;

namespace BBDown.Core.Download;

/// <summary>
/// 下载链路向调用方回吐进度的出口。取代把 serve 的可变 DownloadTask 一路透传到
/// 下载/混流层的做法：下层只认这几个回调，不再认识 serve 的任务模型，依赖恢复单向
/// （Serve → Pipeline → Media → Download，无回指）。CLI 直接传 default，全部回调为 null。
/// </summary>
public readonly record struct PipelineSink(
    Action<VInfo>? Meta,
    Action<string>? Saved,
    Action<double, long>? Sample,
    // 主媒体下载窗口信号（true=开始下载音视频轨，false=下载结束进入混流等阶段）。
    // CLI 进度条据此只在明确下载文件时显示；serve / GUI 不消费，传 null
    Action<bool>? Downloading);
