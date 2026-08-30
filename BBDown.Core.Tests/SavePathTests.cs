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
}
