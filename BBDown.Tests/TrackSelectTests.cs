using System.Collections.Generic;

using BBDown.Core;

using static BBDown.Core.Entity.Entity;

namespace BBDown.Tests;

public class TrackSelectTests
{
    private static Video V(string id, long bandwidth)
    {
        return new( ) { id = id, bandwidth = bandwidth, dfn = Config.GetQualityName(id), baseUrl = "", codecs = "" };
    }

    // 默认（无 -q）时原生 1080P(qn=80) 优先于智能修复(qn=100)，8K 仍最高
    [Fact]
    public void SortTracks_NoPriorityPrefersNative1080POverAiRepair( )
    {
        var tracks = new List<Video> { V("100", 5000), V("80", 3000), V("127", 9000) };
        var sorted = TrackSelect.SortTracks(tracks, [], [], videoAscending: false, encodingFirst: false);
        Assert.Equal("127", sorted[0].id);
        Assert.Equal("80", sorted[1].id);
        Assert.Equal("100", sorted[2].id);
    }

    // -q "智能修复" 时该档位置顶
    [Fact]
    public void SortTracks_ExplicitAiRepairPriorityPutsItFirst( )
    {
        var tracks = new List<Video> { V("100", 5000), V("80", 3000) };
        var dfn = new Dictionary<string, int> { ["智能修复"] = 0 };
        var sorted = TrackSelect.SortTracks(tracks, dfn, [], videoAscending: false, encodingFirst: false);
        Assert.Equal("100", sorted[0].id);
    }

    // 未收录/非数字的 qn 不抛异常，且落到排序末尾（非 -q 命中的已收录档位顺序保持）
    [Fact]
    public void SortTracks_UnrecognizedQnDoesNotThrow( )
    {
        var tracks = new List<Video> { V("abc", 3000), V("80", 3000), V("127", 9000) };
        var sorted = TrackSelect.SortTracks(tracks, [], [], videoAscending: false, encodingFirst: false);
        Assert.Equal(3, sorted.Count);
        Assert.Equal("127", sorted[0].id);
        Assert.Equal("80", sorted[1].id);
        Assert.Equal("abc", sorted[2].id);
    }
}
