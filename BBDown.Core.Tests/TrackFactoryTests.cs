using System.Collections.Generic;
using System.Text.Json;

using BBDown.Core.PlayUrl;

namespace BBDown.Core.Tests;

public class TrackFactoryTests
{
    private static JsonElement Parse(string json)
    {
        return JsonDocument.Parse(json).RootElement.Clone( );
    }

    [Fact]
    public void BuildUrlList_MergesBaseAndBackupUrls( )
    {
        var list = TrackFactory.BuildUrlList(Parse("""{"base_url":"http://main","backup_url":["http://b1","http://b2"]}"""));
        Assert.Equal(["http://main", "http://b1", "http://b2"], list);
    }

    [Theory]
    [InlineData("""{"base_url":"http://main"}""")]
    [InlineData("""{"base_url":"http://main","backup_url":null}""")]
    public void BuildUrlList_NoUsableBackupUrl(string json)
    {
        Assert.Equal(["http://main"], TrackFactory.BuildUrlList(Parse(json)));
    }

    [Fact]
    public void PickBaseUrl_PrefersUrlWithoutPort( )
    {
        // 带端口的 url（P2P/mcdn）被跳过，选第一个不带端口的
        List<string> list = ["https://xy1.mcdn.bilivideo.cn:8082/v.m4s", "https://upos-sz.bilivideo.com/v.m4s"];
        Assert.Equal("https://upos-sz.bilivideo.com/v.m4s", TrackFactory.PickBaseUrl(list));
    }

    [Fact]
    public void PickBaseUrl_AllHavePorts_FallsBackToFirst( )
    {
        List<string> list = ["https://a:8082/v.m4s", "https://b:4483/v.m4s"];
        Assert.Equal("https://a:8082/v.m4s", TrackFactory.PickBaseUrl(list));
    }

    [Fact]
    public void BuildVideo_ReadsIdFromNodeAndScalesBandwidth( )
    {
        var v = TrackFactory.BuildVideo(Parse("""{"id":80,"bandwidth":2048000,"codecid":7,"size":12345,"base_url":"http://v"}"""), 120);

        Assert.Equal("80", v.id);
        Assert.Equal("1080P 高清", v.dfn);
        Assert.Equal(2048, v.bandwidth);
        Assert.Equal("AVC", v.codecs);
        Assert.Equal(12345, v.size);
        Assert.Equal(120, v.dur);
        Assert.Equal("http://v", v.baseUrl);
    }

    // intl 接口的清晰度不在 dash_video 节点上，必须由调用方传入
    [Fact]
    public void BuildVideo_ExplicitIdOverridesNodeAndMissingSizeIsZero( )
    {
        var v = TrackFactory.BuildVideo(Parse("""{"id":16,"bandwidth":1000,"codecid":13,"base_url":"http://v"}"""), 0, "127");

        Assert.Equal("127", v.id);
        Assert.Equal("AV1", v.codecs);
        Assert.Equal(0, v.size);
    }

    [Fact]
    public void BuildAudio_UsesNodeCodecsWhenNotOverridden( )
    {
        const string Json = """{"id":30280,"bandwidth":128000,"codecs":"mp4a.40.2","base_url":"http://a"}""";

        Assert.Equal("mp4a.40.2", TrackFactory.BuildAudio(Parse(Json), 120).codecs);
        Assert.Equal("M4A", TrackFactory.BuildAudio(Parse(Json), 120, "M4A").codecs);

        var a = TrackFactory.BuildAudio(Parse(Json), 120);
        Assert.Equal("30280", a.id);
        Assert.Equal("30280", a.dfn);
        Assert.Equal(128, a.bandwidth);
    }

    [Theory]
    [InlineData("mp4a.40.2", "M4A")]
    [InlineData("mp4a.40.5", "M4A")]
    [InlineData("ec-3", "E-AC-3")]
    [InlineData("fLaC", "FLAC")]
    [InlineData("opus", "opus")]
    public void NormalizeAudioCodec_MapsKnownCodecs(string raw, string expected)
    {
        Assert.Equal(expected, TrackFactory.NormalizeAudioCodec(raw));
    }

