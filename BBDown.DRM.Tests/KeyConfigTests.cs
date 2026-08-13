using System;
using System.Text.Json;

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

    // README 示例用小写 keys；反序列化大小写不敏感，两种写法都须生效
    [Theory]
    [InlineData("""{"keys":["kid:key"]}""")]
    [InlineData("""{"Keys":["kid:key"]}""")]
    public void Deserialize_KeyFile_CaseInsensitive(string json)
    {
        var config = JsonSerializer.Deserialize(json, DrmJsonContext.Default.KeyFile);

        Assert.NotNull(config);
        Assert.Equal(["kid:key"], config!.Keys);
    }
}
