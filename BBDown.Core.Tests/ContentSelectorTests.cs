using System.Collections.Generic;
using System.Linq;

namespace BBDown.Core.Tests;

public class ContentSelectorTests
{
    private static DownloadContent Resolve(
        out List<string> warnings,
        string[]? get = null,
        string[]? with = null,
        string[]? without = null,
        bool commentCountExplicit = false,
        bool commentSortExplicit = false,
        bool commentFormatsExplicit = false,
        bool danmakuFormatsExplicit = false)
    {
        return ContentSelector.Resolve(get ?? [], with ?? [], without ?? [],
            commentCountExplicit, commentSortExplicit, commentFormatsExplicit, danmakuFormatsExplicit, out warnings);
    }

    [Fact]
    public void Resolve_EmptyInputs_ReturnsNone( )
    {
        var flags = Resolve(out _);
        Assert.Equal(DownloadContent.None, flags);
    }

    [Fact]
    public void Resolve_DefaultValue_EqualsDefaultFlags( )
    {
        var flags = ContentSelector.Resolve([ContentSelector.Default], [], [], false, false, false, false, out var warnings);
        Assert.Empty(warnings);
        Assert.Equal(ContentSelector.DefaultFlags, flags);
    }

    [Fact]
    public void Resolve_DefaultFlags_MatchesDefaultString( )
    {
        Assert.Equal("avCimMs", ContentSelector.ToNormalizedString(ContentSelector.DefaultFlags));
    }

    // ---- 集合运算 ----

    [Fact]
    public void Resolve_GetMergesAcrossMultipleValues( )
    {
        var flags = Resolve(out _, get: ["av", "c"]);
        Assert.True(flags.Has(DownloadContent.Audio));
        Assert.True(flags.Has(DownloadContent.Video));
        Assert.True(flags.Has(DownloadContent.Cover));
        Assert.Equal("avc", ContentSelector.ToNormalizedString(flags));
    }

    [Fact]
    public void Resolve_WithAddsOnTopOfGet( )
    {
        var flags = Resolve(out _, get: ["av"], with: ["s"]);
        Assert.True(flags.Has(DownloadContent.Subtitle));
        Assert.True(flags.Has(DownloadContent.Video));
    }

    [Fact]
    public void Resolve_WithoutRemovesFromGet( )
    {
        var flags = Resolve(out _, get: ["avmsCi"], without: ["m"]);
        Assert.False(flags.Has(DownloadContent.MuxMetadata));
        Assert.True(flags.Has(DownloadContent.Audio));
    }

    [Fact]
    public void Resolve_WithoutUnknownFlagIsSilent( )
    {
        // 减掉集合中不存在的合法字符：静默，不警告
        var flags = Resolve(out var warnings, get: ["v"], without: ["a"]);
        Assert.Empty(warnings);
        Assert.Equal(DownloadContent.Video, flags);
    }

    [Fact]
    public void Resolve_DuplicateChars_Deduplicated( )
    {
        var flags = Resolve(out _, get: ["aaavvv"]);
        Assert.Equal(DownloadContent.Audio | DownloadContent.Video, flags);
    }

    // ---- 非法字符 ----

    [Fact]
    public void Resolve_InvalidChar_WarnsAndDrops( )
    {
        var flags = Resolve(out var warnings, get: ["avx"]);
        Assert.Single(warnings);
        Assert.Contains("无效的内容字符「x」", warnings[0]);
        Assert.Equal(DownloadContent.Audio | DownloadContent.Video, flags);
    }

    [Fact]
    public void Resolve_InvalidCharInWithout_Warns( )
    {
        Resolve(out var warnings, get: ["av"], without: ["y"]);
        Assert.Single(warnings);
        Assert.Contains("无效的内容字符「y」", warnings[0]);
    }

    // ---- 依赖规则 ----

