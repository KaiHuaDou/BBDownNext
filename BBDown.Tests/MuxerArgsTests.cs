using System.Collections.Generic;
using System.Linq;

using static BBDown.Core.Entity.Entity;

namespace BBDown.Tests;

public class MuxerArgsTests
{
    private static readonly string Url = TestVideos.PickRandom( );

    private static Subtitle Sub(string lan, string path)
    {
        return new( ) { lan = lan, url = "", path = path };
    }

    private static string? ValueAfter(List<string> args, string flag)
    {
        var i = args.IndexOf(flag);
        return i >= 0 && i + 1 < args.Count ? args[i + 1] : null;
    }

    [Fact]
    public void BuildFFmpegArgs_EmitsMinimalCommandForVideoPlusAudio( )
    {
        var args = Muxer.BuildFFmpegArgs(Url, "/tmp/v.mp4", "/tmp/a.m4a", [], "/out/x.mp4",
            desc: "", title: "标题", author: "UP主", episodeId: "", pic: "", lang: "",
            subs: [], audioOnly: false, chapterFile: null, pubTime: 0, noMetadata: false,
            tagHvc1: false, debugLog: false);

        Assert.Equal([
            "-loglevel", "warning", "-y",
            "-i", "/tmp/v.mp4", "-i", "/tmp/a.m4a",
            "-map", "0", "-map", "1",
            "-metadata", "title=标题",
            "-metadata", $"comment={Url}",
            "-metadata", "artist=UP主",
            "-c:v", "copy", "-c:a", "copy",
            "-movflags", "faststart", "-strict", "-2", "-f", "mp4", "--", "/out/x.mp4"
        ], args);
    }

    [Fact]
    public void BuildFFmpegArgs_KeepsInjectedQuotesInsideOneArgument( )
    {
        // UP 主昵称来自 B 站接口，含引号时旧的字符串拼装会被 ffmpeg 解析成额外参数
        const string evil = "恶意UP\" -f null - \"";

        var args = Muxer.BuildFFmpegArgs(Url, "/tmp/v.mp4", "", [], "/out/x.mp4",
            desc: "", title: "t", author: evil, episodeId: "", pic: "", lang: "",
            subs: [], audioOnly: false, chapterFile: null, pubTime: 0, noMetadata: false,
            tagHvc1: false, debugLog: false);

        Assert.Contains($"artist={evil}", args);
        Assert.Equal(1, args.Count(a => a == "-f"));
        Assert.Equal("mp4", ValueAfter(args, "-f"));
        Assert.DoesNotContain("null", args);
    }

    [Fact]
    public void BuildFFmpegArgs_SimplyMuxDropsAllMetadataFlags( )
    {
        var args = Muxer.BuildFFmpegArgs(Url, "/tmp/v.mp4", "/tmp/a.m4a", [], "/out/x.mp4",
            desc: "简介", title: "标题", author: "UP主", episodeId: "第1话", pic: "", lang: "zh",
            subs: [], audioOnly: false, chapterFile: null, pubTime: 1600000000, noMetadata: true,
            tagHvc1: false, debugLog: false);

        Assert.DoesNotContain("-metadata", args);
        Assert.DoesNotContain("-metadata:s:a:0", args);
    }

    [Fact]
    public void BuildFFmpegArgs_WritesFullMetadataWhenNotSimplyMux( )
    {
        var args = Muxer.BuildFFmpegArgs(Url, "/tmp/v.mp4", "/tmp/a.m4a", [], "/out/x.mp4",
            desc: "简介", title: "标题", author: "UP主", episodeId: "第1话", pic: "", lang: "zh",
            subs: [], audioOnly: false, chapterFile: null, pubTime: 1600000000, noMetadata: false,
            tagHvc1: false, debugLog: false);

        Assert.Contains("title=第1话", args);
        Assert.Contains("album=标题", args);
        Assert.Contains("description=简介", args);
        Assert.Contains("language=zh", args);
        Assert.Contains("creation_time=2020-09-13T12:26:40.000000Z", args);
    }

