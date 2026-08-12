using System.Text.Json.Serialization;

namespace BBDown.DRM;

// AOT 源生成器序列化上下文：请求 JSON 与密钥配置文件的反序列化必须走这里，
// 运行时反射序列化在 AOT 发布下不可用（会被裁剪并抛 NotSupportedException）。
// 命名策略与主程序序列化端一致（属性名原样 PascalCase，大小写敏感）。
[JsonSerializable(typeof(PostProcessRequest))]
[JsonSerializable(typeof(KeyFile))]
internal partial class DrmJsonContext : JsonSerializerContext;
