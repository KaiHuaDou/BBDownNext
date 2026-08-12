using System.Threading.Tasks;

namespace BBDown.DRM.Tests;

public class DrmDecryptorWidevineTests
{
    // widevine 通道缺 wvd → DeviceMissing
    [Fact]
    public async Task DecryptAsync_WidevineNoWvd_DeviceMissing( )
    {
        var keys = new DrmKeySource([]);

        var result = await DrmDecryptor.DecryptAsync("widevine", null, "AAAA", "a.m4s", "a.dec.mp4", keys, "ffmpeg", null, TestContext.Current.CancellationToken);

        Assert.Equal(DrmResult.DeviceMissing, result);
    }

    // widevine 通道缺 pssh → DeviceMissing
    [Fact]
    public async Task DecryptAsync_WidevineNoPssh_DeviceMissing( )
    {
        var keys = new DrmKeySource([]);

        var result = await DrmDecryptor.DecryptAsync("widevine", null, null, "a.m4s", "a.dec.mp4", keys, "ffmpeg", "device.wvd", TestContext.Current.CancellationToken);

        Assert.Equal(DrmResult.DeviceMissing, result);
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
