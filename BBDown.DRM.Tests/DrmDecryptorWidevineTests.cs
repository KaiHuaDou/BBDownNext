using System.Threading.Tasks;

namespace BBDown.DRM.Tests;

public class DrmDecryptorWidevineTests
{
    // widevine 通道取钥失败（无 wvd）→ Unsupported
    [Fact]
    public async Task DecryptAsync_WidevineNoWvd_Unsupported( )
    {
        var keys = new DrmKeySource([]);

        var result = await DrmDecryptor.DecryptAsync("widevine", null, "AAAA", "a.m4s", "a.dec.mp4", keys, "ffmpeg", null, TestContext.Current.CancellationToken);

        Assert.Equal(DrmResult.Unsupported, result);
    }

    // widevine 通道无 pssh → Unsupported
    [Fact]
    public async Task DecryptAsync_WidevineNoPssh_Unsupported( )
    {
        var keys = new DrmKeySource([]);

        var result = await DrmDecryptor.DecryptAsync("widevine", null, null, "a.m4s", "a.dec.mp4", keys, "ffmpeg", "device.wvd", TestContext.Current.CancellationToken);

        Assert.Equal(DrmResult.Unsupported, result);
    }

    // bili_drm 通道无 key → KeyMissing
    [Fact]
    public async Task DecryptAsync_BiliDrmNoKey_KeyMissing( )
    {
        var keys = new DrmKeySource([]);

        var result = await DrmDecryptor.DecryptAsync("bili_drm", "uri:bili://d8f66b93db284984b4e7fc50d71278ff", null, "a.m4s", "a.dec.mp4", keys, "ffmpeg", null, TestContext.Current.CancellationToken);

        Assert.Equal(DrmResult.KeyMissing, result);
    }
}
