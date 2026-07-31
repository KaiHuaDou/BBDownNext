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
            await CredentialStore.SaveTvToken("access_token=fromfile", dir);
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
            await CredentialStore.SaveTvToken("access_token=fromfile", dir);
            var (_, token) = CredentialStore.LoadAll(null, null, false, false, dir);
            Assert.Equal("", token);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}
