using System;
using System.Linq;

namespace BBDown.DRM.Tests;

public class DrmKeySourceTests
{
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
}
