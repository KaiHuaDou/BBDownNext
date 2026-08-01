using System;
using System.IO;
using System.Linq;

namespace BBDown.Tests;

public class PartFileTests
{
    private const long PerSize = 20 * 1024 * 1024;

    private static string NewTempDir( )
    {
        var dir = Path.Combine(Path.GetTempPath( ), "bbdown_part_" + Path.GetRandomFileName( ));
        Directory.CreateDirectory(dir);
        return dir;
    }

    // B站每次解析拿到的 CDN 地址 host/query 都不同，指纹必须只认 path，否则永远续不上
    [Fact]
    public void Fingerprint_IgnoresHostAndQuery( )
    {
        var a = PartFile.Fingerprint("https://cn-hb.bilivideo.com/upgcxcode/10/20/2001/2001-1-30280.m4s?e=ig8&deadline=1");
        var b = PartFile.Fingerprint("http://upos-sz-mirror08c.bilivideo.com/upgcxcode/10/20/2001/2001-1-30280.m4s?e=xx&deadline=9999&oi=1");
        Assert.Equal(a, b);
    }

    // 换画质会改变 path 里的流 id，必须判定为不同内容，否则会静默合并出损坏文件
    [Fact]
    public void Fingerprint_DiffersWhenTrackChanges( )
    {
        var v80 = PartFile.Fingerprint("https://x.bilivideo.com/upgcxcode/10/20/2001/2001-1-80.m4s?e=1");
        var v120 = PartFile.Fingerprint("https://x.bilivideo.com/upgcxcode/10/20/2001/2001-1-120.m4s?e=1");
        Assert.NotEqual(v80, v120);
    }

    [Fact]
    public void Fingerprint_FallsBackToRawStringForNonAbsoluteUrl( )
    {
        Assert.NotEqual(PartFile.Fingerprint("not a url"), PartFile.Fingerprint("also not a url"));
        Assert.Equal(PartFile.Fingerprint("not a url"), PartFile.Fingerprint("not a url"));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(PerSize - 1)]
    [InlineData(PerSize)]
    public void Ranges_SmallerThanOneChunk_ProducesSingleRange(long totalSize)
    {
        var ranges = PartFile.Ranges(totalSize, PerSize);

        var only = Assert.Single(ranges);
        Assert.Equal(0, only.From);
        Assert.Equal(totalSize - 1, only.To);
    }

    // 旧实现末片 To=-1，导致「已下完的末片」永远判不出完成，每次续传都白发一个必然 416 的请求
    [Fact]
    public void Ranges_LastRangeEndsAtRealOffset( )
    {
        var ranges = PartFile.Ranges((PerSize * 5) + 12345, PerSize);

        Assert.Equal((PerSize * 5) + 12344, ranges[^1].To);
        Assert.Equal(6, ranges.Count);
    }

    // Range 是闭区间，下一片的 From 必须正好是上一片 To 的下一个字节：留缺口=文件损坏，重叠=字节重复
    [Fact]
    public void Ranges_AreContiguousWithoutGapOrOverlap( )
    {
        var ranges = PartFile.Ranges((PerSize * 4) + 999, PerSize);

        Assert.Equal(0, ranges[0].From);
        for (var i = 1; i < ranges.Count; i++)
        {
            Assert.Equal(ranges[i - 1].To + 1, ranges[i].From);
        }

        Assert.Equal((PerSize * 4) + 998, ranges[^1].To);
    }

    [Fact]
    public void Ranges_CoverExactlyTotalSize( )
    {
        const long total = (PerSize * 3) + 7;
        Assert.Equal(total, PartFile.Ranges(total, PerSize).Sum(r => r.To - r.From + 1));
    }

    [Fact]
    public void Ranges_ZeroOrNegativeSize_ProducesNothing( )
    {
        Assert.Empty(PartFile.Ranges(0, PerSize));
        Assert.Empty(PartFile.Ranges(-1, PerSize));
    }

