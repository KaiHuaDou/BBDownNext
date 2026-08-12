using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BBDown.DRM;

/// <summary>
/// --drm-key 条目集合：<c>kid:key</c> 或纯 <c>key</c>（后者为全局默认 key，用于未命中 KID 的轨）。
/// key/kid 接受 32 位 hex 或 base64(base64url) 两种编码，统一规范化为小写 hex。
/// </summary>
internal sealed class DrmKeySource
{
    private readonly Dictionary<string, string> _keys = new(StringComparer.OrdinalIgnoreCase);
    private readonly string? _defaultKey;

    public DrmKeySource(IEnumerable<string> entries)
    {
        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry))
            {
                continue;
            }

            var colon = entry.IndexOf(':');
            try
            {
                if (colon > 0)
                {
                    _keys[ToHex(entry[..colon])] = ToHex(entry[(colon + 1)..]);
                }
                else
                {
                    _defaultKey = ToHex(entry);
                }
            }
            catch (FormatException)
            {
                Console.WriteLine($"无效的密钥条目：{RedactEntry(entry)}，已忽略（key 为 16 字节，可传 32 位 hex 或 base64）");
            }
        }
    }

    public bool HasKeys => _keys.Count != 0 || _defaultKey != null;

    /// <summary>按 KID 查找 key；KID 为空或未命中时回落全局默认 key。</summary>
    public string? TryGetKey(string? kid)
    {
        if (kid != null && _keys.TryGetValue(kid, out var key))
        {
            return key;
        }

        return _defaultKey;
    }

    // key 属凭据，告警里不落明文；kid 非秘密，可以留
    private static string RedactEntry(string entry)
    {
        var colon = entry.IndexOf(':');
        return colon > 0 ? $"{entry[..colon]}:[redacted]" : "[redacted]";
    }

    public static string ToHex(string value)
    {
        if (value.Length == 32 && value.All(Uri.IsHexDigit))
        {
            return value.ToLowerInvariant( );
        }

        var padding = new string('=', (4 - value.Length % 4) % 4);
        var bytes = Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/') + padding);
        if (bytes.Length != 16)
        {
            throw new FormatException("key 长度必须为 16 字节");
        }

        return Convert.ToHexString(bytes).ToLowerInvariant( );
    }
}
