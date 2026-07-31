using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace BBDown.Core.Util;

// 结构化的 JSON 判定，替代在已解析过的 JSON 字符串上再做全文 Contains
internal static class JsonUtil
{
    public static bool HasObject(JsonElement parent, string name)
    {
        return parent.ValueKind == JsonValueKind.Object
               && parent.TryGetProperty(name, out var child)
               && child.ValueKind == JsonValueKind.Object;
    }

    public static bool TryGetArray(JsonElement parent, string name, out JsonElement array)
    {
        if (parent.ValueKind == JsonValueKind.Object
            && parent.TryGetProperty(name, out var child)
            && child.ValueKind == JsonValueKind.Array)
        {
            array = child;
            return true;
        }

        array = default;
        return false;
    }

    // 非数组(含 default(JsonElement))时给空序列, 免去调用方到处判 ValueKind
    public static IEnumerable<JsonElement> EnumerateArrayOrEmpty(JsonElement element)
    {
        return element.ValueKind == JsonValueKind.Array ? element.EnumerateArray( ) : [];
    }

    // 番剧接口用 episodes[].id 标识分集。原实现把整棵子树 ToString 后找 "/ep{id}"，
    // ep123 会被 ep1234 的链接误命中
    public static bool ContainsEpisode(JsonElement episodes, string epId)
    {
        return episodes.ValueKind == JsonValueKind.Array
               && episodes.EnumerateArray( ).Any(ep =>
                   ep.ValueKind == JsonValueKind.Object
                   && ep.TryGetProperty("id", out var id)
                   && id.ToString( ) == epId);
    }
}
