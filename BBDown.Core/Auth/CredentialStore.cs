using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using static BBDown.Core.Logger;

namespace BBDown.Core.Auth;

/// <summary>
/// 凭据读写收口：WEB cookie、TV token、APP token 三类凭据全部合并进单一文件
/// <c>BBDown.data</c> 的同一个 JSON 对象，CLI 与 serve 模式共用，避免多份文件不一致
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
/// 各类凭据独立写入：每次保存只更新对应字段并合并保留其余字段，互不影响
/// </summary>
public static class CredentialStore
{
    private const string DataFile = "BBDown.data";

    // 保存走「读文件 → with 修改 → 写回」序列：serve 并发任务各自触发保存时
    // 无锁会让两个写者基于同一份旧快照合并，后写者覆盖先写者的字段更新（丢凭据）
    // 序列内有 await，lock 语句不可用，用信号量互斥；文件写入本身由 tmp + Move 保证原子
    private static readonly SemaphoreSlim saveGate = new(1, 1);

    private static readonly Credential Empty = new(null, null, null, null, null, null, null);

    // 单一合并凭据模型；字段缺失即为 null。属性名经 JsonPropertyName 映射为磁盘上的 snake_case
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
    /// 读取 Web 凭据三元组：cookie、refresh_token（可能为空）、签发时间戳（可能为空）
    /// 文件缺失或非合法 JSON 时返回 ("", null, null)
    /// </summary>
    public static (string cookie, string? refreshToken, long? issueTs) LoadWebCredential(string? dir = null)
    {
        var c = LoadCredential(dir);
        return (c.Cookie ?? "", c.RefreshToken, c.Ts);
    }

    // ── 保存：每次只更新对应字段，合并保留其它字段（核心：单文件合并，互不覆盖）────

    public static Task SaveWebCookie(string cookie, string? dir = null, string? refreshToken = null, long? issueTs = null)
    {
        return SaveCredential(dir, c => c with { Cookie = cookie, RefreshToken = refreshToken, Ts = issueTs });
    }

    public static Task SaveTvToken(string accessToken, long? issueTs = null, string? dir = null)
    {
        return SaveCredential(dir, c => c with { TvAccessToken = accessToken, TvTs = issueTs });
    }

    public static Task SaveAppToken(string accessToken, long? issueTs = null, string? dir = null)
    {
        return SaveCredential(dir, c => c with { AppAccessToken = accessToken, AppTs = issueTs });
    }

    // 三个 Save 共用的读改写收口，序列互斥见 saveGate 注释
    private static async Task SaveCredential(string? dir, Func<Credential, Credential> update)
    {
        await saveGate.WaitAsync( );
        try
        {
            await WriteCredential(dir, update(LoadCredential(dir)));
        }
        finally
        {
            saveGate.Release( );
        }
    }

    // ── JSON 序列化 / 反序列化（源生成器，AOT 安全）────────────────────────────

    /// <summary>读取完整凭据快照（含签发时间戳）。<see cref="LoadWebCredential"/> 等便捷方法只取所需字段
    /// 需要跨通道一次性取齐时走本方法，避免为拿时间戳重复读盘。</summary>
    internal static Credential LoadCredential(string? dir = null)
    {
        return ParseCredentialJson(TryRead(dir, DataFile));
    }

    // 解析凭据 JSON + 去首尾空白 + 非法 / 空 → Empty。纯函数以便单测锁定旧格式拒绝与裁剪行为
    internal static Credential ParseCredentialJson(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Empty;
        }

        try
        {
            return TrimCredential(JsonSerializer.Deserialize(raw, CredentialJsonContext.Default.Credential) ?? Empty);
        }
        catch
        {
            // 非 JSON / 损坏文件一律视为无效（不兼容旧格式）
            return Empty;
        }
    }

    // 用户从网页/终端粘贴的凭据常带入首尾空白与换行符，留着会让认证静默失败
    private static Credential TrimCredential(Credential c)
    {
        return c with
        {
            Cookie = c.Cookie?.Trim( ),
            RefreshToken = c.RefreshToken?.Trim( ),
            TvAccessToken = c.TvAccessToken?.Trim( ),
            AppAccessToken = c.AppAccessToken?.Trim( ),
        };
    }

    private static async Task WriteCredential(string? dir, Credential c)
    {
        var path = Path.Combine(dir ?? AppEnv.AppDir, DataFile);
        var tmp = path + ".tmp";
        await File.WriteAllTextAsync(tmp, JsonSerializer.Serialize(c, CredentialJsonContext.Default.Credential));
        File.Move(tmp, path, overwrite: true);
        HardenFilePermissions(path);
    }

    // 凭据明文写入，尽量收紧文件权限：类 Unix 系统设为 600（仅 owner 可读写）；Windows 暂不收紧以避免误锁自身
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
    /// 合并命令行传入与本地文件的凭据：命令行优先；缺失时回退到对应类型的本地文件
    /// </summary>
    public static (string cookie, string token) LoadAll(
        string? cliCookie, string? cliToken, ApiType api, string? dir = null)
    {
        var file = LoadCredential(dir);
        var result = Resolve(cliCookie, cliToken, api, file);
        // 恢复本地凭据加载提示（Resolve 为纯函数不输出）：来源条件与 Resolve 分支对应
        if (string.IsNullOrEmpty(cliCookie) && !string.IsNullOrEmpty(file.Cookie))
        {
            Log("加载本地 cookie...");
        }

        if (string.IsNullOrEmpty(cliToken) && api == ApiType.Tv && !string.IsNullOrEmpty(file.TvAccessToken))
        {
            Log("加载本地 token...");
        }
        else if (string.IsNullOrEmpty(cliToken) && api == ApiType.App && !string.IsNullOrEmpty(file.AppAccessToken))
        {
            Log("加载本地 token...");
        }

        return result;
    }

    // CLI 优先，缺失回退本地文件；TV / APP 按 api 类型门控。纯函数以便单测锁定合并优先级
    internal static (string cookie, string token) Resolve(
        string? cliCookie, string? cliToken, ApiType api, Credential file)
    {
        var cookie = cliCookie ?? "";
        var token = cliToken ?? "";

        if (string.IsNullOrEmpty(cookie) && (file.Cookie?.Length ?? 0) > 0)
        {
            cookie = file.Cookie!;
        }

        if (string.IsNullOrEmpty(token) && api == ApiType.Tv && (file.TvAccessToken?.Length ?? 0) > 0)
        {
            token = file.TvAccessToken!;
        }

        if (string.IsNullOrEmpty(token) && api == ApiType.App && (file.AppAccessToken?.Length ?? 0) > 0)
        {
            token = file.AppAccessToken!;
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
