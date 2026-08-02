using System.IO;
using System.Threading.Tasks;
using BBDown;

namespace BBDown.Tests;

public class CredentialStoreTests
{
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
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task LoadAll_PrefersCliOverFile( )
    {
        var dir = Path.Combine(Path.GetTempPath( ), "bbdown_cred_" + Path.GetRandomFileName( ));
        Directory.CreateDirectory(dir);
        try
        {
            await CredentialStore.SaveTvToken("fromfile", null, dir);
            var (cookie, token) = CredentialStore.LoadAll("cliCookie", "access_token=cli", false, true, dir);
            Assert.Equal("cliCookie", cookie);
            Assert.Equal("cli", token);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task WebAndTvMergeIntoSingleFile( )
    {
        var dir = Path.Combine(Path.GetTempPath( ), "bbdown_cred_" + Path.GetRandomFileName( ));
        Directory.CreateDirectory(dir);
        try
        {
            // 先存 Web
            await CredentialStore.SaveWebCookie("web-cookie", dir, "rt", 1700000000);
            Assert.Equal("web-cookie", CredentialStore.LoadWebCookie(dir));
            Assert.Equal("rt", CredentialStore.LoadWebCredential(dir).refreshToken);
            Assert.Equal("", CredentialStore.LoadTvToken(dir));

            // 再存 TV，应合并进同一文件、保留 Web 字段
            await CredentialStore.SaveTvToken("tv-tok", 1700000001, dir);
            Assert.Equal("web-cookie", CredentialStore.LoadWebCookie(dir));
            Assert.Equal("tv-tok", CredentialStore.LoadTvToken(dir));

            // 损坏 / 旧格式文件视为无效
            await File.WriteAllTextAsync(Path.Combine(dir, "BBDown.data"), "access_token=legacy", TestContext.Current.CancellationToken);
            Assert.Equal("", CredentialStore.LoadWebCookie(dir));
            Assert.Equal("", CredentialStore.LoadTvToken(dir));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task LoadAll_ReadsFileWhenCliEmpty( )
    {
        var dir = Path.Combine(Path.GetTempPath( ), "bbdown_cred_" + Path.GetRandomFileName( ));
        Directory.CreateDirectory(dir);
        try
        {
            await CredentialStore.SaveWebCookie("filecookie", dir);
            var (cookie, token) = CredentialStore.LoadAll(null, null, false, false, dir);
            Assert.Equal("filecookie", cookie);
            Assert.Equal("", token);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task LoadAll_TvTokenGatedByUseTvApi( )
    {
        var dir = Path.Combine(Path.GetTempPath( ), "bbdown_cred_" + Path.GetRandomFileName( ));
        Directory.CreateDirectory(dir);
        try
        {
            await CredentialStore.SaveTvToken("fromfile", dir: dir);
            var (_, token) = CredentialStore.LoadAll(null, null, false, false, dir);
            Assert.Equal("", token);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}
