using System;
using Xunit.Sdk;

[assembly: RegisterXunitSerializer(typeof(BBDown.Core.Tests.ResourceIdSerializer), typeof(ResourceId))]

namespace BBDown.Core.Tests;

/// <summary>
/// <see cref="ResourceId"/> 的 theory data 序列化器：取 <see cref="ResourceIdJsonConverter.Format"/> 的规范串为唯一表示
/// 与 <see cref="ResourceId.TryParse"/> 严格对称。ResourceId 为封闭标记联合且在项目侧无法实现 IXunitSerializable
/// 故走外部序列化器注册
/// </summary>
public sealed class ResourceIdSerializer : XunitSerializer<ResourceId>
{
    public override string Serialize(ResourceId value)
    {
        return ResourceIdJsonConverter.Format(value);
    }

    public override ResourceId Deserialize(Type type, string serializedValue)
    {
        if (!ResourceId.TryParse(serializedValue, out var id))
        {
            throw new FormatException($"ResourceId 规范串无法还原：{serializedValue}");
        }

        return id;
    }
}
