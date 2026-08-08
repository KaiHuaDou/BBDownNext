using System.Collections.Generic;
using System.Linq;

using BBDown.Core;
using BBDown.Core.Entity;

namespace BBDown.Tests;

public class MuxerArgsTests
{
    // 普通的 BV 号（与生产中 p.Bvid 一致）。Build* 会把它拼成完整视频页 URL 写入 comment 元数据，
    // 故断言也按拼装后的形态校验，而不是传入的完整 URL 原样回声。
    private const string Bvid = "BV1hY411J7cA";

    private static Subtitle Sub(string lan, string path)
    {
        return new( ) { Lan = lan, Url = "", Path = path };
    }

    // 用窄 MuxRequest 组装混流入参；
    // Build* 仅读取 req 上的路径/元数据，Tools/Points/IsHevc 等字段不影响本测试断言，给安全默认值。
    private static MuxRequest Req(
        string bvid, string videoPath, string audioPath,
        List<AudioMaterial>? audioMaterial = null, string outPath = "", string desc = "", string title = "",
        string author = "", string episodeId = "", string pic = "", string lang = "",
        List<Subtitle>? subs = null, DownloadContent content = DownloadContent.Audio | DownloadContent.Video | DownloadContent.MuxMetadata,
        long pubTime = 0)
        => new(
            UseMp4box: false,
            Bvid: bvid,
            VideoPath: videoPath,
            AudioPath: audioPath,
            AudioMaterial: audioMaterial ?? [ ],
            OutPath: outPath,
            Tools: default,
            Desc: desc,
            Title: title,
            Author: author,
            EpisodeId: episodeId,
            Pic: pic,
            Lang: lang,
            Subs: subs,
            Content: content,
            Points: null,
            PubTime: pubTime,
            IsHevc: false);

    private static string? ValueAfter(List<string> args, string flag)
    {
        var i = args.IndexOf(flag);
        return i >= 0 && i + 1 < args.Count ? args[i + 1] : null;
    }

