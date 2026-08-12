using System;

namespace BBDown.DRM.Tests;

public class KeyConfigTests
{
    [Fact]
    public void Load_FromEnvironmentVariable( )
    {
        Environment.SetEnvironmentVariable("BBDOWN_DRM_KEYS", "d8f66b93db284984b4e7fc50d71278ff:00112233445566778899aabbccddeeff");
        try
        {
            var keys = KeyConfig.Load( );

            Assert.Equal("00112233445566778899aabbccddeeff", keys.TryGetKey("d8f66b93db284984b4e7fc50d71278ff"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("BBDOWN_DRM_KEYS", null);
        }
    }

    [Fact]
    public void Load_NoConfig_ReturnsEmpty( )
    {
        Environment.SetEnvironmentVariable("BBDOWN_DRM_KEYS", null);

        var keys = KeyConfig.Load( );

        Assert.False(keys.HasKeys);
    }
}
