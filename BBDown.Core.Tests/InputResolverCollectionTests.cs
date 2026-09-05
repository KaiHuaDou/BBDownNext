using System.Threading;
using System.Threading.Tasks;

using BBDown.Core;
using BBDown.Core.Pipeline;

namespace BBDown.Core.Tests;

// 集合形态解析为纯字符串处理（不触网，不触发 FixAvidAsync 的 HEAD 探测），
// 故可在无网络环境下断言内部 id 形态与 TryDispatch 的命中 / 未命中行为。
public class InputResolverCollectionTests
{
    // 四种集合 URL 与对应的 ResourceId 子类型
    public static TheoryData<string, ResourceId> CollectionUrlCases => new( )
    {
        { "https://www.bilibili.com/read/readlist/rl75249", new ResourceId.ReadList(75249) },
        { "https://www.bilibili.com/read/readlist/rl75249?spm_id_from=333", new ResourceId.ReadList(75249) },
        { "https://space.bilibili.com/213741/upload/opus", new ResourceId.SpaceOpus(213741) },
        { "https://space.bilibili.com/213741/upload/audio", new ResourceId.SpaceAudio(213741) },
        { "https://space.bilibili.com/213741/audio", new ResourceId.SpaceAudio(213741) },
        { "https://space.bilibili.com/213741/dynamic", new ResourceId.SpaceDynamic(213741) },
        { "https://space.bilibili.com/213741/dynamic/", new ResourceId.SpaceDynamic(213741) },
    };

    [Theory]
    [MemberData(nameof(CollectionUrlCases))]
    public async Task ResolveIdAsync_CollectionUrl_ResolvesCorrectly(string input, ResourceId expected)
    {
        var result = await InputResolver.ResolveIdAsync(input, AppConfig.Empty, TestContext.Current.CancellationToken);
        Assert.Equal(expected, result);
    }

    // 集合简写：rl / readlist / spaceOpus / spaceAudio / spaceDynamic
    public static TheoryData<string, ResourceId> CollectionShorthandCases => new( )
    {
        { "rl75249", new ResourceId.ReadList(75249) },
        { "readlist75249", new ResourceId.ReadList(75249) },
        { "spaceOpus213741", new ResourceId.SpaceOpus(213741) },
        { "spaceopus213741", new ResourceId.SpaceOpus(213741) },
        { "spaceAudio213741", new ResourceId.SpaceAudio(213741) },
        { "spaceDynamic213741", new ResourceId.SpaceDynamic(213741) },
    };

    [Theory]
    [MemberData(nameof(CollectionShorthandCases))]
    public async Task ResolveIdAsync_CollectionShorthand_ResolvesCorrectly(string input, ResourceId expected)
    {
        var result = await InputResolver.ResolveIdAsync(input, AppConfig.Empty, TestContext.Current.CancellationToken);
        Assert.Equal(expected, result);
    }

    // 单音频（au）URL 与简写
    public static TheoryData<string, ResourceId> AudioInputCases => new( )
    {
        { "https://www.bilibili.com/audio/au12345", new ResourceId.Audio(12345) },
        { "https://www.bilibili.com/audio/au12345?spm_id_from=333", new ResourceId.Audio(12345) },
        { "au12345", new ResourceId.Audio(12345) },
        { "AU12345", new ResourceId.Audio(12345) },
    };

    [Theory]
    [MemberData(nameof(AudioInputCases))]
    public async Task ResolveIdAsync_AudioInput_ResolvesCorrectly(string input, ResourceId expected)
    {
        var result = await InputResolver.ResolveIdAsync(input, AppConfig.Empty, TestContext.Current.CancellationToken);
        Assert.Equal(expected, result);
    }

    // 回归护栏：空间音频列表页（/audio 无 au 尾段）不被单音频分支误吞
    [Theory]
    [InlineData("https://space.bilibili.com/213741/upload/audio")]
    [InlineData("https://space.bilibili.com/213741/audio")]
    public void TryDispatch_SpaceAudioList_NotAudio(string input)
    {
        Assert.True(InputResolver.TryDispatch(input, out var id));
        Assert.IsType<ResourceId.SpaceAudio>(id);
    }

    // 非法单音频形态不命中：纯前缀 / 非数字尾段 / 音频别字（audio123）一律拒绝
    [Theory]
    [InlineData("au")]
    [InlineData("auabc")]
    [InlineData("audio123")]
    [InlineData("https://www.bilibili.com/audio/au")]
    public void TryDispatch_MalformedAudio_DoesNotMatch(string input)
    {
        Assert.False(InputResolver.TryDispatch(input, out _));
    }

    [Fact]
    public void TryDispatch_CollectionInput_Matches( )
    {
        Assert.True(InputResolver.TryDispatch("https://www.bilibili.com/read/readlist/rl75249", out var id));
        Assert.Equal(new ResourceId.ReadList(75249), id);
    }

    // 回归护栏：视频形态不命中 TryDispatch（返回 false 走视频管道），避免分流误吞
    [Theory]
    [InlineData("https://www.bilibili.com/video/BV1xx411c7mD")]
    [InlineData("https://www.bilibili.com/video/av170001")]
    [InlineData("https://space.bilibili.com/402787936/upload/video")]
    [InlineData("https://space.bilibili.com/402787936")]
    [InlineData("ep123456")]
    [InlineData("av170001")]
    public void TryDispatch_VideoInput_DoesNotMatch(string input)
    {
        Assert.False(InputResolver.TryDispatch(input, out _));
    }

    // 回归护栏：合集 / 系列 / 收藏夹子页不被集合分支吞掉，仍走各自解析
    [Theory]
    [InlineData("https://space.bilibili.com/392959666/lists/1560264?type=season")]
    [InlineData("https://space.bilibili.com/3/favlist?fid=12345")]
    public void TryDispatch_SpaceListInput_DoesNotMatch(string input)
    {
        Assert.False(InputResolver.TryDispatch(input, out _));
    }

    // 简写前缀后必须紧跟纯数字：非数字 / 尾杂字符 / 纯前缀一律不命中，留给视频链路报输入错误
    [Theory]
    [InlineData("rlabc")]
    [InlineData("spaceOpus123a")]
    [InlineData("rl")]
    [InlineData("spaceOpus")]
    public void TryDispatch_MalformedShorthand_DoesNotMatch(string input)
    {
        Assert.False(InputResolver.TryDispatch(input, out _));
    }

    // 文集 URL 尾段非 rl 前缀数字时整体不命中（防止误吞成其它形态）
    [Theory]
    [InlineData("https://www.bilibili.com/read/readlist/abc")]
    [InlineData("https://www.bilibili.com/read/readlist/")]
    public void TryDispatch_MalformedReadListUrl_DoesNotMatch(string input)
    {
        Assert.False(InputResolver.TryDispatch(input, out _));
    }

    // 空间 URL 命中前缀但无法解析 mid（UidRegex 失败）时整体不命中
    [Fact]
    public void TryDispatch_SpaceUrlWithoutMid_DoesNotMatch( )
    {
        Assert.False(InputResolver.TryDispatch("https://space.bilibili.com//upload/opus", out _));
        Assert.False(InputResolver.TryDispatch("https://space.bilibili.com/abc/dynamic", out _));
    }
}