    [Fact]
    public void Ranges_NonPositiveChunkSizeFallsBackToDefault( )
    {
        Assert.Equal(PartFile.Ranges(PerSize * 3, PartFile.DefaultChunkSize), PartFile.Ranges(PerSize * 3, 0));
    }

    [Fact]
    public void SaveAndLoad_RoundTrips( )
    {
        var dir = NewTempDir( );
        try
        {
            var dest = Path.Combine(dir, "video.mp4");
            var manifest = new PartManifest
            {
                Fingerprint = "abc123",
                TotalSize = 1000,
                ChunkSize = 400,
                IfRange = "\"etag-value\"",
                Completed = [400, 400, 12],
            };
            PartFile.Save(dest, manifest);

            var loaded = PartFile.TryLoad(dest);
            Assert.NotNull(loaded);
            Assert.Equal("abc123", loaded.Fingerprint);
            Assert.Equal(1000, loaded.TotalSize);
            Assert.Equal(400, loaded.ChunkSize);
            Assert.Equal("\"etag-value\"", loaded.IfRange);
            Assert.Equal([400, 400, 12], loaded.Completed);
            Assert.False(loaded.Done);
            Assert.Equal(812, PartFile.DownloadedBytes(loaded));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Save_LeavesNoStagingFileBehind( )
    {
        var dir = NewTempDir( );
        try
        {
            var dest = Path.Combine(dir, "video.mp4");
            PartFile.Save(dest, new PartManifest { Fingerprint = "x", Completed = [0] });
            PartFile.Save(dest, new PartManifest { Fingerprint = "y", Completed = [1] });

            Assert.Equal([PartFile.ManifestPath(dest)], Directory.GetFiles(dir));
            Assert.Equal("y", PartFile.TryLoad(dest)!.Fingerprint);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void TryLoad_ReturnsNullWhenMissingOrCorruptOrOutdated( )
    {
        var dir = NewTempDir( );
        try
        {
            var dest = Path.Combine(dir, "video.mp4");
            Assert.Null(PartFile.TryLoad(dest));

            File.WriteAllText(PartFile.ManifestPath(dest), "{ not json");
            Assert.Null(PartFile.TryLoad(dest));

            File.WriteAllText(PartFile.ManifestPath(dest), """{"Version":999,"Fingerprint":"x"}""");
            Assert.Null(PartFile.TryLoad(dest));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Matches_RequiresFingerprintSizeAndRangeCount( )
    {
        var manifest = new PartManifest
        {
            Fingerprint = "abc",
            TotalSize = 1000,
            ChunkSize = 400,
            Completed = [400, 400, 200],
        };

        Assert.True(PartFile.Matches(manifest, "abc", 1000));
        Assert.False(PartFile.Matches(manifest, "def", 1000));
        Assert.False(PartFile.Matches(manifest, "abc", 999));
        // 远端这次没给长度时不因此作废，指纹仍然有效
        Assert.True(PartFile.Matches(manifest, "abc", -1));
    }

    // 分片大小变了（比如改了配置）就不能沿用旧进度，否则 offset 全错
    [Fact]
    public void Matches_RejectsManifestWithStaleChunkLayout( )
    {
        var manifest = new PartManifest
        {
            Fingerprint = "abc",
            TotalSize = 1000,
            ChunkSize = 400,
            Completed = [1000],
        };

        Assert.False(PartFile.Matches(manifest, "abc", 1000));
    }

    [Fact]
    public void Matches_UnknownTotalSizeExpectsSingleEntry( )
    {
        var manifest = new PartManifest { Fingerprint = "abc", TotalSize = -1, ChunkSize = 400, Completed = [123] };
        Assert.True(PartFile.Matches(manifest, "abc", -1));
    }

    [Fact]
    public void Discard_RemovesBothPartAndManifest( )
    {
        var dir = NewTempDir( );
        try
        {
            var dest = Path.Combine(dir, "video.mp4");
            File.WriteAllBytes(PartFile.PartPath(dest), [1, 2, 3]);
            PartFile.Save(dest, new PartManifest { Fingerprint = "x", Completed = [3] });

            PartFile.Discard(dest);

            Assert.Empty(Directory.GetFiles(dir));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
