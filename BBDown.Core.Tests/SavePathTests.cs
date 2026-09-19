using BBDown.Core.Entity;

namespace BBDown.Core.Tests;

/// <summary>
/// 文件名模板的日期占位符（纯函数部分）。落盘路径的拼装依赖 WorkContext，不在此测。
/// </summary>
public class SavePathTests
{
    private static Page Page(long pubTime = 1600000000)
    {
        return new( )
        {
            Index = 1,
            Aid = "114",
            Cid = "514",
            EpId = "",
            Title = "标题",
            Dur = 100,
            Res = "1080p",
            PubTime = pubTime
        };
    }

    private static string Format(string pattern, long pubTime = 1600000000)
    {
        return SavePath.Format(pattern, "标题", null, null, Page(pubTime), 1, ApiType.Web, pubTime);
    }

    private static Video Video(string res, string fps, string dfn, string codecs)
    {
        return new( )
        {
            Id = "1",
            Dfn = dfn,
            BaseUrl = "https://example.com/master.m3u8",
            Res = res,
            Fps = fps,
            Codecs = codecs,
        };
    }

    private static string Format(string pattern, Video video)
    {
        return SavePath.Format(pattern, "标题", video, null, Page( ), 1, ApiType.Web, 1600000000);
    }

    // ':' 在 Windows 上不合法，含冒号的日期格式会让整条路径失效（或落到备用数据流）。
    // 时刻部分随时区变化，故只断言冒号被替换
    [Fact]
    public void Format_ReplacesColonInDateFormat( )
    {
        var result = Format("<publishDate:yyyy_MM_ddTHH:mm:ss>");

        Assert.DoesNotContain(':', result);
        Assert.Matches(@"^\d{4}_\d{2}_\d{2}T\d{2}_\d{2}_\d{2}\.mp4$", result);
    }

    [Fact]
    public void Format_VideoDateUsesPagePubTime( )
    {
        Assert.Equal("2020-09-13.mp4", Format("<videoDate:yyyy-MM-dd>"));
    }

    [Fact]
    public void Format_UnknownPlaceholderIsKeptVerbatim( )
    {
        Assert.Equal("<nope>.mp4", Format("<nope>"));
    }

    // 清晰度 / 分辨率 / 帧率 / 编码逐字来自 playurl 响应（--insecure 中间人或镜像站对端可控），
    // 展开时必须不含路径分隔符：任何 / 或 \ 都已替换，整串是单段文件名，`..` 无法构成穿越
    [Theory]
    [InlineData("..\\evil", "20000/1001", "4K 杜比", "avc1/../x")]
    [InlineData("../evil", "../../x", "..", "avc1..4")]
    [InlineData("---", "////", "..\\..", "|:?*")]
    public void Format_SanitizesServerControlledTrackValues(string res, string fps, string dfn, string codecs)
    {
        var result = Format("<dfn>-<res>-<fps>-<videoCodecs>", Video(res, fps, dfn, codecs));

        Assert.DoesNotContain('/', result);
        Assert.DoesNotContain('\\', result);
        Assert.EndsWith(".mp4", result);
    }

    [Fact]
    public void Format_KeepsLegalTrackValues( )
    {
        // 合法输入保持原样（帧率 60000/1001 的 / 本就不能留在文件名里，替换为 _ 是既有固定行为）
        Assert.Equal("1080P-1920x1080-60000_1001-avc1.640028.mp4", Format("<dfn>-<res>-<fps>-<videoCodecs>", Video("1920x1080", "60000/1001", "1080P", "avc1.640028")));
    }
}
