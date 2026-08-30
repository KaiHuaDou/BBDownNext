using System.Linq;
using System.Text;

using BBDown.Core.Util;

namespace BBDown.Core.Tests;

public class FileNameUtilTests
{
    [Theory]
    [InlineData("normal_title", "normal_title")]
    [InlineData("a:b*c?d", "a_b_c_d")]
    [InlineData("a<b>c|d\"e", "a_b_c_d_e")]
    public void GetValidFileName_ReplacesInvalidChars(string input, string expected)
    {
        Assert.Equal(expected, FileNameUtil.GetValidFileName(input));
    }

    [Fact]
    public void GetValidFileName_FiltersBothSlashes( )
    {
        Assert.Equal("a_b", FileNameUtil.GetValidFileName("a/b"));
        Assert.Equal("a_b", FileNameUtil.GetValidFileName("a\\b"));
        Assert.Equal("a_b_c", FileNameUtil.GetValidFileName("a/b\\c"));
    }

    [Fact]
    public void GetValidFileName_ControlCharsStripped( )
    {
        Assert.Equal("a_b", FileNameUtil.GetValidFileName("a\nb"));
    }

    [Fact]
    public void GetValidFileName_PrefixesLeadingDot( )
    {
        Assert.Equal("_.hidden", FileNameUtil.GetValidFileName(".hidden"));
    }

    [Theory]
    [InlineData("title.", "title")]
    [InlineData("title...", "title")]
    [InlineData("  title  ", "title")]
    [InlineData(".Title.", "_.Title")]
    public void GetValidFileName_TrimsTrailingDotsAndSpaces(string input, string expected)
    {
        Assert.Equal(expected, FileNameUtil.GetValidFileName(input));
    }

    [Theory]
    [InlineData("CON")]
    [InlineData("nul")]
    [InlineData("COM9")]
    [InlineData("LPT1.mp4")]
    public void GetValidFileName_EscapesWindowsReservedNames(string input)
    {
        Assert.Equal("_" + input, FileNameUtil.GetValidFileName(input));
    }

    // 占位符的字符集（SavePath.InfoRegex）不含 '/'，替换值里出现分隔符只可能来自服务端数据
    [Theory]
    [InlineData("2024/01/02", "2024_01_02")]
    [InlineData("2024-01-02 03:04:05", "2024-01-02 03_04_05")]
    public void GetValidFileName_NeutralizesDateFormatResults(string input, string expected)
    {
        Assert.Equal(expected, FileNameUtil.GetValidFileName(input));
    }

    [Fact]
    public void GetValidFileName_ConsoleIsNotReserved( )
    {
        Assert.Equal("CONSOLE", FileNameUtil.GetValidFileName("CONSOLE"));
    }

    [Fact]
    public void GetValidFileName_TruncatesByUtf8Bytes( )
    {
        var result = FileNameUtil.GetValidFileName(new string('中', 200));

        Assert.True(Encoding.UTF8.GetByteCount(result) <= 200);
        Assert.Equal(66, result.Length);
    }

    [Fact]
    public void GetValidFileName_TruncationKeepsSurrogatePairsIntact( )
    {
        var result = FileNameUtil.GetValidFileName(string.Concat(Enumerable.Repeat("🍣", 100)));

        Assert.True(Encoding.UTF8.GetByteCount(result) <= 200);
        Assert.Equal(0, result.Length % 2);
        Assert.False(char.IsHighSurrogate(result[^1]));
    }

    [Theory]
    [InlineData(".Title.")]
    [InlineData("CON")]
    [InlineData("a/b")]
    public void GetValidFileName_IsIdempotent(string input)
    {
        var once = FileNameUtil.GetValidFileName(input);
        Assert.Equal(once, FileNameUtil.GetValidFileName(once));
    }

    [Theory]
    [InlineData(".", "_")]
    [InlineData(" ", "_")]
    [InlineData("..", "_")]
    [InlineData(" .", "_")]
    [InlineData(". ", "_")]
    [InlineData("  ", "_")]
    [InlineData("a.", "a")]
    [InlineData("a ", "a")]
    public void GetValidFileName_PureDotOrSpaceFallsBackToUnderscore(string input, string expected)
    {
        var result = FileNameUtil.GetValidFileName(input);

        Assert.Equal(expected, result);
        Assert.False(result.EndsWith('.'), "文件名不得以点结尾");
        Assert.False(result.EndsWith(' '), "文件名不得以空格结尾");
        Assert.NotEqual("", result);
    }
}