    [Fact]
    public void BuildFFmpegArgs_EmitsMinimalCommandForVideoPlusAudio( )
    {
        var req = Req(Bvid, "/tmp/v.mp4", "/tmp/a.m4a", outPath: "/out/x.mp4", title: "标题", author: "UP主");
        var args = Muxer.BuildFFmpegArgs(req, null, false);

        Assert.Equal([
            "-loglevel", "warning", "-y",
            "-i", "/tmp/v.mp4", "-i", "/tmp/a.m4a",
            "-map", "0", "-map", "1",
            "-metadata", "title=标题",
            "-metadata", $"comment={BiliApi.VideoPage}/{Bvid}/",
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

        var req = Req(Bvid, "/tmp/v.mp4", "", outPath: "/out/x.mp4", title: "t", author: evil);
        var args = Muxer.BuildFFmpegArgs(req, null, false);

        Assert.Contains($"artist={evil}", args);
        Assert.Equal(1, args.Count(a => a == "-f"));
        Assert.Equal("mp4", ValueAfter(args, "-f"));
        Assert.DoesNotContain("null", args);
    }

    [Fact]
    public void BuildFFmpegArgs_SimplyMuxDropsAllMetadataFlags( )
    {
        var req = Req(Bvid, "/tmp/v.mp4", "/tmp/a.m4a", outPath: "/out/x.mp4", desc: "简介", title: "标题", author: "UP主", episodeId: "第1话", lang: "zh",
            content: DownloadContent.Audio | DownloadContent.Video);
        var args = Muxer.BuildFFmpegArgs(req, null, false);

        Assert.DoesNotContain("-metadata", args);
        Assert.DoesNotContain("-metadata:s:a:0", args);
    }

    [Fact]
    public void BuildFFmpegArgs_WritesFullMetadataWhenNotSimplyMux( )
    {
        var req = Req(Bvid, "/tmp/v.mp4", "/tmp/a.m4a", outPath: "/out/x.mp4", desc: "简介", title: "标题", author: "UP主", episodeId: "第1话", lang: "zh", pubTime: 1600000000);
        var args = Muxer.BuildFFmpegArgs(req, null, false);

        Assert.Contains("title=第1话", args);
        Assert.Contains("album=标题", args);
        Assert.Contains("description=简介", args);
        Assert.Contains("language=zh", args);
        Assert.Contains("creation_time=2020-09-13T12:26:40.000000Z", args);
    }

    [Fact]
    public void BuildFFmpegArgs_NumbersInputsAndChaptersConsistently( )
    {
        List<AudioMaterial> material = [new( ) { Title = "配音", PersonName = "甲", Path = "/tmp/m1.m4a" }];

        var req = Req(Bvid, "/tmp/v.mp4", "/tmp/a.m4a", audioMaterial: material, outPath: "/out/x.mp4",
            pic: "/tmp/c.jpg", subs: [Sub("zh-Hans", "/tmp/s0.srt"), Sub("en-US", "/tmp/s1.srt")]);
        var args = Muxer.BuildFFmpegArgs(req, "/tmp/chapters", false);

        // 6 路输入：视频、音频、配音、封面、两条字幕；章节文件是第 7 路，索引为 6
        Assert.Equal(7, args.Count(a => a == "-i"));
        Assert.Equal("6", ValueAfter(args, "-map_chapters"));
        Assert.Equal(["0", "1", "2", "3", "4", "5"],
            args.Select((a, i) => (a, i)).Where(t => t.a == "-map").Select(t => args[t.i + 1]));
    }

    [Fact]
    public void BuildFFmpegArgs_IndexesSubtitleMetadataByStreamOrder( )
    {
        var req = Req(Bvid, "/tmp/v.mp4", "", outPath: "/out/x.mp4", title: "t",
            subs: [Sub("zh-Hans", "/tmp/s0.srt"), Sub("en-US", "/tmp/s1.srt")]);
        var args = Muxer.BuildFFmpegArgs(req, null, false);

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
        var req = Req(Bvid, "/tmp/v.mp4", "", outPath: "/out/x.m4a", title: "t", content: DownloadContent.Audio);
        var args = Muxer.BuildFFmpegArgs(req, null, false);

        Assert.Contains("-vn", args);
    }

    [Fact]
    public void BuildFFmpegArgs_CoverAddsAttachedPicDisposition( )
    {
        var withVideo = Req(Bvid, "/tmp/v.mp4", "/tmp/a.m4a", outPath: "/out/x.mp4", title: "t", pic: "/tmp/c.jpg");
        var withVideoArgs = Muxer.BuildFFmpegArgs(withVideo, null, false);
        var audioOnly = Req(Bvid, "", "/tmp/a.m4a", outPath: "/out/x.m4a", title: "t", pic: "/tmp/c.jpg", content: DownloadContent.Audio);
        var audioOnlyArgs = Muxer.BuildFFmpegArgs(audioOnly, null, false);

        Assert.Equal("attached_pic", ValueAfter(withVideoArgs, "-disposition:v:1"));
        Assert.Equal("attached_pic", ValueAfter(audioOnlyArgs, "-disposition:v:0"));
    }

    [Theory]
    [InlineData(true, "verbose")]
    [InlineData(false, "warning")]
    public void BuildFFmpegArgs_SwitchesLogLevelWithDebugFlag(bool debugLog, string expected)
    {
        var req = Req(Bvid, "/tmp/v.mp4", "", outPath: "/out/x.mp4", title: "t");
        var args = Muxer.BuildFFmpegArgs(req, null, debugLog);

        Assert.Equal(expected, ValueAfter(args, "-loglevel"));
    }

    [Fact]
    public void BuildMp4boxArgs_EmitsMinimalCommandForVideoPlusAudio( )
    {
        var req = Req(Bvid, "/tmp/v.mp4", "/tmp/a.m4a", outPath: "/out/x.mp4", desc: "简介", title: "标题", author: "UP主");
        var args = Muxer.BuildMp4boxArgs(req, null, false);

        Assert.Equal([
            "-inter", "500", "-noprog",
            "-add", "/tmp/v.mp4#trackID=1:name=",
            "-add", "/tmp/a.m4a:lang=und",
            "-itags", $"tool=:title=标题:sdesc=简介:comment={BiliApi.VideoPage}/{Bvid}/:artist=UP主",
            "-new", "--", "/out/x.mp4"
        ], args);
    }

    [Fact]
    public void BuildMp4boxArgs_AudioOnlyWithoutAudioUsesTrackTwo( )
    {
        var req = Req(Bvid, "/tmp/v.mp4", "", outPath: "/out/x.m4a", title: "t", content: DownloadContent.Audio);
        var args = Muxer.BuildMp4boxArgs(req, null, false);

        Assert.Contains("/tmp/v.mp4#trackID=2:name=", args);
    }

    [Fact]
    public void BuildMp4boxArgs_NumbersSubtitleUdtaAfterExistingTracks( )
    {
        var req = Req(Bvid, "/tmp/v.mp4", "/tmp/a.m4a", outPath: "/out/x.mp4", title: "t", lang: "zh",
            subs: [Sub("zh-Hans", "/tmp/s0.srt")]);
        var args = Muxer.BuildMp4boxArgs(req, "/tmp/chapters", true);

        Assert.Equal("-v", args[0]);
        Assert.Contains("/tmp/a.m4a:lang=zh", args);
        Assert.Equal("/tmp/chapters", ValueAfter(args, "-chap"));
        Assert.Contains("/tmp/s0.srt#trackID=1:name=:hdlr=sbtl:lang=chi", args);
        Assert.Equal("3:type=name:str=中文（简体）", ValueAfter(args, "-udta"));
    }

    [Fact]
    public void BuildMp4boxArgs_EpisodeIdBecomesTitleAndAlbumHoldsSeries( )
    {
        var req = Req(Bvid, "/tmp/v.mp4", "", outPath: "/out/x.mp4", desc: "d", title: "剧集名", author: "a", episodeId: "第1话", pic: "/tmp/c.jpg");
        var args = Muxer.BuildMp4boxArgs(req, null, false);

        Assert.Equal($"tool=:cover=/tmp/c.jpg:album=剧集名:title=第1话:sdesc=d:comment={BiliApi.VideoPage}/{Bvid}/:artist=a",
            ValueAfter(args, "-itags"));
    }

    [Fact]
    public void BuildMp4boxArgs_KeepsInjectedQuotesInsideOneArgument( )
    {
        var req = Req(Bvid, "/tmp/v.mp4", "", outPath: "/out/x.mp4", title: "标题\" -new /etc/passwd \"");
        var args = Muxer.BuildMp4boxArgs(req, null, false);

        Assert.Equal(1, args.Count(a => a == "-new"));
        Assert.DoesNotContain("/etc/passwd", args);
    }
}