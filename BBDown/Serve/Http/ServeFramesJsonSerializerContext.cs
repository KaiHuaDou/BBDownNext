using System;
using System.Text.Json.Serialization;

using BBDown.Core.Workflow;

namespace BBDown.Serve.Http;

/// <summary>
/// WebSocket 帧与 WorkflowEvent 的序列化上下文：帧信封（EventFrame / ClientFrame）与进度快照自包含声明，
/// 不依赖根命名空间的 AppJsonSerializerContext，避免 Http → Serve 根的回指。
/// </summary>
[JsonSerializable(typeof(EventFrame))]
[JsonSerializable(typeof(ClientFrame))]
[JsonSerializable(typeof(DownloadTaskSnapshot))]
[JsonSerializable(typeof(WorkflowEvent))]
[JsonSerializable(typeof(MessageEvent))]
[JsonSerializable(typeof(ProgressRangeStartEvent))]
[JsonSerializable(typeof(ProgressSampleEvent))]
[JsonSerializable(typeof(ProgressRangeEndEvent))]
[JsonSerializable(typeof(OptionRequestEvent))]
[JsonSerializable(typeof(AskOption))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal partial class ServeFramesJsonSerializerContext : JsonSerializerContext;

/// <summary>
/// 客户端 → 服务端帧：kind 为 subscribe / unsubscribe / submitChoice / ping。
/// </summary>
internal sealed record ClientFrame(string? Kind, string? TaskId, Guid? RequestId, string? Choice);

/// <summary>
/// 服务端 → 客户端帧：kind 为 event / snapshot / choiceResult / error / taskList。
/// taskList 携带全量任务列表（running + finished），由 store 结构变更时广播，使前端免轮询。
/// </summary>
internal sealed record EventFrame(
    string Kind,
    string? TaskId = null,
    WorkflowEvent? Event = null,
    ProgressSampleEvent? Snapshot = null,
    Guid? RequestId = null,
    bool Ok = false,
    string? Error = null,
    DownloadTaskSnapshot? Tasks = null);
