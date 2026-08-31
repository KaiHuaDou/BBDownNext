namespace BBDown.DRM.Tests;

public class DrmDecryptorTests
{
    [Fact]
    public void BuildArgs_SingleKeyCbcsDecrypt( )
    {
        var args = DrmDecryptor.BuildArgs("d8f66b93db284984b4e7fc50d71278ff", "a.m4s", "a.dec.mp4");

        Assert.Equal(
            ["-loglevel", "warning", "-y", "-decryption_key", "d8f66b93db284984b4e7fc50d71278ff", "-i", "a.m4s", "-c", "copy", "-f", "mp4", "--", "a.dec.mp4"],
            args);
    }
}
