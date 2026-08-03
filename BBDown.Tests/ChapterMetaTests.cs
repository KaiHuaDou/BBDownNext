using System;
using System.Collections.Generic;

using BBDown.Core;
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
        var points = new List<Entity.ViewPoint>
        {
            new( ) { title = "Intro", start = 0, end = 10 },
            new( ) { title = "Chapter 2", start = 10, end = 20 },
        };

        var meta = ChapterMeta.GetFFmpegMetaString(points);

        var nl = Environment.NewLine;
        var expected = string.Join(nl, FfmpegLines) + nl;

        Assert.Equal(expected, meta);
    }

    [Fact]
    public void GetMp4boxMetaString_EmitsTimestampedTitles( )
    {
        var points = new List<Entity.ViewPoint>
        {
            new( ) { title = "Intro", start = 0, end = 10 },
            new( ) { title = "Chapter 2", start = 10, end = 20 },
            new( ) { title = "Deep", start = 3661, end = 3700 },
        };

        var meta = ChapterMeta.GetMp4boxMetaString(points);

        var nl = Environment.NewLine;
        var expected = string.Join(nl, Mp4boxLines) + nl;

        Assert.Equal(expected, meta);
    }
}