    [Fact]
    public void BuildFFmpegArgs_NumbersInputsAndChaptersConsistently( )
    {
        List<AudioMaterial> material = [new( ) { title = "配音", personName = "甲", path = "/tmp/m1.m4a" }];

        var args = Muxer.BuildFFmpegArgs(Url, "/tmp/v.mp4", "/tmp/a.m4a", material, "/out/x.mp4",
            desc: "", title: "标题", author: "", episodeId: "", pic: "/tmp/c.jpg", lang: "",
            subs: [Sub("zh-Hans", "/tmp/s0.srt"), Sub("en-US", "/tmp/s1.srt")],
            audioOnly: false, chapterFile: "/tmp/chapters", pubTime: 0, noMetadata: false,
            tagHvc1: false, debugLog: false);

        // 6 路输入：视频、音频、配音、封面、两条字幕；章节文件是第 7 路，索引为 6
        Assert.Equal(7, args.Count(a => a == "-i"));
        Assert.Equal("6", ValueAfter(args, "-map_chapters"));
        Assert.Equal(["0", "1", "2", "3", "4", "5"],
            args.Select((a, i) => (a, i)).Where(t => t.a == "-map").Select(t => args[t.i + 1]));
    }

    [Fact]
    public void BuildFFmpegArgs_IndexesSubtitleMetadataByStreamOrder( )
    {
        var args = Muxer.BuildFFmpegArgs(Url, "/tmp/v.mp4", "", [], "/out/x.mp4",
            desc: "", title: "t", author: "", episodeId: "", pic: "", lang: "",
            subs: [Sub("zh-Hans", "/tmp/s0.srt"), Sub("en-US", "/tmp/s1.srt")],
            audioOnly: false, chapterFile: null, pubTime: 0, noMetadata: false,
            tagHvc1: false, debugLog: false);

        Assert.Contains("title=中文（简体）", args);
        Assert.Contains("language=chi", args);
        Assert.Contains("title=English(USA)", args);
        Assert.Contains("language=eng", args);
        Assert.Contains("-metadata:s:s:0", args);
        Assert.Contains("-metadata:s:s:1", args);
        Assert.DoesNotContain("-metadata:s:s:2", args);
        Assert.Equal("mov_text", ValueAfter(args, "-c:s"));
    }

    [Fact]
    public void BuildFFmpegArgs_AudioOnlyWithoutAudioTrackDropsVideo( )
    {
        var args = Muxer.BuildFFmpegArgs(Url, "/tmp/v.mp4", "", [], "/out/x.m4a",
            desc: "", title: "t", author: "", episodeId: "", pic: "", lang: "",
            subs: [], audioOnly: true, chapterFile: null, pubTime: 0, noMetadata: false,
            tagHvc1: false, debugLog: false);

        Assert.Contains("-vn", args);
    }

    [Fact]
    public void BuildFFmpegArgs_CoverAddsAttachedPicDisposition( )
    {
        var withVideo = Muxer.BuildFFmpegArgs(Url, "/tmp/v.mp4", "/tmp/a.m4a", [], "/out/x.mp4",
            desc: "", title: "t", author: "", episodeId: "", pic: "/tmp/c.jpg", lang: "",
            subs: [], audioOnly: false, chapterFile: null, pubTime: 0, noMetadata: false,
            tagHvc1: false, debugLog: false);
        var audioOnly = Muxer.BuildFFmpegArgs(Url, "", "/tmp/a.m4a", [], "/out/x.m4a",
            desc: "", title: "t", author: "", episodeId: "", pic: "/tmp/c.jpg", lang: "",
            subs: [], audioOnly: true, chapterFile: null, pubTime: 0, noMetadata: false,
            tagHvc1: false, debugLog: false);

        Assert.Equal("attached_pic", ValueAfter(withVideo, "-disposition:v:1"));
        Assert.Equal("attached_pic", ValueAfter(audioOnly, "-disposition:v:0"));
    }

