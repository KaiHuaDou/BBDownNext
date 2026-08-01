using BBDown;

namespace BBDown.Tests;

public class ProgramTests
{
    [Fact]
    public void DetermineApiType_DefaultsToWeb()
    {
        var o = new MyOption();
        Assert.Equal("WEB", Program.DetermineApiType(o));
    }

    [Fact]
    public void DetermineApiType_TvApi()
    {
        var o = new MyOption { UseTvApi = true };
        Assert.Equal("TV", Program.DetermineApiType(o));
    }

    [Fact]
    public void DetermineApiType_AppApi()
    {
        var o = new MyOption { UseAppApi = true };
        Assert.Equal("APP", Program.DetermineApiType(o));
    }

    [Fact]
    public void DetermineApiType_IntlApi()
    {
        var o = new MyOption { UseIntlApi = true };
        Assert.Equal("INTL", Program.DetermineApiType(o));
    }

    [Fact]
    public void ParseEncodingPriority_NullInput_YieldsEmpty()
    {
        var o = new MyOption { EncodingPriority = null };
        var (encodingPriority, firstEncoding) = Program.ParseEncodingPriority(o);
        Assert.Empty(encodingPriority);
        Assert.Equal("", firstEncoding);
    }

    [Fact]
    public void ParseEncodingPriority_ParsesPriorityAndFirst()
    {
        var o = new MyOption { EncodingPriority = "avc,hevc,av1" };
        var (encodingPriority, firstEncoding) = Program.ParseEncodingPriority(o);
        Assert.Equal(3, encodingPriority.Count);
        Assert.Equal((byte)0, encodingPriority["AVC"]);
        Assert.Equal((byte)1, encodingPriority["HEVC"]);
        Assert.Equal((byte)2, encodingPriority["AV1"]);
        Assert.Equal("AVC", firstEncoding);
    }

    [Fact]
    public void ParseEncodingPriority_CleansChineseCommaAndTrims()
    {
        var o = new MyOption { EncodingPriority = "avc，hevc, av1 " };
        var (encodingPriority, firstEncoding) = Program.ParseEncodingPriority(o);
        Assert.Equal(3, encodingPriority.Count);
        Assert.Equal((byte)0, encodingPriority["AVC"]);
        Assert.Equal((byte)1, encodingPriority["HEVC"]);
        Assert.Equal((byte)2, encodingPriority["AV1"]);
        Assert.Equal("AVC", firstEncoding);
    }

    [Fact]
    public void ParseEncodingPriority_DedupesRepeatedEncoding()
    {
        var o = new MyOption { EncodingPriority = "avc,avc,hevc" };
        var (encodingPriority, _) = Program.ParseEncodingPriority(o);
        Assert.Equal(2, encodingPriority.Count);
        Assert.Equal((byte)0, encodingPriority["AVC"]);
        Assert.Equal((byte)1, encodingPriority["HEVC"]);
    }

    [Fact]
    public void ParseDfnPriority_NullInput_YieldsEmpty()
    {
        var o = new MyOption { DfnPriority = null };
        Assert.Empty(Program.ParseDfnPriority(o));
    }

    [Fact]
    public void ParseDfnPriority_ParsesPriority()
    {
        var o = new MyOption { DfnPriority = "1080p,720p,360p" };
        var dfn = Program.ParseDfnPriority(o);
        Assert.Equal(3, dfn.Count);
        Assert.Equal(0, dfn["1080P"]);
        Assert.Equal(1, dfn["720P"]);
        Assert.Equal(2, dfn["360P"]);
    }

    [Fact]
    public void ParseDownloadDanmakuFormats_EmptyInput_UsesDefault()
    {
        var o = new MyOption { DownloadDanmakuFormats = null };
        var formats = Program.ParseDownloadDanmakuFormats(o);
        Assert.Equal(BBDownDanmakuFormatInfo.DefaultFormats, formats);
    }

    [Fact]
    public void ParseDownloadDanmakuFormats_ParsesExplicit()
    {
        var o = new MyOption { DownloadDanmakuFormats = "xml,ass" };
        var formats = Program.ParseDownloadDanmakuFormats(o);
        Assert.Contains(BBDownDanmakuFormat.Xml, formats);
        Assert.Contains(BBDownDanmakuFormat.Ass, formats);
    }

    [Fact]
    public void ParseDownloadDanmakuFormats_InvalidFallsBackToDefault()
    {
        var o = new MyOption { DownloadDanmakuFormats = "xml,bogus" };
        var formats = Program.ParseDownloadDanmakuFormats(o);
        Assert.Equal(BBDownDanmakuFormatInfo.DefaultFormats, formats);
    }

    [Fact]
    public void HandleConflictingOptions_InteractiveForcesShowStreams()
    {
        var o = new MyOption { Interactive = true, HideStreams = true };
        Program.HandleConflictingOptions(o);
        Assert.False(o.HideStreams);
    }

    [Fact]
    public void HandleConflictingOptions_AudioOnlyAndVideoOnlyBothCleared()
    {
        var o = new MyOption { AudioOnly = true, VideoOnly = true };
        Program.HandleConflictingOptions(o);
        Assert.False(o.AudioOnly);
        Assert.False(o.VideoOnly);
    }

    [Fact]
    public void HandleConflictingOptions_NoSubClearsSubOnly()
    {
        var o = new MyOption { NoSub = true, SubOnly = true };
        Program.HandleConflictingOptions(o);
        Assert.False(o.SubOnly);
    }
}
