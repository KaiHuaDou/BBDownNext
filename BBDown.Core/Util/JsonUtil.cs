using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace BBDown.Core.Util;

// 结构化的 JSON 判定，替代在已解析过的 JSON 字符串上再做全文 Contains
public static class JsonUtil
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

    // 逐级下钻取数组; 路径中断或末端不是数组时返回 null, 让调用方区分"没有这段"与"有但为空"
    public static List<JsonElement>? ArrayAtPath(JsonElement parent, params string[] path)
    {
        var node = parent;
        foreach (var name in path)
        {
            if (node.ValueKind != JsonValueKind.Object || !node.TryGetProperty(name, out var child))
            {
                return null;
            }

            node = child;
        }

        return node.ValueKind == JsonValueKind.Array ? [.. node.EnumerateArray( )] : null;
    }

    // dimension 字段在部分条目上缺失, 取不到时给空串而不是抛 KeyNotFoundException
    public static string ReadDimension(JsonElement parent)
    {
        if (parent.ValueKind != JsonValueKind.Object
            || !parent.TryGetProperty("dimension", out var dimension)
            || dimension.ValueKind != JsonValueKind.Object
            || !dimension.TryGetProperty("width", out var width)
            || !dimension.TryGetProperty("height", out var height))
        {
            return "";
        }

        return $"{width}x{height}";
    }

    // 番剧(pgc season) 的 duration 以毫秒计, 而 UGC pages 与课程 pugv 的同名字段已是秒; 调用方统一按秒存放故在此换算。
    // TryGetInt64 遇到非 Number 的 ValueKind 会抛而不是返回 false, 需先判类型; 取不到时给 0 而不是抛
    public static int ReadDurationSeconds(JsonElement parent)
    {
        return parent.ValueKind == JsonValueKind.Object
               && parent.TryGetProperty("duration", out var duration)
               && duration.ValueKind == JsonValueKind.Number
               && duration.TryGetInt64(out var milliseconds)
            ? (int) Math.Round(milliseconds / 1000.0)
            : 0;
    }

    // B 站接口统一的外层 code/message
    public static (int Code, string Message) ReadApiError(JsonElement root)
    {
        var code = root.ValueKind == JsonValueKind.Object
                   && root.TryGetProperty("code", out var codeElem)
                   && codeElem.ValueKind == JsonValueKind.Number
            ? codeElem.GetInt32( )
            : 0;
        var message = root.ValueKind == JsonValueKind.Object
                      && root.TryGetProperty("message", out var msgElem)
                      && msgElem.ValueKind == JsonValueKind.String
            ? msgElem.GetString( )!
            : "未知错误";
        return (code, message);
    }

    // 接口失败时 data 缺失或为 null，直接 GetProperty("data") 只会抛出不含 code/message 的 KeyNotFoundException
    public static JsonElement GetApiData(JsonElement root, string label, string dataName = "data")
    {
        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty(dataName, out var data)
            && data.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
        {
            return data;
        }

        var (code, message) = ReadApiError(root);
        throw new InvalidOperationException($"获取{label}失败(code={code})：{message}");
    }

    // 番剧接口用 episodes[].Id 标识分集。原实现把整棵子树 ToString 后找 "/ep{id}"，
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
