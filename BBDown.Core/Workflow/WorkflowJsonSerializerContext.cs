using System.Text.Json.Serialization;

namespace BBDown.Core.Workflow;

/// <summary>
/// WorkflowEvent 标记联合的序列化上下文：多态标记（type）经源生成落地，serve WebSocket 帧与 CLI 共用
/// </summary>
[JsonSerializable(typeof(WorkflowEvent))]
[JsonSerializable(typeof(MessageEvent))]
[JsonSerializable(typeof(ProgressRangeStartEvent))]
[JsonSerializable(typeof(ProgressSampleEvent))]
[JsonSerializable(typeof(ProgressRangeEndEvent))]
[JsonSerializable(typeof(OptionRequestEvent))]
[JsonSerializable(typeof(AskOption))]
public partial class WorkflowJsonSerializerContext : JsonSerializerContext;
