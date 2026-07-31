using System.Collections.Generic;
using System.Linq;
using BBDown.Core.Entity;
using Xunit;

namespace BBDown.Tests;

public class SelectedPagesTests
{
    private const string PlainUrl = "https://www.bilibili.com/video/BV1xx411c7mD";

    private static VInfo MakeVInfo(int pageCount, string? index = null) => new()
    {
        Title = "t",
        Desc = "d",
        Pic = "p",
        PubTime = 0,
        Index = index,
        PagesInfo = [.. Enumerable.Range(1, pageCount).Select(i => new Entity.Page
        {
            index = i,
            aid = "1",
            cid = i.ToString(),
            epid = "",
            title = $"P{i}",
            dur = 0,
            res = "",
            pubTime = 0
        })]
    };

    private static List<string>? Select(string selectPage, int pageCount = 10, string url = PlainUrl, string? index = null)
        => Program.GetSelectedPages(new MyOption { SelectPage = selectPage }, MakeVInfo(pageCount, index), url);

    [Fact]
    public void NoSelection_ReturnsNull()
    {
        Assert.Null(Select(""));
    }

    [Fact]
    public void NoSelection_FallsBackToEpisodeIndex()
    {
        Assert.Equal(["7"], Select("", index: "7"));
    }

    [Fact]
    public void NoSelection_FallsBackToUrlPageParam()
    {
        Assert.Equal(["3"], Select("", url: $"{PlainUrl}?p=3"));
    }

    // Index 优先于 URL 上的 ?p=
    [Fact]
    public void EpisodeIndex_WinsOverUrlPageParam()
    {
        Assert.Equal(["7"], Select("", url: $"{PlainUrl}?p=3", index: "7"));
    }

    // ALL 返回 null，与「未选择」同义，由调用方按全量处理
    [Fact]
    public void All_ReturnsNull()
    {
        Assert.Null(Select("ALL"));
    }

    [Fact]
    public void SingleNumber()
    {
        Assert.Equal(["8"], Select("8"));
    }

    [Fact]
    public void CommaList()
    {
        Assert.Equal(["1", "2", "5"], Select("1,2,5"));
    }

    [Fact]
    public void Range_IsInclusiveOnBothEnds()
    {
        Assert.Equal(["3", "4", "5"], Select("3-5"));
    }

    [Theory]
    [InlineData("LAST")]
    [InlineData("NEW")]
    [InlineData("LATEST")]
    public void LastAliases_ResolveToPageCount(string alias)
    {
        Assert.Equal(["10"], Select(alias, pageCount: 10));
    }

    [Fact]
    public void LastAlias_IsCaseInsensitive()
    {
        Assert.Equal(["10"], Select("last", pageCount: 10));
    }

    [Fact]
    public void LastAlias_WorksInsideCommaList()
    {
        Assert.Equal(["3", "5", "10"], Select("3,5,LATEST", pageCount: 10));
    }

    [Fact]
    public void LastAlias_WorksAsRangeEnd()
    {
        Assert.Equal(["8", "9", "10"], Select("8-LAST", pageCount: 10));
    }

    [Fact]
    public void SurroundingWhitespaceAndTrailingCommaAreTrimmed()
    {
        Assert.Equal(["1", "2"], Select("  1,2,  "));
    }

    // 解析失败时返回 null 而非空列表，调用方据此退回全量
    [Fact]
    public void Unparsable_ReturnsNull()
    {
        Assert.Null(Select("abc-def"));
    }

    // 倒序区间产出空列表（for 循环一次都不进），不是 null
    [Fact]
    public void ReversedRange_ReturnsEmpty()
    {
        Assert.Empty(Select("5-3")!);
    }
}
