using System.Collections.Generic;
using System.Linq;

using BBDown.Live;

namespace BBDown.Tests;

public class LiveMuxerArgsTests
{
    [Fact]
    public void BuildLiveToTsArgs_Avc_UsesH264Bsf( )
    {
        var args = LiveMuxer.BuildLiveToTsArgs("/tmp/a.001.bbdown.part", "/tmp/a.001.bbdown.ts", "avc", debugLog: false);

        Assert.Equal([
            "-loglevel", "error", "-y",
            "-fflags", "+genpts+discardcorrupt", "-err_detect", "ignore_err",
            "-i", "/tmp/a.001.bbdown.part",
            "-map", "0", "-c", "copy",
            "-f", "mpegts", "-bsf:v", "h264_mp4toannexb",
            "--", "/tmp/a.001.bbdown.ts"
        ], args);
    }

    // 用 h264 的 bsf 处理 hevc 会让 ffmpeg 直接失败，整段录像作废
    [Fact]
    public void BuildLiveToTsArgs_Hevc_UsesHevcBsf( )
    {
        var args = LiveMuxer.BuildLiveToTsArgs("/tmp/a.part", "/tmp/a.ts", "hevc", debugLog: false);
        Assert.Equal("hevc_mp4toannexb", ValueAfter(args, "-bsf:v"));
    }

    // 编码未知时宁可不指定，让 ffmpeg 自行插入合适的 bsf
    [Theory]
    [InlineData("")]
    [InlineData("av1")]
    [InlineData("AVC")]
    public void BuildLiveToTsArgs_UnknownCodec_OmitsBsf(string codec)
    {
        var args = LiveMuxer.BuildLiveToTsArgs("/tmp/a.part", "/tmp/a.ts", codec, debugLog: false);
        Assert.DoesNotContain("-bsf:v", args);
    }

    [Theory]
    [InlineData("avc", "h264_mp4toannexb")]
    [InlineData("h264", "h264_mp4toannexb")]
    [InlineData("hevc", "hevc_mp4toannexb")]
    [InlineData("h265", "hevc_mp4toannexb")]
    [InlineData("av1", null)]
    public void SelectBitstreamFilter_MapsCodecName(string codec, string? expected)
    {
        Assert.Equal(expected, LiveMuxer.SelectBitstreamFilter(codec));
    }

    [Fact]
    public void BuildLiveRemuxArgs_WithFaststart( )
    {
        var args = LiveMuxer.BuildLiveRemuxArgs("/tmp/a.concat.ts", "/out/x.mp4", faststart: true, debugLog: false);

        Assert.Equal([
            "-loglevel", "error", "-y",
            "-fflags", "+genpts+discardcorrupt", "-err_detect", "ignore_err",
            "-i", "/tmp/a.concat.ts",
            "-map", "0", "-c", "copy",
            "-movflags", "+faststart",
            "-f", "mp4", "--", "/out/x.mp4"
        ], args);
    }

    [Fact]
    public void BuildLiveRemuxArgs_WithoutFaststart_OmitsMovflags( )
    {
        var args = LiveMuxer.BuildLiveRemuxArgs("/tmp/a.ts", "/out/x.mp4", faststart: false, debugLog: false);
        Assert.DoesNotContain("-movflags", args);
        Assert.Equal(["-f", "mp4", "--", "/out/x.mp4"], args.TakeLast(4));
    }

    // 直播时间戳会跳变/回绕，丢了 +genpts 会得到时长错误、无法 seek 的 mp4
    [Fact]
    public void BuildLiveRemuxArgs_AlwaysRegeneratesPts( )
    {
        var args = LiveMuxer.BuildLiveRemuxArgs("/tmp/a.ts", "/out/x.mp4", faststart: false, debugLog: false);
        var fflags = ValueAfter(args, "-fflags");
        Assert.NotNull(fflags);
        Assert.Contains("+genpts", fflags);
        // -fflags 必须在 -i 之前，作为输入选项才生效
        Assert.True(args.IndexOf("-fflags") < args.IndexOf("-i"));
    }

    // 停录会把分段截在半个 FLV tag 上，必须容忍损坏包，否则合并满屏报错甚至整段失败
    [Fact]
    public void BuildArgs_ToleratesCorruptSource( )
    {
        var ts = LiveMuxer.BuildLiveToTsArgs("in", "out.ts", "avc", debugLog: false);
        Assert.Equal("+genpts+discardcorrupt", ValueAfter(ts, "-fflags"));
        Assert.Equal("ignore_err", ValueAfter(ts, "-err_detect"));
        Assert.True(ts.IndexOf("-fflags") < ts.IndexOf("-i"));
        Assert.True(ts.IndexOf("-err_detect") < ts.IndexOf("-i"));
        // 非调试路径不再刷 warning 级 demux 噪声（如 Track size mismatch / corrupt input packet）
        Assert.Equal("error", ValueAfter(ts, "-loglevel"));

        var mp4 = LiveMuxer.BuildLiveRemuxArgs("in", "out.mp4", faststart: true, debugLog: false);
        Assert.Equal("+genpts+discardcorrupt", ValueAfter(mp4, "-fflags"));
        Assert.Equal("ignore_err", ValueAfter(mp4, "-err_detect"));
        Assert.Equal("error", ValueAfter(mp4, "-loglevel"));
    }

    [Fact]
    public void BuildArgs_DebugLog_SwitchesToVerbose( )
    {
        Assert.Equal("verbose", ValueAfter(LiveMuxer.BuildLiveToTsArgs("a", "b", "avc", debugLog: true), "-loglevel"));
        Assert.Equal("verbose", ValueAfter(LiveMuxer.BuildLiveRemuxArgs("a", "b", faststart: true, debugLog: true), "-loglevel"));
    }

    // 分段名可能以 - 开头（含中文标题被清洗后），没有 -- 分隔符会被 ffmpeg 当成选项
    [Fact]
    public void BuildArgs_TerminateOptionsBeforeOutput( )
    {
        var ts = LiveMuxer.BuildLiveToTsArgs("in", "-weird.ts", "avc", debugLog: false);
        Assert.Equal(["--", "-weird.ts"], ts.TakeLast(2));

        var mp4 = LiveMuxer.BuildLiveRemuxArgs("in", "-weird.mp4", faststart: true, debugLog: false);
        Assert.Equal(["--", "-weird.mp4"], mp4.TakeLast(2));
    }

    private static string? ValueAfter(List<string> args, string flag)
    {
        var i = args.IndexOf(flag);
        return i >= 0 && i + 1 < args.Count ? args[i + 1] : null;
    }
}