    [Theory]
    [InlineData(true, "verbose")]
    [InlineData(false, "warning")]
    public void BuildFFmpegArgs_SwitchesLogLevelWithDebugFlag(bool debugLog, string expected)
    {
        var args = Muxer.BuildFFmpegArgs(Url, "/tmp/v.mp4", "", [], "/out/x.mp4",
            desc: "", title: "t", author: "", episodeId: "", pic: "", lang: "",
            subs: [], audioOnly: false, chapterFile: null, pubTime: 0, noMetadata: false,
            tagHvc1: false, debugLog: debugLog);

        Assert.Equal(expected, ValueAfter(args, "-loglevel"));
    }

    [Fact]
    public void BuildMp4boxArgs_EmitsMinimalCommandForVideoPlusAudio( )
    {
        var args = Muxer.BuildMp4boxArgs(Url, "/tmp/v.mp4", "/tmp/a.m4a", "/out/x.mp4",
            desc: "简介", title: "标题", author: "UP主", episodeId: "", pic: "", lang: "",
            subs: [], audioOnly: false, chapterFile: null, debugLog: false);

        Assert.Equal([
            "-inter", "500", "-noprog",
            "-add", "/tmp/v.mp4#trackID=1:name=",
            "-add", "/tmp/a.m4a:lang=und",
            "-itags", $"tool=:title=标题:sdesc=简介:comment={Url}:artist=UP主",
            "-new", "--", "/out/x.mp4"
        ], args);
    }

    [Fact]
    public void BuildMp4boxArgs_AudioOnlyWithoutAudioUsesTrackTwo( )
    {
        var args = Muxer.BuildMp4boxArgs(Url, "/tmp/v.mp4", "", "/out/x.m4a",
            desc: "", title: "t", author: "", episodeId: "", pic: "", lang: "",
            subs: [], audioOnly: true, chapterFile: null, debugLog: false);

        Assert.Contains("/tmp/v.mp4#trackID=2:name=", args);
    }

    [Fact]
    public void BuildMp4boxArgs_NumbersSubtitleUdtaAfterExistingTracks( )
    {
        var args = Muxer.BuildMp4boxArgs(Url, "/tmp/v.mp4", "/tmp/a.m4a", "/out/x.mp4",
            desc: "", title: "t", author: "", episodeId: "", pic: "", lang: "zh",
            subs: [Sub("zh-Hans", "/tmp/s0.srt")], audioOnly: false,
            chapterFile: "/tmp/chapters", debugLog: true);

        Assert.Equal("-v", args[0]);
        Assert.Contains("/tmp/a.m4a:lang=zh", args);
        Assert.Equal("/tmp/chapters", ValueAfter(args, "-chap"));
        Assert.Contains("/tmp/s0.srt#trackID=1:name=:hdlr=sbtl:lang=chi", args);
        Assert.Equal("3:type=name:str=中文（简体）", ValueAfter(args, "-udta"));
    }

    [Fact]
    public void BuildMp4boxArgs_EpisodeIdBecomesTitleAndAlbumHoldsSeries( )
    {
        var args = Muxer.BuildMp4boxArgs(Url, "/tmp/v.mp4", "", "/out/x.mp4",
            desc: "d", title: "剧集名", author: "a", episodeId: "第1话", pic: "/tmp/c.jpg", lang: "",
            subs: [], audioOnly: false, chapterFile: null, debugLog: false);

        Assert.Equal($"tool=:cover=/tmp/c.jpg:album=剧集名:title=第1话:sdesc=d:comment={Url}:artist=a",
            ValueAfter(args, "-itags"));
    }

    [Fact]
    public void BuildMp4boxArgs_KeepsInjectedQuotesInsideOneArgument( )
    {
        var args = Muxer.BuildMp4boxArgs(Url, "/tmp/v.mp4", "", "/out/x.mp4",
            desc: "", title: "标题\" -new /etc/passwd \"", author: "", episodeId: "", pic: "", lang: "",
            subs: [], audioOnly: false, chapterFile: null, debugLog: false);

        Assert.Equal(1, args.Count(a => a == "-new"));
        Assert.DoesNotContain("/etc/passwd", args);
    }
}
