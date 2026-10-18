using System.Text.Json;

using BBDown.Core.PlayUrl;

namespace BBDown.Core.Tests;

/// <summary>
/// 试看判定（is_preview）的纯函数测试：数字 / 布尔 / 缺失各形态
/// </summary>
public class PlayUrlResponseTests
{
    [Theory]
    [InlineData("""{"is_preview":1}""", true)]
    [InlineData("""{"is_preview":0}""", false)]
    [InlineData("""{"is_preview":true}""", true)]
    [InlineData("""{"is_preview":false}""", false)]
    [InlineData("""{"is_preview":"1"}""", true)]
    [InlineData("""{}""", false)]
    [InlineData("""{"is_preview":null}""", false)]
    public void IsPreview_MatchesPayloadFlag(string json, bool expected)
    {
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(expected, PlayUrlResponse.IsPreview(doc.RootElement));
    }

    [Fact]
    public void IsPreview_NonObjectPayload_ReturnsFalse( )
    {
        using var doc = JsonDocument.Parse("""["is_preview"]""");
        Assert.False(PlayUrlResponse.IsPreview(doc.RootElement));
    }
}
