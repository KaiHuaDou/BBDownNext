using System.Linq;
using System.Text.Json;

using BBDown.Core.Util;

namespace BBDown.Core.Tests;

public class JsonUtilTests
{
    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone( );

    [Theory]
    [InlineData("""{"dash":{}}""", "dash", true)]
    [InlineData("""{"dash":{"video":[]}}""", "dash", true)]
    [InlineData("""{"dash":[]}""", "dash", false)]
    [InlineData("""{"dash":null}""", "dash", false)]
    [InlineData("""{"dash":"{}"}""", "dash", false)]
    [InlineData("""{"durl":[]}""", "dash", false)]
    [InlineData("""[]""", "dash", false)]
    public void HasObject_OnlyMatchesObjectValued(string json, string name, bool expected)
    {
        Assert.Equal(expected, JsonUtil.HasObject(Parse(json), name));
    }

    [Fact]
    public void TryGetArray_ReturnsArrayNode( )
    {
        Assert.True(JsonUtil.TryGetArray(Parse("""{"durl":[1,2,3]}"""), "durl", out var array));
        Assert.Equal(3, array.GetArrayLength( ));
    }

    [Theory]
    [InlineData("""{"durl":{}}""")]
    [InlineData("""{"durl":null}""")]
    [InlineData("""{"other":[]}""")]
    [InlineData("""3""")]
    public void TryGetArray_NonArrayFails(string json)
    {
        Assert.False(JsonUtil.TryGetArray(Parse(json), "durl", out var array));
        Assert.Equal(JsonValueKind.Undefined, array.ValueKind);
    }

    [Fact]
    public void EnumerateArrayOrEmpty_NonArrayGivesEmptySequence( )
    {
        Assert.Empty(JsonUtil.EnumerateArrayOrEmpty(Parse("""{"a":1}""")));
        Assert.Empty(JsonUtil.EnumerateArrayOrEmpty(default));
        Assert.Equal(2, JsonUtil.EnumerateArrayOrEmpty(Parse("[1,2]")).Count( ));
    }

    [Theory]
    [InlineData("""[{"id":123},{"id":456}]""", "123", true)]
    [InlineData("""[{"id":123},{"id":456}]""", "456", true)]
    [InlineData("""[{"id":123}]""", "999", false)]
    [InlineData("""[]""", "123", false)]
    [InlineData("""{}""", "123", false)]
    public void ContainsEpisode_MatchesIdField(string json, string epId, bool expected)
    {
        Assert.Equal(expected, JsonUtil.ContainsEpisode(Parse(json), epId));
    }

    // 旧实现把整棵子树 ToString 后找 "/ep123"，ep1234 的链接会误命中
    [Fact]
    public void ContainsEpisode_DoesNotMatchLongerIdPrefix( )
    {
        var episodes = Parse("""[{"id":1234,"link":"https://www.bilibili.com/bangumi/play/ep1234"}]""");
        Assert.False(JsonUtil.ContainsEpisode(episodes, "123"));
        Assert.True(JsonUtil.ContainsEpisode(episodes, "1234"));
    }

    // 分集自身的 id 才算命中，正文里出现的其他 ep 链接不算
    [Fact]
    public void ContainsEpisode_IgnoresIdsMentionedElsewhere( )
    {
        var episodes = Parse("""[{"id":1,"share_copy":"see also /ep777"}]""");
        Assert.False(JsonUtil.ContainsEpisode(episodes, "777"));
    }
}
