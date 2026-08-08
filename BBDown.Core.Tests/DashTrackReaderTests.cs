using System.Linq;
using System.Text.Json;

using BBDown.Core.Entity;
using BBDown.Core.PlayUrl;


namespace BBDown.Core.Tests;

public class DashTrackReaderTests
{
    private static JsonElement Root(string json)
    {
        return JsonDocument.Parse(json).RootElement;
    }

    [Fact]
    public void ReadDuration_PrefersTimelengthOverDashDuration( )
    {
        using var doc = JsonDocument.Parse("""{"timelength":125000,"dash":{"duration":999}}""");
        Assert.Equal(125, DashTrackReader.ReadDuration(doc.RootElement));
    }

    // 等价点 A：pDur 取首次响应，而非免二压的 MaxQn 二次响应
    [Fact]
    public void Collect_TakesDurationFromFirstRoot( )
    {
        var first = Root("""{"timelength":125000,"dash":{"video":[]}}""");
        var maxQn = Root("""{"timelength":999000,"dash":{"video":[]}}""");

        var result = new ParsedResult( );
        DashTrackReader.Collect(result, first, maxQn, tvApi: false);

        Assert.Equal(125, result.Duration);
    }

    // 免二压视频：首次响应没有的更高档位只在 MaxQn 二次响应出现，视频轨取两次并集（按 Equals 去重）
    [Fact]
    public void Collect_UnionsVideoTracksFromBothResponsesAndDedups( )
    {
        const string video80 = """{"id":"80","base_url":"http://a","backup_url":["http://b"],"bandwidth":3000000,"codecid":"7","size":1000,"width":1920,"height":1080,"frame_rate":"30"}""";
        const string video127 = """{"id":"127","base_url":"http://c","backup_url":[],"bandwidth":9000000,"codecid":"12","size":3000,"width":3840,"height":2160,"frame_rate":"30"}""";

        var first = Root("""{"timelength":125000,"dash":{"video":[""" + video80 + """]}}""");
        var maxQn = Root("""{"timelength":125000,"dash":{"video":[""" + video80 + "," + video127 + """]}}""");

        var result = new ParsedResult( );
        DashTrackReader.Collect(result, first, maxQn, tvApi: false);

        var ids = result.VideoTracks.Select(v => v.Id).ToList( );
        Assert.Equal(2, ids.Count);
        Assert.Contains("80", ids);
        Assert.Contains("127", ids);
    }

    // 二次响应降级（无 dash/音轨）时，音轨回退到首次响应而不是被丢弃
    [Fact]
    public void Collect_FallsBackToFirstRootAudioWhenMaxQnHasNoAudio( )
    {
        const string audio = """{"id":"30264","base_url":"http://au","bandwidth":64000,"codecs":"mp4a.40.2","size":300}""";
        var first = Root("""{"timelength":100000,"dash":{"video":[{"id":"64","base_url":"http://v","bandwidth":1,"codecid":"7","size":1,"width":1920,"height":1080,"frame_rate":"30"}],"audio":[""" + audio + """]}}""");
        var maxQn = Root("""{"timelength":100000,"dash":{"video":[{"id":"127","base_url":"http://v2","bandwidth":1,"codecid":"12","size":1,"width":3840,"height":2160,"frame_rate":"30"}]}}""");

        var result = new ParsedResult( );
        DashTrackReader.Collect(result, first, maxQn, tvApi: false);

        Assert.Equal(["30264"], result.AudioTracks.Select(a => a.Id).ToList( ));
    }

    // dash.Audio 为 null 但存在 dolby 节点时，仍要收集杜比音轨（tvApi=false）
    [Fact]
    public void Collect_CollectsDolbyAudioWhenDashAudioMissing( )
    {
        var first = Root("""{"timelength":100000,"dash":{"video":[{"id":"80","base_url":"http://v","bandwidth":1,"codecid":"7","size":1,"width":1920,"height":1080,"frame_rate":"30"}],"dolby":{"audio":[{"id":"30250","base_url":"http://dolby","bandwidth":200000,"codecs":"ec-3","size":800}]}}}""");

        var result = new ParsedResult( );
        DashTrackReader.Collect(result, first, first, tvApi: false);

        Assert.Contains("30250", result.AudioTracks.Select(a => a.Id));
    }

    // tvApi=true 时不写 res/fps，且跳过 dolby/flac 音轨
    [Fact]
    public void Collect_TvApiSkipsResolutionAndDolby( )
    {
        var first = Root("""{"timelength":100000,"dash":{"video":[{"id":"80","base_url":"http://v","bandwidth":1,"codecid":"7","size":1,"width":1920,"height":1080,"frame_rate":"30"}],"dolby":{"audio":[{"id":"30250","base_url":"http://dolby","bandwidth":200000,"codecs":"ec-3","size":800}]}}}""");

        var result = new ParsedResult( );
        DashTrackReader.Collect(result, first, first, tvApi: true);

        var v = Assert.Single(result.VideoTracks);
        Assert.Null(v.Res);
        Assert.Null(v.Fps);
        Assert.DoesNotContain("30250", result.AudioTracks.Select(a => a.Id));
    }

    // support_formats 声明了智能修复、dash 却无对应轨道 => 账号权限不够
    [Fact]
    public void DeclaredButMissing_DeclaredWithoutTrack_ReturnsTrue( )
    {
        var root = Root("""{"support_formats":[{"quality":100,"new_description":"智能修复"}]}""");
        var result = new ParsedResult( );
        Assert.True(DashTrackReader.DeclaredButMissing(root, result, "100"));
    }

    [Fact]
    public void DeclaredButMissing_DeclaredWithTrack_ReturnsFalse( )
    {
        var root = Root("""{"support_formats":[{"quality":100,"new_description":"智能修复"}]}""");
        var result = new ParsedResult( );
        result.VideoTracks.Add(new Video { Id = "100", Dfn = "智能修复", BaseUrl = "", Codecs = "AVC" });
        Assert.False(DashTrackReader.DeclaredButMissing(root, result, "100"));
    }

    [Fact]
    public void DeclaredButMissing_NoSupportFormats_ReturnsFalse( )
    {
        var root = Root("""{"dash":{"video":[]}}""");
        var result = new ParsedResult( );
        Assert.False(DashTrackReader.DeclaredButMissing(root, result, "100"));
    }

    [Fact]
    public void DeclaredButMissing_EmptySupportFormats_ReturnsFalse( )
    {
        var root = Root("""{"support_formats":[]}""");
        var result = new ParsedResult( );
        Assert.False(DashTrackReader.DeclaredButMissing(root, result, "100"));
    }
}