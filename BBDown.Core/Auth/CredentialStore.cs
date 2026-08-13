using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

using static BBDown.Core.Logger;

namespace BBDown.Core.Auth;

/// <summary>
/// 凭据读写收口：WEB cookie、TV token、APP token 三类凭据全部合并进单一文件
/// <c>BBDown.data</c> 的同一个 JSON 对象，CLI 与 serve 模式共用，避免多份文件不一致。
///
/// <code>
/// {
///   "cookie": "...",          // WEB 登录 Cookie（未登录为 null）
///   "refresh_token": "...",   // WEB 续期令牌（未登录为 null）
///   "ts": 1700000000,         // WEB 凭据签发时间戳（未登录为 null）
///   "tv_access_token": "...", // TV 登录令牌（未登录为 null）
///   "tv_ts": 1700000000,      // TV 凭据签发时间戳（未登录为 null）
///   "app_access_token": "...",// APP 登录令牌（未登录为 null）
///   "app_ts": 1700000000      // APP 凭据签发时间戳（未登录为 null）
/// }
/// </code>
/// 各类凭据独立落盘：每次保存只更新对应字段并合并保留其余字段，互不影响。
/// </summary>
public static class CredentialStore
{
    private const string DataFile = "BBDown.data";

    private static readonly Credential Empty = new(null, null, null, null, null, null, null);

    // 单一合并凭据模型；字段缺失即为 null。属性名经 JsonPropertyName 映射为磁盘上的 snake_case。
    internal sealed record Credential(
        [property: JsonPropertyName("cookie")] string? Cookie,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: JsonPropertyName("ts")] long? Ts,
        [property: JsonPropertyName("tv_access_token")] string? TvAccessToken,
        [property: JsonPropertyName("tv_ts")] long? TvTs,
        [property: JsonPropertyName("app_access_token")] string? AppAccessToken,
        [property: JsonPropertyName("app_ts")] long? AppTs
    );

    public static string LoadWebCookie(string? dir = null)
    {
        return LoadCredential(dir).Cookie ?? "";
    }

    public static string LoadTvToken(string? dir = null)
    {
        return LoadCredential(dir).TvAccessToken ?? "";
    }

    public static string LoadAppToken(string? dir = null)
    {
        return LoadCredential(dir).AppAccessToken ?? "";
    }

    /// <summary>
    /// 读取 Web 凭据三元组：cookie、refresh_token（可能为空）、签发时间戳（可能为空）。
    /// 文件缺失或非合法 JSON 时返回 ("", null, null)。
    /// </summary>
    public static (string cookie, string? refreshToken, long? issueTs) LoadWebCredential(string? dir = null)
    {
        var c = LoadCredential(dir);
        return (c.Cookie ?? "", c.RefreshToken, c.Ts);
    }

    // ── 保存：每次只更新对应字段，合并保留其它字段（核心：单文件合并，互不覆盖）────

    public static async Task SaveWebCookie(string cookie, string? dir = null, string? refreshToken = null, long? issueTs = null)
    {
        var c = LoadCredential(dir) with { Cookie = cookie, RefreshToken = refreshToken, Ts = issueTs };
        await WriteCredential(dir, c);
    }

    public static async Task SaveTvToken(string accessToken, long? issueTs = null, string? dir = null)
    {
        var c = LoadCredential(dir) with { TvAccessToken = accessToken, TvTs = issueTs };
        await WriteCredential(dir, c);
    }

    public static async Task SaveAppToken(string accessToken, long? issueTs = null, string? dir = null)
    {
        var c = LoadCredential(dir) with { AppAccessToken = accessToken, AppTs = issueTs };
        await WriteCredential(dir, c);
    }

    // ── JSON 序列化 / 反序列化（源生成器，AOT 安全）────────────────────────────

    private static Credential LoadCredential(string? dir)
    {
        var raw = TryRead(dir, DataFile);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Empty;
        }

        try
        {
            return JsonSerializer.Deserialize(raw, CredentialJsonContext.Default.Credential) ?? Empty;
        }
        catch
        {
            // 非 JSON / 损坏文件一律视为无效（不兼容旧格式）
            return Empty;
        }
    }

    private static async Task WriteCredential(string? dir, Credential c)
    {
        var path = Path.Combine(dir ?? AppEnv.AppDir, DataFile);
        var tmp = path + ".tmp";
        await File.WriteAllTextAsync(tmp, JsonSerializer.Serialize(c, CredentialJsonContext.Default.Credential));
        File.Move(tmp, path, overwrite: true);
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
        string? cliCookie, string? cliToken, ApiType api, string? dir = null)
    {
        var cookie = cliCookie ?? "";
        var token = cliToken ?? "";

        if (string.IsNullOrEmpty(cookie) && LoadWebCookie(dir) is { Length: > 0 } localCookie)
        {
            Log("加载本地 cookie...");
            cookie = localCookie;
        }

        if (string.IsNullOrEmpty(token) && api == ApiType.Tv && LoadTvToken(dir) is { Length: > 0 } tvToken)
        {
            Log("加载本地 token...");
            token = tvToken;
        }

        if (string.IsNullOrEmpty(token) && api == ApiType.App && LoadAppToken(dir) is { Length: > 0 } appToken)
        {
            Log("加载本地 token...");
            token = appToken;
        }

        return (cookie, token);
    }

    private static string TryRead(string? dir, string name)
    {
        var path = Path.Combine(dir ?? AppEnv.AppDir, name);
        return File.Exists(path) ? File.ReadAllText(path) : "";
    }
}

[JsonSerializable(typeof(CredentialStore.Credential))]
internal partial class CredentialJsonContext : JsonSerializerContext;
