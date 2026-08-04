using System.Linq;
using System.Text.Json;

using BBDown.Core.Util;

namespace BBDown.Core.Tests;

public class JsonUtilTests
{
    private static JsonElement Parse(string json)
    {
        return JsonDocument.Parse(json).RootElement.Clone( );
    }

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

    [Fact]
    public void ArrayAtPath_DrillsDownNestedObjects( )
    {
        var found = JsonUtil.ArrayAtPath(Parse("""{"dash":{"video":[{"id":80},{"id":64}]}}"""), "dash", "video");
        Assert.Equal(["80", "64"], found!.Select(node => node.GetProperty("id").ToString( )));
    }

    // null 与空数组必须可区分：dash.audio 缺失要回退到 dolby/flac，存在但为空则不回退
    [Theory]
    [InlineData("""{"dash":{"audio":[]}}""", 0)]
    [InlineData("""{"dash":{"audio":null}}""", -1)]
    [InlineData("""{"dash":{}}""", -1)]
    [InlineData("""{"dash":[]}""", -1)]
    [InlineData("""{}""", -1)]
    [InlineData("""[]""", -1)]
    public void ArrayAtPath_MissingOrNonArrayGivesNull(string json, int expectedCount)
    {
        Assert.Equal(expectedCount, JsonUtil.ArrayAtPath(Parse(json), "dash", "audio")?.Count ?? -1);
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

    [Theory]
    [InlineData("""{"dimension":{"width":1920,"height":1080}}""", "1920x1080")]
    [InlineData("""{"dimension":{"width":1920,"height":1080,"rotate":0}}""", "1920x1080")]
    [InlineData("""{"dimension":{"width":1920}}""", "")]
    [InlineData("""{"dimension":null}""", "")]
    [InlineData("""{}""", "")]
    [InlineData("""[]""", "")]
    public void ReadDimension_MissingFieldsGiveEmptyString(string json, string expected)
    {
        Assert.Equal(expected, JsonUtil.ReadDimension(Parse(json)));
    }

    // 番剧接口给的是毫秒（实测 ep327325 为 2826000），Page.dur 按秒存放
    [Theory]
    [InlineData("""{"duration":2826000}""", 2826)]
    [InlineData("""{"duration":1500}""", 2)]
    [InlineData("""{"duration":499}""", 0)]
    [InlineData("""{"duration":"2826000"}""", 0)]
    [InlineData("""{"duration":null}""", 0)]
    [InlineData("""{"duration":2826.5}""", 0)]
    [InlineData("""{}""", 0)]
    [InlineData("""[]""", 0)]
    public void ReadDurationSeconds_ConvertsMillisecondsAndToleratesBadValues(string json, int expected)
    {
        Assert.Equal(expected, JsonUtil.ReadDurationSeconds(Parse(json)));
    }

    [Theory]
    [InlineData("""{"code":-403,"message":"访问权限不足"}""", -403, "访问权限不足")]
    [InlineData("""{"code":0}""", 0, "未知错误")]
    [InlineData("""{"message":"boom"}""", 0, "boom")]
    [InlineData("""{"code":"-403","message":404}""", 0, "未知错误")]
    [InlineData("""[]""", 0, "未知错误")]
    public void ReadApiError_FallsBackOnMissingOrMistypedFields(string json, int code, string message)
    {
        Assert.Equal((code, message), JsonUtil.ReadApiError(Parse(json)));
    }
}
