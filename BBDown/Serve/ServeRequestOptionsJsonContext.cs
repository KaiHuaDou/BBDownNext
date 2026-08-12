using System.Text.Json.Serialization;

using BBDown.Core;
using BBDown.Core.Download;

namespace BBDown.Serve;

/// <summary>
/// serve 请求契约的序列化上下文：只服务 <see cref="ServeRequestOptions"/> 与 <see cref="DownloadRequest"/> 之间的 round-trip 转换，
/// 不进 public API 面（契约类型保持 internal）。
/// </summary>
[JsonSerializable(typeof(ServeRequestOptions))]
[JsonSerializable(typeof(DownloadRequest))]
internal partial class ServeRequestOptionsJsonContext : JsonSerializerContext;
