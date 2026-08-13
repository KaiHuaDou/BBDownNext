using System.Text.Json.Serialization;

namespace BBDown.DRM;

// AOT 源生成器序列化上下文：请求 JSON 与密钥配置文件的反序列化必须走这里，
// 运行时反射序列化在 AOT 发布下不可用（会被裁剪并抛 NotSupportedException）。
// 大小写不敏感：配置文件允许 keys / Keys 混写，避免用户按小写示例配置时静默失效。
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(PostProcessRequest))]
[JsonSerializable(typeof(KeyFile))]
internal partial class DrmJsonContext : JsonSerializerContext;
