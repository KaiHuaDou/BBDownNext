using System;
using System.IO;
using System.Threading.Tasks;

using static BBDown.Core.Logger;

namespace BBDown;

/// <summary>
/// 凭据读写收口：WEB cookie、TV token、APP token 三类凭据的本地文件读写，
/// 以及命令行与本地文件的合并加载。CLI 与 serve 模式共用，避免两份不一致的装配逻辑。
/// </summary>
internal static class CredentialStore
{
    private const string WebFile = "BBDown.data";
    private const string TvFile = "BBDownTV.data";
    private const string AppFile = "BBDownApp.data";

    public static string LoadWebCookie(string? dir = null)
        => TryRead(dir, WebFile);

    public static string LoadTvToken(string? dir = null)
        => TryRead(dir, TvFile);

    public static string LoadAppToken(string? dir = null)
        => TryRead(dir, AppFile);

    public static async Task SaveWebCookie(string content, string? dir = null)
        => await File.WriteAllTextAsync(Path.Combine(dir ?? Program.APP_DIR, WebFile), content);

    public static async Task SaveTvToken(string content, string? dir = null)
        => await File.WriteAllTextAsync(Path.Combine(dir ?? Program.APP_DIR, TvFile), content);

    public static async Task SaveAppToken(string content, string? dir = null)
        => await File.WriteAllTextAsync(Path.Combine(dir ?? Program.APP_DIR, AppFile), content);

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

    private static string TryRead(string? dir, string name)
    {
        var path = Path.Combine(dir ?? Program.APP_DIR, name);
        return File.Exists(path) ? File.ReadAllText(path) : "";
    }
}
