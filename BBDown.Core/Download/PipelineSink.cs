using System;

using BBDown.Core.Entity;

namespace BBDown.Core.Download;

/// <summary>
/// 下载链路向调用方回吐元数据与产物的出口。进度不再走本回调——统一经
/// <see cref="BBDown.Core.Workflow.ProgressBus"/>（阶段化），展示由宿主决定。
/// CLI 直接传 default，全部回调为 null。
/// </summary>
public readonly record struct PipelineSink(
    Action<VInfo>? Meta,
    Action<string>? Saved);
