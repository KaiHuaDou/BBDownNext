using System.Linq;
using System.Text.Json;

using BBDown.Core.Entity;
using BBDown.Core.PlayUrl;

namespace BBDown.Core.Tests;

public class FlvTrackReaderTests
{
    private static JsonElement Root(string json)
    {
        return JsonDocument.Parse(json).RootElement;
    }

    [Fact]
    public void ReadAcceptedDfns_PrefersTvQnExtras( )
    {
        using var doc = JsonDocument.Parse("""{"qn_extras":[{"qn":"120"},{"qn":"80"}],"accept_quality":[64]}""");
        Assert.Equal(["120", "80"], FlvTrackReader.ReadAcceptedDfns(doc.RootElement).ToList( ));
    }

    [Fact]
    public void ReadAcceptedDfns_FallsBackToAcceptQualityAndSkipsEmpty( )
    {
        using var doc = JsonDocument.Parse("""{"accept_quality":[80,"",64]}""");
        Assert.Equal(["80", "64"], FlvTrackReader.ReadAcceptedDfns(doc.RootElement).ToList( ));
    }

    [Fact]
    public void ReadAcceptedDfns_NoQualityInfo_ReturnsEmpty( )
    {
        using var doc = JsonDocument.Parse("""{"durl":[]}""");
        Assert.Empty(FlvTrackReader.ReadAcceptedDfns(doc.RootElement));
    }

    // 多分段 durl：分段 URL 全部收集，体积/时长累加，Dfns 取自 qn_extras
    [Fact]
    public void Collect_AccumulatesSegmentsSizeLengthAndDfns( )
    {
        var root = Root("""{"quality":"127","video_codecid":"7","qn_extras":[{"qn":"120"},{"qn":"80"}],"durl":[{"url":"http://seg1","size":1000,"length":5000},{"url":"http://seg2","size":2000,"length":5000}]}""");

        var result = new ParsedResult( );
        FlvTrackReader.Collect(result, root);

        Assert.Equal(["http://seg1", "http://seg2"], result.Clips);
        Assert.Equal(["120", "80"], result.Dfns);
        Assert.Equal(10, result.Duration);

        var v = Assert.Single(result.VideoTracks);
        Assert.Equal("127", v.Id);
        Assert.Equal("8K 超高清", v.Dfn);
        Assert.Equal("AVC", v.Codecs);
        Assert.Equal(10, v.Dur);
        Assert.Equal(3000, v.Size);
    }
}
