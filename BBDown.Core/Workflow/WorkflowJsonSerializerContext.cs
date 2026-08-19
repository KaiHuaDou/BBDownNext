using System.Text.Json.Serialization;

namespace BBDown.Core.Workflow;

/// <summary>
/// WorkflowEvent 判别联合的序列化上下文：多态判别符（type）经源生成落地，serve WebSocket 帧与 CLI 共用。
/// </summary>
[JsonSerializable(typeof(WorkflowEvent))]
[JsonSerializable(typeof(MessageEvent))]
[JsonSerializable(typeof(ProgressRangeStartEvent))]
[JsonSerializable(typeof(ProgressSampleEvent))]
[JsonSerializable(typeof(ProgressRangeEndEvent))]
[JsonSerializable(typeof(OptionRequestEvent))]
public partial class WorkflowJsonSerializerContext : JsonSerializerContext;
