namespace BBDown.Tests;

public class ProgressBarTests
{
    [Fact]
    public void BuildDiff_SameText_ReturnsEmpty( )
    {
        Assert.Equal(string.Empty, ProgressBar.BuildDiff("abc", "abc"));
    }

    [Fact]
    public void BuildDiff_EmptyPrevious_AppendsText( )
    {
        Assert.Equal("abc", ProgressBar.BuildDiff(string.Empty, "abc"));
    }

    [Fact]
    public void BuildDiff_ExtendedSuffix_AppendsOnlyTail( )
    {
        Assert.Equal("de", ProgressBar.BuildDiff("abc", "abcde"));
    }

    [Fact]
    public void BuildDiff_ChangedSuffix_BackspacesAndRewrites( )
    {
        Assert.Equal("\b\bxe", ProgressBar.BuildDiff("abcd", "abxe"));
    }

    [Fact]
    public void BuildDiff_ShorterText_PadsSpacesThenBackspaces( )
    {
        Assert.Equal("\b\b\b   \b\b\b", ProgressBar.BuildDiff("abc", string.Empty));
    }

    [Fact]
    public void BuildDiff_CommonPrefixOnly_RewritesDivergentTail( )
    {
        // 公共前缀保留，回退到分叉点后重写
        Assert.Equal("\b\b78", ProgressBar.BuildDiff("123456", "123478"));
    }
}
