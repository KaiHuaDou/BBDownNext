using System.IO;
using System.Threading.Tasks;

namespace BBDown.Core.Tests;

public class CredentialStoreTests
{
    // 唯一保留的真实落盘往返：锁定「序列化 → 写入 → 读回」整链，其余用例均为纯函数测试
    [Fact]
    public async Task SaveAndLoadWebCookie_RoundTrips( )
    {
        var dir = Path.Combine(Path.GetTempPath( ), "bbdown_cred_" + Path.GetRandomFileName( ));
        Directory.CreateDirectory(dir);
        try
        {
            Assert.Equal("", CredentialStore.LoadWebCookie(dir));
            await CredentialStore.SaveWebCookie("SESSDATA=xxx", dir);
            Assert.Equal("SESSDATA=xxx", CredentialStore.LoadWebCookie(dir));
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, true);
            }
        }
    }

    // 用户从网页/终端粘贴凭据时带入的首尾空白与换行符必须被剥离，否则认证会静默失败
    [Fact]
    public void LoadWebCookie_TrimsSurroundingWhitespace( )
    {
        var raw = "  \r\n SESSDATA=abc \t\n ";
        var json = "{\"cookie\":" + System.Text.Json.JsonSerializer.Serialize(raw) + "}";

        Assert.Equal("SESSDATA=abc", CredentialStore.ParseCredentialJson(json).Cookie);
    }

    // 损坏 / 旧格式（access_token= 前缀）文件一律视为无效，不污染新模型
    [Fact]
    public void ParseCredentialJson_RejectsLegacyFormat( )
    {
        Assert.Null(CredentialStore.ParseCredentialJson("access_token=legacy").Cookie);
        Assert.Null(CredentialStore.ParseCredentialJson("access_token=legacy").TvAccessToken);
    }

    // LoadAll 有意不剥离 access_token= 前缀：调用方须传入纯令牌（前缀剥离已移除）
    [Fact]
    public void LoadAll_PrefersCliOverFile( )
    {
        var file = new CredentialStore.Credential("filecookie", null, null, null, null, null, null);

        var (cookie, token) = CredentialStore.Resolve("cliCookie", "cli", ApiType.App, file);

        Assert.Equal("cliCookie", cookie);
        Assert.Equal("cli", token);
    }

    [Fact]
    public void LoadAll_ReadsFileWhenCliEmpty( )
    {
        var file = new CredentialStore.Credential("filecookie", null, null, null, null, null, null);

        var (cookie, token) = CredentialStore.Resolve(null, null, ApiType.Web, file);

        Assert.Equal("filecookie", cookie);
        Assert.Equal("", token);
    }

    // TV token 仅在 TV 模式下回退，Web 模式不读
    [Fact]
    public void LoadAll_TvTokenGatedByApi( )
    {
        var file = new CredentialStore.Credential(null, null, null, "fromfile", null, null, null);

        var (_, webToken) = CredentialStore.Resolve(null, null, ApiType.Web, file);
        Assert.Equal("", webToken);

        var (_, tvToken) = CredentialStore.Resolve(null, null, ApiType.Tv, file);
        Assert.Equal("fromfile", tvToken);
    }

    // Web 与 TV 合并进同一 JSON 对象：先存 Web 再存 TV，Web 字段保留
    [Fact]
    public void WebAndTvMergeIntoSingleFile( )
    {
        var web = new CredentialStore.Credential("web-cookie", null, 1700000000, null, null, null, null);
        Assert.Equal("web-cookie", web.Cookie);
        Assert.Null(web.TvAccessToken);

        var json = "{\"cookie\":\"web-cookie\",\"tv_access_token\":\"tv-tok\",\"tv_ts\":1700000001}";
        var back = CredentialStore.ParseCredentialJson(json);

        Assert.Equal("web-cookie", back.Cookie);
        Assert.Equal("tv-tok", back.TvAccessToken);
    }
}
