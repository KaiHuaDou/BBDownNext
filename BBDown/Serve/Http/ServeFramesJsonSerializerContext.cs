using System.Text.Json.Serialization;

using BBDown.Core.Workflow;

namespace BBDown.Serve.Http;

/// <summary>
/// WebSocket 帧与 WorkflowEvent 的序列化上下文：帧信封（EventFrame / ClientFrame）与进度快照自包含声明，
/// 不依赖根命名空间的 AppJsonSerializerContext，避免 Http → Serve 根的回指。
/// </summary>
[JsonSerializable(typeof(EventFrame))]
[JsonSerializable(typeof(ClientFrame))]
[JsonSerializable(typeof(WorkflowEvent))]
[JsonSerializable(typeof(MessageEvent))]
[JsonSerializable(typeof(ProgressRangeStartEvent))]
[JsonSerializable(typeof(ProgressSampleEvent))]
[JsonSerializable(typeof(ProgressRangeEndEvent))]
[JsonSerializable(typeof(OptionRequestEvent))]
[JsonSerializable(typeof(AskOption))]
internal partial class ServeFramesJsonSerializerContext : JsonSerializerContext;