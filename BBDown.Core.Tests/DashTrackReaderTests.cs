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

    // pDur 取响应自身的 timelength，作为充电专属试看的判据（等价点 A）
    [Fact]
    public void Collect_TakesDurationFromRoot( )
    {
        var root = Root("""{"timelength":125000,"dash":{"video":[]}}""");

        var result = new ParsedResult( );
        DashTrackReader.Collect(result, root, tvApi: false);

        Assert.Equal(125, result.Duration);
    }

    // 单份响应已含全部可用档位：视频轨全部收集，重复轨道按 Equals 去重
    [Fact]
    public void Collect_CollectsAllVideoTracksAndDedups( )
    {
        const string video80 = """{"id":"80","base_url":"http://a","backup_url":["http://b"],"bandwidth":3000000,"codecid":"7","size":1000,"width":1920,"height":1080,"frame_rate":"30"}""";
        const string video127 = """{"id":"127","base_url":"http://c","backup_url":[],"bandwidth":9000000,"codecid":"12","size":3000,"width":3840,"height":2160,"frame_rate":"30"}""";

        var root = Root("""{"timelength":125000,"dash":{"video":[""" + video80 + "," + video127 + "," + video80 + """]}}""");

        var result = new ParsedResult( );
        DashTrackReader.Collect(result, root, tvApi: false);

        var ids = result.VideoTracks.Select(v => v.Id).ToList( );
        Assert.Equal(2, ids.Count);
        Assert.Contains("80", ids);
        Assert.Contains("127", ids);
    }

    // 单份响应同时携带音视频轨时，音轨一并收集
    [Fact]
    public void Collect_CollectsAudioFromRoot( )
    {
        const string audio = """{"id":"30264","base_url":"http://au","bandwidth":64000,"codecs":"mp4a.40.2","size":300}""";
        var root = Root("""{"timelength":100000,"dash":{"video":[{"id":"64","base_url":"http://v","bandwidth":1,"codecid":"7","size":1,"width":1920,"height":1080,"frame_rate":"30"}],"audio":[""" + audio + """]}}""");

        var result = new ParsedResult( );
        DashTrackReader.Collect(result, root, tvApi: false);

        Assert.Equal(["30264"], result.AudioTracks.Select(a => a.Id).ToList( ));
    }

    // dash.Audio 为 null 但存在 dolby 节点时，仍要收集杜比音轨（tvApi=false）
    [Fact]
    public void Collect_CollectsDolbyAudioWhenDashAudioMissing( )
    {
        var root = Root("""{"timelength":100000,"dash":{"video":[{"id":"80","base_url":"http://v","bandwidth":1,"codecid":"7","size":1,"width":1920,"height":1080,"frame_rate":"30"}],"dolby":{"audio":[{"id":"30250","base_url":"http://dolby","bandwidth":200000,"codecs":"ec-3","size":800}]}}}""");

        var result = new ParsedResult( );
        DashTrackReader.Collect(result, root, tvApi: false);

        Assert.Contains("30250", result.AudioTracks.Select(a => a.Id));
    }

    // tvApi=true 时不写 res/fps，且跳过 dolby/flac 音轨
    [Fact]
    public void Collect_TvApiSkipsResolutionAndDolby( )
    {
        var root = Root("""{"timelength":100000,"dash":{"video":[{"id":"80","base_url":"http://v","bandwidth":1,"codecid":"7","size":1,"width":1920,"height":1080,"frame_rate":"30"}],"dolby":{"audio":[{"id":"30250","base_url":"http://dolby","bandwidth":200000,"codecs":"ec-3","size":800}]}}}""");

        var result = new ParsedResult( );
        DashTrackReader.Collect(result, root, tvApi: true);

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

    // dolby.type=2 标为「杜比全景声」，不再只标杜比音效
    [Fact]
    public void Collect_DolbyType2LabelsAtmos( )
    {
        var root = Root("""{"timelength":100000,"dash":{"video":[{"id":"80","base_url":"http://v","bandwidth":1,"codecid":"7","size":1,"width":1920,"height":1080,"frame_rate":"30"}],"dolby":{"type":2,"audio":[{"id":"30250","base_url":"http://dolby","bandwidth":200000,"codecs":"ec-3","size":800}]}}}""");

        var result = new ParsedResult( );
        DashTrackReader.Collect(result, root, tvApi: false);

        var dolby = Assert.Single(result.AudioTracks, a => a.Id == "30250");
        Assert.Equal("杜比全景声", dolby.Dfn);
    }

    // dolby.type=1 标为「杜比音效」
    [Fact]
    public void Collect_DolbyType1LabelsDolbyAudio( )
    {
        var root = Root("""{"timelength":100000,"dash":{"video":[{"id":"80","base_url":"http://v","bandwidth":1,"codecid":"7","size":1,"width":1920,"height":1080,"frame_rate":"30"}],"dolby":{"type":1,"audio":[{"id":"30250","base_url":"http://dolby","bandwidth":200000,"codecs":"ec-3","size":800}]}}}""");

        var result = new ParsedResult( );
        DashTrackReader.Collect(result, root, tvApi: false);

        var dolby = Assert.Single(result.AudioTracks, a => a.Id == "30250");
        Assert.Equal("杜比音效", dolby.Dfn);
    }

    // flac.audio 标为「Hi-Res 无损」
    [Fact]
    public void Collect_FlacLabelsHiRes( )
    {
        var root = Root("""{"timelength":100000,"dash":{"video":[{"id":"80","base_url":"http://v","bandwidth":1,"codecid":"7","size":1,"width":1920,"height":1080,"frame_rate":"30"}],"flac":{"audio":{"id":"30251","base_url":"http://flac","bandwidth":900000,"codecs":"flac","size":9000}}}}""");

        var result = new ParsedResult( );
        DashTrackReader.Collect(result, root, tvApi: false);

        var hiRes = Assert.Single(result.AudioTracks, a => a.Id == "30251");
        Assert.Equal("Hi-Res 无损", hiRes.Dfn);
    }

    // 普通音轨按 id 标为「192K」，不受 dolby/flac 影响
    [Fact]
    public void Collect_PlainAudioLabelsByBitrate( )
    {
        var root = Root("""{"timelength":100000,"dash":{"video":[{"id":"80","base_url":"http://v","bandwidth":1,"codecid":"7","size":1,"width":1920,"height":1080,"frame_rate":"30"}],"audio":[{"id":"30280","base_url":"http://au","bandwidth":192000,"codecs":"mp4a.40.2","size":300}]}}""");

        var result = new ParsedResult( );
        DashTrackReader.Collect(result, root, tvApi: false);

        var audio = Assert.Single(result.AudioTracks, a => a.Id == "30280");
        Assert.Equal("192K", audio.Dfn);
    }
}