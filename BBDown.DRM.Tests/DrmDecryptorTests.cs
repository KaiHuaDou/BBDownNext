using Xunit;

namespace BBDown.DRM.Tests;

public class DrmDecryptorTests
{
    [Theory]
    [InlineData("uri:bili://d8f66b93db284984b4e7fc50d71278ff", "d8f66b93db284984b4e7fc50d71278ff")]
    [InlineData("d8f66b93db284984b4e7fc50d71278ff", "d8f66b93db284984b4e7fc50d71278ff")]
    public void KidFromUri_TakesTextAfterLastDoubleSlash(string uri, string expected)
    {
        Assert.Equal(expected, DrmDecryptor.KidFromUri(uri));
    }

    [Fact]
    public void KidFromUri_Null_ReturnsNull( )
    {
        Assert.Null(DrmDecryptor.KidFromUri(null));
    }

    [Fact]
    public void BuildArgs_SingleKeyCbcsDecrypt( )
    {
        var args = DrmDecryptor.BuildArgs("d8f66b93db284984b4e7fc50d71278ff", "a.m4s", "a.dec.mp4");

        Assert.Equal(
            ["-loglevel", "warning", "-y", "-decryption_key", "d8f66b93db284984b4e7fc50d71278ff", "-i", "a.m4s", "-c", "copy", "-f", "mp4", "--", "a.dec.mp4"],
            args);
    }
}
