using System;

namespace BBDown.Core.Tests;

// ResourceId 的规范串往返与前缀匹配（TryParse / ResourceIdJsonConverter.Format 均为纯函数）
public class ResourceIdTests
{
    public static TheoryData<ResourceId, string> FormatCases => new( )
    {
        { new ResourceId.ReadList(75249), "readlist75249" },
        { new ResourceId.SpaceOpus(213741), "spaceOpus213741" },
        { new ResourceId.SpaceAudio(213741), "spaceAudio213741" },
        { new ResourceId.SpaceDynamic(213741), "spaceDynamic213741" },
        { new ResourceId.Audio(12345), "au12345" },
    };

    [Theory]
    [MemberData(nameof(FormatCases))]
    public void Format_NewCollectionTypes_CanonicalString(ResourceId id, string expected)
    {
        Assert.Equal(expected, ResourceIdJsonConverter.Format(id));
    }

    [Theory]
    [MemberData(nameof(FormatCases))]
    public void TryParse_CanonicalString_RoundTrips(ResourceId id, string expected)
    {
        Assert.True(ResourceId.TryParse(expected, out var parsed));
        Assert.Equal(id, parsed);
    }

    [Theory]
    [InlineData("rl75249")]
    [InlineData("readlist75249")]
    public void TryParse_ReadListDualPrefix_Accepted(string input)
    {
        // 前缀匹配为 Ordinal（大小写敏感），仅接受规范形态；用户输入简写的大小写宽容由 InputResolver 负责
        Assert.True(ResourceId.TryParse(input, out var parsed));
        Assert.Equal(new ResourceId.ReadList(75249), parsed);
    }

    [Theory]
    [InlineData("spaceOpus123", typeof(ResourceId.SpaceOpus))]
    [InlineData("spaceAudio123", typeof(ResourceId.SpaceAudio))]
    [InlineData("spaceDynamic123", typeof(ResourceId.SpaceDynamic))]
    public void TryParse_LongSpacePrefix_WinsOverSpace(string input, Type expectedType)
    {
        // 前缀按长度降序匹配：spaceOpus 等长前缀须先于 space 命中，否则被 space 吞掉后 TryLong 失败整体返回 false
        Assert.True(ResourceId.TryParse(input, out var parsed));
        Assert.Equal(expectedType, parsed!.GetType( ));
        Assert.Equal(ResourceIdJsonConverter.Format(parsed), input);
    }

    [Fact]
    public void TryParse_BareSpacePrefix_StillResolves( )
    {
        // 回归护栏：space 单前缀行为不变（全部投稿视频）
        Assert.True(ResourceId.TryParse("space123", out var parsed));
        Assert.Equal(new ResourceId.Space(123), parsed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("rl")]
    [InlineData("readlist")]
    [InlineData("spaceOpus")]
    [InlineData("rlabc")]
    [InlineData("spaceOpus123a")]
    [InlineData("rl-1")]
    [InlineData("readlist 1")]
    [InlineData("au")]
    [InlineData("auabc")]
    [InlineData("au-1")]
    [InlineData("audio123")]
    public void TryParse_InvalidInput_Rejected(string input)
    {
        // 仅接受纯数字（无符号 / 空白），规范形态与非法输入严格区分
        Assert.False(ResourceId.TryParse(input, out _));
    }
}
