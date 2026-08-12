namespace BBDown.DRM.Tests;

public class DrmKeysTests
{
    [Theory]
    [InlineData("uri:bili://d8f66b93db284984b4e7fc50d71278ff", "d8f66b93db284984b4e7fc50d71278ff")]
    [InlineData("d8f66b93db284984b4e7fc50d71278ff", "d8f66b93db284984b4e7fc50d71278ff")]
    public void KidFromUri_TakesTextAfterLastDoubleSlash(string uri, string expected)
    {
        Assert.Equal(expected, DrmKeys.KidFromUri(uri));
    }

    [Fact]
    public void KidFromUri_Null_ReturnsNull( )
    {
        Assert.Null(DrmKeys.KidFromUri(null));
    }
}
