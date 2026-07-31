using System.Linq;
using Xunit;

namespace BBDown.Tests;

// GetAllClips 的分片是多线程下载的基础：任何 off-by-one 都会导致字节缺口或重叠。
public class DownloadClipTests
{
    private const long PerSize = 20 * 1024 * 1024;

    [Theory]
    [InlineData(1)]
    [InlineData(PerSize - 1)]
    [InlineData(PerSize)]
    public void SmallerThanOneChunk_ProducesSingleOpenEndedClip(long fileSize)
    {
        var clips = BBDownDownloadUtil.GetAllClips("", fileSize);

        var clip = Assert.Single(clips);
        Assert.Equal(0, clip.index);
        Assert.Equal(0, clip.from);
        Assert.Equal(-1, clip.to);
    }

    [Fact]
    public void LastClipIsAlwaysOpenEnded()
    {
        var clips = BBDownDownloadUtil.GetAllClips("", PerSize * 5 + 12345);

        Assert.Equal(-1, clips[^1].to);
        Assert.All(clips[..^1], c => Assert.NotEqual(-1, c.to));
    }

    [Fact]
    public void IndexesAreContiguousFromZero()
    {
        var clips = BBDownDownloadUtil.GetAllClips("", PerSize * 7 + 1);

        Assert.Equal(Enumerable.Range(0, clips.Count), clips.Select(c => c.index));
    }

    // Range 请求是闭区间，所以下一片的 from 必须正好是上一片 to 的下一个字节，
    // 既不能留缺口（文件损坏）也不能重叠（字节重复）。
    [Fact]
    public void RangesAreContiguousWithoutGapOrOverlap()
    {
        var clips = BBDownDownloadUtil.GetAllClips("", PerSize * 4 + 999);

        Assert.True(clips.Count > 1);
        for (var i = 1; i < clips.Count; i++)
        {
            Assert.Equal(clips[i - 1].to + 1, clips[i].from);
        }
    }

    [Fact]
    public void FirstClipStartsAtZero()
    {
        var clips = BBDownDownloadUtil.GetAllClips("", PerSize * 3);

        Assert.Equal(0, clips[0].from);
    }

    [Fact]
    public void ZeroOrNegativeSize_ProducesNoClips()
    {
        Assert.Empty(BBDownDownloadUtil.GetAllClips("", 0));
        Assert.Empty(BBDownDownloadUtil.GetAllClips("", -1));
    }
}
