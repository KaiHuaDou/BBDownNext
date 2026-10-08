using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;

namespace BBDown.Core.Util;

// debug 日志脱敏：凭据不落明文（与 DownloadOptions.WithSecretsRedacted 同一安全意图）
public static partial class Redactor
{
    private static readonly HashSet<string> SecretHeaderNames = ["Cookie", "Set-Cookie", "Authorization"];

    // 头里凭据字段只打印字段名，值打码
    public static string Headers(HttpHeaders? headers)
    {
        if (headers is null)
        {
            return "";
        }

        var parts = new List<string>( );
        foreach (var header in headers)
        {
            var value = SecretHeaderNames.Contains(header.Key, StringComparer.OrdinalIgnoreCase)
                ? "[redacted]"
                : string.Join(", ", header.Value);
            parts.Add($"{header.Key}: {value}");
        }

        return string.Join("; ", parts);
    }

    // 同 Headers(HttpHeaders)，但接收已构造好的字典（如 App 端 gRPC 请求头），同样对凭据键打码
    public static string Headers(IReadOnlyDictionary<string, string>? headers)
    {
        if (headers is null)
        {
            return "";
        }

        var parts = new List<string>(headers.Count);
        foreach (var header in headers)
        {
            var value = SecretHeaderNames.Contains(header.Key, StringComparer.OrdinalIgnoreCase)
                ? "[redacted]"
                : header.Value;
            parts.Add($"{header.Key}: {value}");
        }

        return string.Join("; ", parts);
    }

    // 自由文本（URL / 响应体）里的凭据键值对打码。access_key 是 TV / APP 通道的 query 凭据参数名
    // 与 access_token 是两个不同的键，漏掉任何一个都会让令牌随 debug 日志写入
    [GeneratedRegex(@"(SESSDATA|bili_jct|access_key|access_token|refresh_token|csrf)(""?:|"":\s*""?|=)([^&\s""'<>,]+)")]
    private static partial Regex SecretTextRegex( );

    public static string Text(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        return SecretTextRegex( ).Replace(text, m => $"{m.Groups[1].Value}{m.Groups[2].Value}[redacted]");
    }
}
