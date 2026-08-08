using System;
using System.Collections.Generic;

using BBDown.Core.Entity;

namespace BBDown.Tests;

public class ChapterMetaTests
{
    private static readonly string[] FfmpegLines =
    [
        ";FFMETADATA",
        "[CHAPTER]",
        "TIMEBASE=1/1000",
        "START=0",
        "END=10000",
        "title=Intro",
        "",
        "[CHAPTER]",
        "TIMEBASE=1/1000",
        "START=10000",
        "END=20000",
        "title=Chapter 2",
        "",
    ];

    private static readonly string[] Mp4boxLines =
    [
        "00:00:00 Intro",
        "00:00:10 Chapter 2",
        "01:01:01 Deep",
    ];

    [Fact]
    public void GetFFmpegMetaString_EmitsFfmetadataWithChapters( )
    {
        var points = new List<ViewPoint>
        {
            new( ) { Title = "Intro", Start = 0, End = 10 },
            new( ) { Title = "Chapter 2", Start = 10, End = 20 },
        };

        var meta = ChapterMeta.GetFFmpegMetaString(points);

        var nl = Environment.NewLine;
        var expected = string.Join(nl, FfmpegLines) + nl;

        Assert.Equal(expected, meta);
    }

    [Fact]
    public void GetMp4boxMetaString_EmitsTimestampedTitles( )
    {
        var points = new List<ViewPoint>
        {
            new( ) { Title = "Intro", Start = 0, End = 10 },
            new( ) { Title = "Chapter 2", Start = 10, End = 20 },
            new( ) { Title = "Deep", Start = 3661, End = 3700 },
        };

        var meta = ChapterMeta.GetMp4boxMetaString(points);

        var nl = Environment.NewLine;
        var expected = string.Join(nl, Mp4boxLines) + nl;

        Assert.Equal(expected, meta);
    }

    [Fact]
    public void ParsePlayerV2_ReadsViewPoints( )
    {
        const string Json = """
        {"code":0,"data":{"view_points":[{"content":"Intro","from":0,"to":10},{"content":"正片","from":10,"to":20}]}}
        """;

        var info = ChapterMeta.ParsePlayerV2(Json);

        Assert.Equal(2, info.Points.Count);
        Assert.Equal("正片", info.Points[1].Title);
        Assert.Equal(10, info.Points[1].Start);
        Assert.Equal(20, info.Points[1].End);
    }

    [Fact]
    public void ParsePlayerV2_ReadsUpowerExclusiveAndTitle( )
    {
        const string Json = """
        {"code":0,"data":{"is_upower_exclusive":true,"is_upower_play":false,"elec_high_level":{"privilege_type":3,"title":"该视频为「铁粉」专属视频"}}}
        """;

        var info = ChapterMeta.ParsePlayerV2(Json);

        Assert.True(info.UpowerExclusive);
        Assert.Equal("该视频为「铁粉」专属视频", info.UpowerTitle);
    }

    [Fact]
    public void ParsePlayerV2_NormalVideo_ReportsNotExclusive( )
    {
        const string Json = """
        {"code":0,"data":{"is_upower_exclusive":false,"elec_high_level":{"privilege_type":0,"title":""}}}
        """;

        var info = ChapterMeta.ParsePlayerV2(Json);

        Assert.False(info.UpowerExclusive);
        Assert.Equal("", info.UpowerTitle);
        Assert.Empty(info.Points);
    }

    [Fact]
    public void ParsePlayerV2_ErrorResponse_ReturnsEmpty( )
    {
        var info = ChapterMeta.ParsePlayerV2("""{"code":-404,"message":"啥都木有","data":null}""");

        Assert.Empty(info.Points);
        Assert.False(info.UpowerExclusive);
        Assert.Equal("", info.UpowerTitle);
    }
}
