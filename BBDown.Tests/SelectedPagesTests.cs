using System.Collections.Generic;
using System.Linq;

using BBDown.Core.Entity;

namespace BBDown.Tests;

public class SelectedPagesTests
{
    private static readonly string PlainUrl = TestVideos.PickRandom( );

    private static VInfo MakeVInfo(int pageCount, string? index = null)
    {
        return new( )
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
    }

    private static List<string>? Select(string selectPage, int pageCount = 10, string? url = null, string? index = null)
    {
        return Program.GetSelectedPages(new DownloadOptions { SelectPage = selectPage }, MakeVInfo(pageCount, index), url ?? TestVideos.PickRandom( ));
    }

    [Fact]
    public void NoSelection_ReturnsNull( )
    {
        Assert.Null(Select(""));
    }

    [Fact]
    public void NoSelection_FallsBackToEpisodeIndex( )
    {
        Assert.Equal(["7"], Select("", index: "7"));
    }

    [Fact]
    public void NoSelection_FallsBackToUrlPageParam( )
    {
        Assert.Equal(["3"], Select("", url: $"{PlainUrl}?p=3"));
    }

    // Index 优先于 URL 上的 ?p=
    [Fact]
    public void EpisodeIndex_WinsOverUrlPageParam( )
    {
        Assert.Equal(["7"], Select("", url: $"{PlainUrl}?p=3", index: "7"));
    }

    // ALL / all 返回 null，与「未选择」同义，由调用方按全量处理
    [Fact]
    public void All_ReturnsNull( )
    {
        Assert.Null(Select("ALL"));
    }

    [Fact]
    public void All_LowerCase_ReturnsNull( )
    {
        Assert.Null(Select("all"));
    }

    [Fact]
    public void SingleNumber( )
    {
        Assert.Equal(["8"], Select("8"));
    }

    [Fact]
    public void CommaList( )
    {
        Assert.Equal(["1", "2", "5"], Select("1,2,5"));
    }

    [Fact]
    public void Range_IsInclusiveOnBothEnds( )
    {
        Assert.Equal(["3", "4", "5"], Select("3-5"));
    }

    [Fact]
    public void SingleValueRange_ResolvesToThatPage( )
    {
        Assert.Equal(["3"], Select("3-3"));
    }

    [Theory]
    [InlineData("LATEST")]
    [InlineData("NEW")]
    public void LatestAliases_ResolveToLastIndex(string alias)
    {
        Assert.Equal(["10"], Select(alias, pageCount: 10));
    }

    [Theory]
    [InlineData("LAST")]
    [InlineData("last")]
    public void LastKeyword_ResolveToSecondToLast(string alias)
    {
        Assert.Equal(["9"], Select(alias, pageCount: 10));
    }

    [Fact]
    public void LatestAlias_IsCaseInsensitive( )
    {
        Assert.Equal(["10"], Select("latest", pageCount: 10));
    }

    [Fact]
    public void LastKeyword_IsCaseInsensitive( )
    {
        Assert.Equal(["9"], Select("LAST", pageCount: 10));
    }

    [Fact]
    public void LatestAlias_WorksInsideCommaList( )
    {
        Assert.Equal(["3", "5", "10"], Select("3,5,LATEST", pageCount: 10));
    }

    [Fact]
    public void LastKeyword_WorksAsRangeEnd( )
    {
        Assert.Equal(["8", "9"], Select("8-LAST", pageCount: 10));
    }

    [Fact]
    public void OpenStartRange_FromFirstToGiven( )
    {
        Assert.Equal(Enumerable.Range(1, 22).Select(x => x.ToString( )), Select("-22", pageCount: 30));
    }

    [Fact]
    public void OpenEndRange_FromGivenToLast( )
    {
        Assert.Equal(Enumerable.Range(16, 15).Select(x => x.ToString( )), Select("16-", pageCount: 30));
    }

    [Fact]
    public void MixedExpression_WithLatestAsRangeEnd( )
    {
        var expected = Enumerable.Range(1, 10).Concat(Enumerable.Range(15, 16)).Select(x => x.ToString( ));
        Assert.Equal(expected, Select("1,2,3-3,4-5,6-10,15-latest", pageCount: 30));
    }

    [Fact]
    public void WhitespaceAroundCommaItemsIsTrimmed( )
    {
        Assert.Equal(["25", "26", "27", "33"], Select("25-27,   33", pageCount: 40));
    }

    [Fact]
    public void SurroundingWhitespaceAndTrailingCommaAreTrimmed( )
    {
        Assert.Equal(["1", "2"], Select("  1,2,  "));
    }

    [Fact]
    public void ReversedRange_IsNormalized( )
    {
        Assert.Equal(["3", "4", "5"], Select("5-3"));
    }

    [Fact]
    public void LastKeyword_WithTwoPages_ResolvesToFirst( )
    {
        Assert.Equal(["1"], Select("last", pageCount: 2));
    }

    [Fact]
    public void LastKeyword_WithSinglePage_IsIgnored( )
    {
        Assert.Empty(Select("last", pageCount: 1)!);
    }

    [Fact]
    public void LatestKeyword_WithSinglePage_ResolvesToOnlyPage( )
    {
        Assert.Equal(["1"], Select("latest", pageCount: 1));
    }

    [Fact]
    public void OutOfRangeUpperBound_IsClamped( )
    {
        Assert.Equal(Enumerable.Range(1, 10).Select(x => x.ToString( )), Select("1-999", pageCount: 10));
    }

    [Fact]
    public void OpenStartBeyondLastPage_ClampsToAll( )
    {
        // 只有 3 集却写 -4：夹紧到末集，下载全 3 集
        Assert.Equal(["1", "2", "3"], Select("-4", pageCount: 3));
    }

    [Fact]
    public void OutOfRangeLowerBound_IsClamped( )
    {
        Assert.Equal(Enumerable.Range(1, 10).Select(x => x.ToString( )), Select("0-10", pageCount: 10));
    }

    [Fact]
    public void InvalidToken_IsIgnored( )
    {
        Assert.Empty(Select("abc", pageCount: 10)!);
    }

    [Fact]
    public void InvalidRangeToken_IsIgnored( )
    {
        Assert.Empty(Select("abc-def", pageCount: 10)!);
    }

    [Fact]
    public void DuplicateValues_AreDeduplicatedAndSorted( )
    {
        Assert.Equal(["1", "2"], Select("1,1,2,2,1"));
    }
}
