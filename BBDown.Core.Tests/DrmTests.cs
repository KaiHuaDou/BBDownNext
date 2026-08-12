using System;
using System.Linq;
using System.Threading.Tasks;


namespace BBDown.Core.Tests;

public class DrmTests
{
    // ═══ DrmKeySource：编码与匹配 ═══

    [Fact]
    public void DrmKeySource_PureHexKey_UsedAsDefaultForAnyKid( )
    {
        var keys = new DrmKeySource(["d8f66b93db284984b4e7fc50d71278ff"]);

        Assert.Equal("d8f66b93db284984b4e7fc50d71278ff", keys.TryGetKey("any-kid"));
        Assert.Equal("d8f66b93db284984b4e7fc50d71278ff", keys.TryGetKey(null));
    }

    [Fact]
    public void DrmKeySource_Base64UrlKey_ConvertedToHex( )
    {
        // base64url 22 字符（无 padding）= 16 字节，转 32 位 hex
        var keys = new DrmKeySource(["c6xChuWnTweKvL8_j0Cm8A"]);

        var hex = keys.TryGetKey(null);
        Assert.True(hex is { Length: 32 } && hex.All(Uri.IsHexDigit));
    }

    [Fact]
    public void DrmKeySource_KidBoundKey_TakesPrecedenceOverDefault( )
    {
        var keys = new DrmKeySource(
        [
            "d8f66b93db284984b4e7fc50d71278ff:00112233445566778899aabbccddeeff",
            "aabbccddeeff00112233445566778899"
        ]);

        Assert.Equal("00112233445566778899aabbccddeeff", keys.TryGetKey("d8f66b93db284984b4e7fc50d71278ff"));
        Assert.Equal("aabbccddeeff00112233445566778899", keys.TryGetKey("unknown-kid"));
        Assert.Equal("aabbccddeeff00112233445566778899", keys.TryGetKey(null));
    }

    [Fact]
    public void DrmKeySource_KidMatchIsCaseInsensitive( )
    {
        var keys = new DrmKeySource(["D8F66B93DB284984B4E7FC50D71278FF:00112233445566778899aabbccddeeff"]);

        Assert.Equal("00112233445566778899aabbccddeeff", keys.TryGetKey("d8f66b93db284984b4e7fc50d71278ff"));
    }

    [Fact]
    public void DrmKeySource_InvalidEntry_Ignored( )
    {
        var keys = new DrmKeySource(["not-a-key"]);

        Assert.False(keys.HasKeys);
        Assert.Null(keys.TryGetKey("d8f66b93db284984b4e7fc50d71278ff"));
    }

    // ═══ DrmDecryptor：KID 提取与命令构建 ═══

    [Theory]
    [InlineData("uri:bili://d8f66b93db284984b4e7fc50d71278ff", "d8f66b93db284984b4e7fc50d71278ff")]
    [InlineData("d8f66b93db284984b4e7fc50d71278ff", "d8f66b93db284984b4e7fc50d71278ff")]
    public void KidFromUri_TakesTextAfterLastDoubleSlash(string uri, string expected)
    {
        Assert.Equal(expected, DrmDecryptor.KidFromUri(uri));
    }

    [Fact]
    public void KidFromUri_Null( )
    {
        Assert.Null(DrmDecryptor.KidFromUri(null));
    }

    [Fact]
    public void BuildArgs_KeyBeforeInput_CopyRemuxToMp4( )
    {
        var args = DrmDecryptor.BuildArgs("d8f66b93db284984b4e7fc50d71278ff", "/tmp/enc.m4s", "/tmp/dec.mp4");

        Assert.Equal([
            "-loglevel", "warning", "-y",
            "-decryption_key", "d8f66b93db284984b4e7fc50d71278ff",
            "-i", "/tmp/enc.m4s",
            "-c", "copy", "-f", "mp4", "--", "/tmp/dec.mp4"
        ], args);
    }

    // ═══ DrmDecryptor：通道判定（不启动外部进程的路径）═══

    [Fact]
    public async Task DecryptAsync_Widevine_ReturnsUnsupported( )
    {
        var result = await DrmDecryptor.DecryptAsync(
            "widevine", "uri:bili://d8f66b93db284984b4e7fc50d71278ff", "a.m4s", "a.dec.mp4", new DrmKeySource([]), "ffmpeg", TestContext.Current.CancellationToken);

        Assert.Equal(DrmResult.Unsupported, result);
    }

    [Fact]
    public async Task DecryptAsync_BiliDrmWithoutKey_ReturnsKeyMissing( )
    {
        var result = await DrmDecryptor.DecryptAsync(
            "bili_drm", "uri:bili://d8f66b93db284984b4e7fc50d71278ff", "a.m4s", "a.dec.mp4", new DrmKeySource([]), "ffmpeg", TestContext.Current.CancellationToken);

        Assert.Equal(DrmResult.KeyMissing, result);
    }
}
