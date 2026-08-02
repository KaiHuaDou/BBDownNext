using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

using static BBDown.Core.Logger;

namespace BBDown;

/// <summary>
/// 凭据读写收口：WEB cookie、TV token、APP token 三类凭据的本地文件读写，
/// 以及命令行与本地文件的合并加载。CLI 与 serve 模式共用，避免两份不一致的装配逻辑。
/// Web 凭据与 refresh_token 同存于 BBDown.data，统一为 JSON 格式
/// <c>{cookie, refresh_token, ts}</c>。不兼容旧的纯 cookie 字符串格式，旧文件一律视为无效、需重新登录。
/// </summary>
internal static class CredentialStore
{
    private const string WebFile = "BBDown.data";
    private const string TvFile = "BBDownTV.data";
    private const string AppFile = "BBDownApp.data";

    public static string LoadWebCookie(string? dir = null)
    {
        var raw = TryRead(dir, WebFile);
        return TryParseWebCredential(raw, out var cred) ? cred.cookie : "";
    }

    public static string LoadTvToken(string? dir = null)
        => TryRead(dir, TvFile);

    public static string LoadAppToken(string? dir = null)
        => TryRead(dir, AppFile);

    /// <summary>
    /// 读取 Web 凭据三元组：cookie、refresh_token（可能为空）、签发时间戳（可能为空）。
    /// 文件缺失或非合法 JSON 时返回 ("", null, null)，旧格式一律不支持。
    /// </summary>
    public static (string cookie, string? refreshToken, long? issueTs) LoadWebCredential(string? dir = null)
    {
        var raw = TryRead(dir, WebFile);
        return TryParseWebCredential(raw, out var cred) ? cred : ("", null, null);
    }

    public static async Task SaveWebCookie(string cookie, string? dir = null, string? refreshToken = null, long? issueTs = null)
    {
        var path = Path.Combine(dir ?? Program.APP_DIR, WebFile);
        // 统一写 JSON（手写而非 JsonSerializer，规避 AOT 裁剪/Trim 分析报错 IL2026/IL3050）；
        // 不再保留纯 cookie 字符串的旧格式
        var content = "{\"cookie\":" + JsonString(cookie)
            + ",\"refresh_token\":" + JsonString(refreshToken)
            + ",\"ts\":" + (issueTs?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "null")
            + "}";
        await File.WriteAllTextAsync(path, content);
        HardenFilePermissions(path);
    }

    private static string JsonString(string? s)
    {
        if (s is null)
        {
            return "null";
        }
        var sb = new StringBuilder(s.Length + 2);
        sb.Append('"');
        foreach (var c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20)
                    {
                        sb.Append("\\u").Append(((int)c).ToString("x4"));
                    }
                    else
                    {
                        sb.Append(c);
                    }
                    break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }

    public static async Task SaveTvToken(string content, string? dir = null)
    {
        var path = Path.Combine(dir ?? Program.APP_DIR, TvFile);
        await File.WriteAllTextAsync(path, content);
        HardenFilePermissions(path);
    }

    // 凭据明文落盘，尽量收紧文件权限：类 Unix 系统设为 600（仅 owner 可读写）；Windows 暂不收紧以避免误锁自身（P0-1）
    private static void HardenFilePermissions(string path)
    {
        try
        {
            if (!OperatingSystem.IsWindows( ))
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }
        catch
        {
            // 权限收紧失败不应影响凭据保存
        }
    }

    /// <summary>
    /// 合并命令行传入与本地文件的凭据：命令行优先；缺失时回退到对应类型的本地文件。
    /// </summary>
    public static (string cookie, string token) LoadAll(
        string? cliCookie, string? cliToken, bool useTvApi, bool useAppApi, string? dir = null)
    {
        var cookie = cliCookie ?? "";
        var token = cliToken?.Replace("access_token=", "") ?? "";

        if (string.IsNullOrEmpty(cookie) && LoadWebCookie(dir) is { Length: > 0 } localCookie)
        {
            Log("加载本地 cookie...");
            cookie = localCookie;
        }

        if (string.IsNullOrEmpty(token) && useTvApi && LoadTvToken(dir) is { Length: > 0 } tvToken)
        {
            Log("加载本地 token...");
            token = tvToken.Replace("access_token=", "");
        }

        if (string.IsNullOrEmpty(token) && useAppApi && LoadAppToken(dir) is { Length: > 0 } appToken)
        {
            Log("加载本地 token...");
            token = appToken.Replace("access_token=", "");
        }

        return (cookie, token);
    }

    private static bool TryParseWebCredential(string raw, out (string cookie, string? refreshToken, long? issueTs) cred)
    {
        cred = ("", null, null);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.TryGetProperty("cookie", out var cookie) && cookie.ValueKind == JsonValueKind.String)
            {
                string? refreshToken = null;
                long? issueTs = null;
                if (doc.RootElement.TryGetProperty("refresh_token", out var rt) && rt.ValueKind == JsonValueKind.String)
                {
                    refreshToken = rt.GetString( );
                }
                if (doc.RootElement.TryGetProperty("ts", out var ts) && ts.ValueKind == JsonValueKind.Number)
                {
                    issueTs = ts.GetInt64( );
                }
                cred = (cookie.GetString( )!, refreshToken, issueTs);
                return true;
            }
        }
        catch
        {
            // 非 JSON（旧格式纯 cookie 字符串）视为解析失败，回退到原始内容
        }

        return false;
    }

    private static string TryRead(string? dir, string name)
    {
        var path = Path.Combine(dir ?? Program.APP_DIR, name);
        return File.Exists(path) ? File.ReadAllText(path) : "";
    }
}