    [Theory]
    [InlineData("13", "AV1")]
    [InlineData("12", "HEVC")]
    [InlineData("7", "AVC")]
    [InlineData("99", "UNKNOWN")]
    public void VideoCodec_MapsCodecId(string code, string expected)
    {
        Assert.Equal(expected, TrackFactory.VideoCodec(code));
    }

    // ═══ DRM 字段解析 ═══

    [Fact]
    public void BuildVideo_ReadsBiliDrmUri( )
    {
        var v = TrackFactory.BuildVideo(Parse("""{"id":80,"bandwidth":2048000,"codecid":12,"base_url":"http://v","bilidrm_uri":"uri:bili://d8f66b93db284984b4e7fc50d71278ff"}"""), 120);

        Assert.True(v.IsDrm);
        Assert.Equal("bili_drm", v.DrmType);
        Assert.Equal("uri:bili://d8f66b93db284984b4e7fc50d71278ff", v.BiliDrmUri);
        Assert.Null(v.WidevinePssh);
    }

    [Fact]
    public void BuildVideo_ReadsWidevinePssh_InfersTypeWhenFieldMissing( )
    {
        var v = TrackFactory.BuildVideo(Parse("""{"id":80,"bandwidth":2048000,"codecid":12,"base_url":"http://v","widevine_pssh":"AAAAdXBzc2gA"}"""), 120);

        Assert.True(v.IsDrm);
        Assert.Equal("widevine", v.DrmType);
        Assert.Equal("AAAAdXBzc2gA", v.WidevinePssh);
        Assert.Null(v.BiliDrmUri);
    }

    [Fact]
    public void BuildVideo_DrmTypeFieldTakesPrecedence( )
    {
        var v = TrackFactory.BuildVideo(Parse("""{"id":80,"bandwidth":2048000,"codecid":12,"base_url":"http://v","drm_type":"widevine","bilidrm_uri":"uri:bili://d8f66b93db284984b4e7fc50d71278ff"}"""), 120);

        Assert.Equal("widevine", v.DrmType);
    }

    [Fact]
    public void BuildVideo_NoDrmFields( )
    {
        var v = TrackFactory.BuildVideo(Parse("""{"id":80,"bandwidth":2048000,"codecid":7,"base_url":"http://v"}"""), 120);

        Assert.False(v.IsDrm);
        Assert.Equal("", v.DrmType);
        Assert.Null(v.WidevinePssh);
        Assert.Null(v.BiliDrmUri);
    }

    [Fact]
    public void BuildVideo_NullDrmFieldsAreIgnored( )
    {
        var v = TrackFactory.BuildVideo(Parse("""{"id":80,"bandwidth":2048000,"codecid":7,"base_url":"http://v","bilidrm_uri":null,"widevine_pssh":null}"""), 120);

        Assert.False(v.IsDrm);
        Assert.Equal("", v.DrmType);
    }

    [Fact]
    public void BuildAudio_ReadsBiliDrmUri( )
    {
        var a = TrackFactory.BuildAudio(Parse("""{"id":30280,"bandwidth":128000,"codecs":"mp4a.40.2","base_url":"http://a","bilidrm_uri":"uri:bili://d8f66b93db284984b4e7fc50d71278ff"}"""), 120);

        Assert.True(a.IsDrm);
        Assert.Equal("bili_drm", a.DrmType);
        Assert.Equal("uri:bili://d8f66b93db284984b4e7fc50d71278ff", a.BiliDrmUri);
    }

    [Fact]
    public void BuildAudio_NoDrmFields( )
    {
        var a = TrackFactory.BuildAudio(Parse("""{"id":30280,"bandwidth":128000,"codecs":"mp4a.40.2","base_url":"http://a"}"""), 120);

        Assert.False(a.IsDrm);
        Assert.Equal("", a.DrmType);
    }
}
