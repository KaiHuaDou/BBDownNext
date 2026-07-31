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
    [InlineData(".title.", "_.title")]
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
    [InlineData(".title.")]
    [InlineData("CON")]
    [InlineData("a/b")]
    public void GetValidFileName_IsIdempotent(string input)
    {
        var once = FileNameUtil.GetValidFileName(input);
        Assert.Equal(once, FileNameUtil.GetValidFileName(once));
    }
}
