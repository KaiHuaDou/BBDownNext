namespace BBDown.Tests;

public class LiveProgressTests
{
    [Fact]
    public void CellWidth_Ascii_ReturnsCharCount( )
    {
        Assert.Equal(10, LiveProgress.CellWidth("abc 12|/-\\"));
    }

    [Fact]
    public void CellWidth_Cjk_EachCharCountsTwoCells( )
    {
        Assert.Equal(6, LiveProgress.CellWidth("录制中"));
    }

    [Fact]
    public void CellWidth_Mixed_CjkAndAscii( )
    {
        Assert.Equal(36, LiveProgress.CellWidth("录制中 00:00:01 | 分段 1 | 原画(avc)"));
    }

    [Fact]
    public void CellWidth_Empty_ReturnsZero( )
    {
        Assert.Equal(0, LiveProgress.CellWidth(string.Empty));
    }

    [Fact]
    public void CellWidth_FullwidthForms_CountTwoCells( )
    {
        Assert.Equal(2, LiveProgress.CellWidth("："));
    }
}