    [Fact]
    public void Resolve_MuxCoverWithoutAudioVideo_Warns( )
    {
        Resolve(out var warnings, get: ["C"]);
        Assert.Contains(warnings, w => w.Contains("封面嵌入", System.StringComparison.Ordinal));
    }

    [Fact]
    public void Resolve_MuxMetadataWithoutAudioVideo_Warns( )
    {
        Resolve(out var warnings, get: ["m"]);
        Assert.Contains(warnings, w => w.Contains("嵌入元数据", System.StringComparison.Ordinal));
    }

    [Fact]
    public void Resolve_SubtitleAlone_NoWarning( )
    {
        var flags = Resolve(out var warnings, get: ["s"]);
        Assert.Empty(warnings);
        Assert.Equal(DownloadContent.Subtitle, flags);
    }

    [Fact]
    public void Resolve_BothComments_KeepsFullOnly( )
    {
        var flags = Resolve(out var warnings, get: ["oO"]);
        Assert.Empty(warnings);
        Assert.True(flags.Has(DownloadContent.FullComments));
        Assert.False(flags.Has(DownloadContent.Comments));
    }

    // ---- 配套选项警告 ----

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void Resolve_CommentOptionWithoutComments_Warns(bool count, bool sort, bool formats)
    {
        Resolve(out var warnings, get: ["av"], commentCountExplicit: count, commentSortExplicit: sort, commentFormatsExplicit: formats);
        Assert.Contains(warnings, w => w.Contains("评论", System.StringComparison.Ordinal));
    }

    [Fact]
    public void Resolve_CommentOptionWithComments_NoWarning( )
    {
        Resolve(out var warnings, get: ["avo"], commentCountExplicit: true);
        Assert.DoesNotContain(warnings, w => w.Contains("评论", System.StringComparison.Ordinal));
    }

    [Fact]
    public void Resolve_DanmakuFormatsWithoutDanmaku_Warns( )
    {
        Resolve(out var warnings, get: ["av"], danmakuFormatsExplicit: true);
        Assert.Contains(warnings, w => w.Contains("弹幕", System.StringComparison.Ordinal));
    }

    [Fact]
    public void Resolve_DanmakuFormatsWithDanmaku_NoWarning( )
    {
        Resolve(out var warnings, get: ["avd"], danmakuFormatsExplicit: true);
        Assert.DoesNotContain(warnings, w => w.Contains("弹幕", System.StringComparison.Ordinal));
    }

    // ---- 规范化互转 ----

    [Theory]
    [InlineData("avmsCi")]
    [InlineData("avmsCiM")]
    [InlineData("aCimsv")]
    [InlineData("")]
    [InlineData("Od")]
    public void FromNormalizedString_RoundTrips(string value)
    {
        var flags = ContentSelector.FromNormalizedString(value);
        Assert.Equal(flags, ContentSelector.FromNormalizedString(ContentSelector.ToNormalizedString(flags)));
    }

    [Fact]
    public void FromNormalizedString_IgnoresInvalidChars( )
    {
        var flags = ContentSelector.FromNormalizedString("avxyz");
        Assert.Equal(DownloadContent.Audio | DownloadContent.Video, flags);
    }

    [Fact]
    public void ToNormalizedString_OrdersByCanonicalSequence( )
    {
        var flags = DownloadContent.Video | DownloadContent.Audio | DownloadContent.Subtitle;
        Assert.Equal("avs", ContentSelector.ToNormalizedString(flags));
    }

    // ---- 模式失效 ----

    [Fact]
    public void DescribeInactive_Opus_ImageActiveOthersInactive( )
    {
        var list = ContentSelector.DescribeInactive(ContentSelector.DefaultFlags, ContentMode.Opus);
        Assert.True(list.All(d => !d.Contains("专栏图片", System.StringComparison.Ordinal)));
        Assert.Contains(list, d => d.Contains("音频", System.StringComparison.Ordinal));
        Assert.Contains(list, d => d.Contains("视频", System.StringComparison.Ordinal));
        Assert.Contains(list, d => d.Contains("字幕", System.StringComparison.Ordinal));
    }

