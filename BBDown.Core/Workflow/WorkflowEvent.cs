using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace BBDown.Core.Workflow;

/// <summary>
/// 工作流通信事件基类：serve / CLI / GUI 三形态统一的消息、进度与交互管道。
/// 判别联合（同 ResourceId）：消费端 switch 分发，缺分支编译报错。
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(MessageEvent), "message")]
[JsonDerivedType(typeof(ProgressRangeStartEvent), "progressStart")]
[JsonDerivedType(typeof(ProgressSampleEvent), "progressSample")]
[JsonDerivedType(typeof(ProgressRangeEndEvent), "progressEnd")]
[JsonDerivedType(typeof(OptionRequestEvent), "optionRequest")]
public abstract record WorkflowEvent;

/// <summary>
/// 纯文本消息（用户可见）。
/// </summary>
public sealed record MessageEvent(string Text, DateTimeOffset Time) : WorkflowEvent;

/// <summary>
/// 进度阶段开始（低频语义事件）：宿主据此显示进度 UI。Scope 为任务标识（与 MessageBus 同源）。
/// </summary>
public sealed record ProgressRangeStartEvent(string Scope, string StageName) : WorkflowEvent;

/// <summary>
/// 阶段内进度样本（高频、快照语义：携带当前累计值，宿主可丢弃中间帧只渲染最新）。
/// Ratio 为当前阶段内完成比例 0-1，多分 P / 多流总进度由宿主聚合。
/// </summary>
public sealed record ProgressSampleEvent(string Scope, double Ratio, long TotalBytes, double Speed, string? Detail = null) : WorkflowEvent;

/// <summary>
/// 进度阶段结束（低频语义事件）：宿主据此隐藏进度 UI。
/// </summary>
public sealed record ProgressRangeEndEvent(string Scope) : WorkflowEvent;

/// <summary>
/// 选项请求：工作流在此挂起，外部经 RequestId 应答；Deadline 为服务端超时时刻，超时按调用方策略处理。
/// DefaultOptionId 为宿主无法解析输入时的回落选项（CLI 回车 / 非法输入），须属于 Options。
/// </summary>
public sealed record OptionRequestEvent(Guid RequestId, string Scope, string Prompt, IReadOnlyList<AskOption> Options, DateTimeOffset Deadline, string? DefaultOptionId = null) : WorkflowEvent;
