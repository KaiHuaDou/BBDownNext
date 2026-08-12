using System;

using BBDown.Core.Entity;

namespace BBDown.Core.Download;

/// <summary>
/// 下载链路向调用方回吐进度的出口。取代把 serve 的可变 DownloadTask 一路透传到
/// 下载/混流层的做法：下层只认这三个回调，不再认识 serve 的任务模型，依赖恢复单向
/// （Serve → Pipeline → Media → Download，无回指）。CLI 直接传 default，全部回调为 null。
/// </summary>
public readonly record struct PipelineSink(
    Action<VInfo>? Meta,
    Action<string>? Saved,
    Action<double, long>? Sample);