    [Fact]
    public void DescribeInactive_Opus_ImageAndFrontMatterActive( )
    {
        var list = ContentSelector.DescribeInactive(
            DownloadContent.OpusImage | DownloadContent.FrontMatter, ContentMode.Opus);
        Assert.Empty(list);
    }

    [Fact]
    public void DescribeInactive_Video_OpusFlagsInactive( )
    {
        var list = ContentSelector.DescribeInactive(DownloadContent.OpusImage, ContentMode.Video);
        Assert.Single(list);
        Assert.Contains("专栏图片", list[0]);
    }

    [Fact]
    public void DescribeInactive_Live_OnlyAudioVideoActive( )
    {
        var list = ContentSelector.DescribeInactive(
            DownloadContent.Audio | DownloadContent.Video | DownloadContent.Danmaku, ContentMode.Live);
        Assert.Single(list);
        Assert.Contains("弹幕", list[0]);
    }

    [Fact]
    public void DescribeInactive_Audio_OnlyAudioActive( )
    {
        // 音频下载恒产出音频文件：其余标志（含视频轨 v）在音频模式下均提示不生效。
        // 用带字符标记的形式断言（模式名「音频下载」本身含「音频」，裸关键词会误判）
        var list = ContentSelector.DescribeInactive(
            DownloadContent.Audio | DownloadContent.Video | DownloadContent.Danmaku, ContentMode.Audio);
        Assert.Equal(2, list.Count);
        Assert.Contains(list, d => d.Contains("视频（v）", System.StringComparison.Ordinal));
        Assert.Contains(list, d => d.Contains("弹幕（d）", System.StringComparison.Ordinal));
        Assert.DoesNotContain(list, d => d.Contains("音频（a）", System.StringComparison.Ordinal));
    }

    [Fact]
    public void DescribeInactive_Audio_AudioFlagOnly_Empty( )
    {
        var list = ContentSelector.DescribeInactive(DownloadContent.Audio, ContentMode.Audio);
        Assert.Empty(list);
    }

    // ---- 资源类型 → 内容适用域（ModeOf 为纯函数，CLI 与 serve 共用判定点） ----

    public static TheoryData<ResourceId, ContentMode> ModeOfCases => new( )
    {
        { new ResourceId.LiveRoom(123), ContentMode.Live },
        { new ResourceId.OpusArticle(123, 0), ContentMode.Opus },
        { new ResourceId.OpusArticle(0, 123), ContentMode.Opus },
        { new ResourceId.ReadList(75249), ContentMode.Opus },
        { new ResourceId.SpaceOpus(213741), ContentMode.Opus },
        { new ResourceId.SpaceDynamic(213741), ContentMode.Opus },
        { new ResourceId.SpaceAudio(213741), ContentMode.Audio },
        { new ResourceId.Av(170001), ContentMode.Video },
        { new ResourceId.Ep(68540), ContentMode.Video },
        { new ResourceId.Season(2539), ContentMode.Video },
        { new ResourceId.CheeseEp(1), ContentMode.Video },
        { new ResourceId.CheeseSeason(1), ContentMode.Video },
        { new ResourceId.Fav(123, 3), ContentMode.Video },
        { new ResourceId.MediaList(1560264), ContentMode.Video },
        { new ResourceId.Series(1560264), ContentMode.Video },
        { new ResourceId.Space(402787936), ContentMode.Video },
        { new ResourceId.WatchLater( ), ContentMode.Video },
    };

    [Theory]
    [MemberData(nameof(ModeOfCases))]
    public void ModeOf_ResourceId_MapsToContentMode(ResourceId id, ContentMode expected)
    {
        Assert.Equal(expected, ContentSelector.ModeOf(id));
    }
}
