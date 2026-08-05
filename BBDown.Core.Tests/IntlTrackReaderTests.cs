using System.Linq;
using System.Text.Json;

using BBDown.Core.Entity;
using BBDown.Core.PlayUrl;

namespace BBDown.Core.Tests;

public class IntlTrackReaderTests
{
    private static JsonElement Root(string json)
    {
        return JsonDocument.Parse(json).RootElement;
    }

    [Fact]
    public void TryGetVideoInfo_RequiresDataVideoInfoWithStreamList( )
    {
        Assert.True(IntlTrackReader.TryGetVideoInfo(
            Root("""{"data":{"video_info":{"stream_list":[]}}}"""), out _));
        // 有 video_info 但无 stream_list：不算 intl 通道
        Assert.False(IntlTrackReader.TryGetVideoInfo(
            Root("""{"data":{"video_info":{}}}"""), out _));
        // 根本没有 data
        Assert.False(IntlTrackReader.TryGetVideoInfo(
            Root("""{"result":{}}"""), out _));
    }

    // dash_video.base_url 为空的档位应跳过，不产出空 baseUrl 的轨道
    [Fact]
    public void Collect_SkipsStreamsWithEmptyBaseUrl( )
    {
        var root = Root("""{"data":{"video_info":{"timelength":100000,"stream_list":[{"stream_info":{"quality":"80"},"dash_video":{"base_url":"","backup_url":[]}},{"stream_info":{"quality":"127"},"dash_video":{"base_url":"http://ok","backup_url":[],"bandwidth":3000000,"codecid":"7","size":1000}}],"dash_audio":[{"id":"30280","base_url":"http://au","bandwidth":100000,"codecs":"mp4a.40.2","size":500}]}}}""");

        var result = new ParsedResult( );
        Assert.True(IntlTrackReader.TryGetVideoInfo(root, out var videoInfo));
        IntlTrackReader.Collect(result, videoInfo);

        Assert.Equal(["127"], result.VideoTracks.Select(v => v.id).ToList( ));
        Assert.Equal(["30280"], result.AudioTracks.Select(a => a.id).ToList( ));
        Assert.Equal(100, result.Duration);
    }
}
