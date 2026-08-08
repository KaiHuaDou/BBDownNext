using System.Collections.Generic;
using System.Linq;

using BBDown.Core.Entity;
using BBDown.Core.Util;


namespace BBDown.Core.Tests;

public class ViewPointUtilTests
{
    [Fact]
    public void FillGapsWithMainContent_InsertsMainContentBetweenClips( )
    {
        List<ViewPoint> points =
        [
            new( ) { Title = "片头", Start = 30, End = 120 },
            new( ) { Title = "片尾", Start = 1300, End = 1400 }
        ];

        var result = ViewPointUtil.FillGapsWithMainContent(points);

        Assert.Equal(["正片", "片头", "正片", "片尾"], result.Select(p => p.Title));
        Assert.Equal([(0, 30), (30, 120), (120, 1300), (1300, 1400)], result.Select(p => (p.Start, p.End)));
    }

    [Fact]
    public void FillGapsWithMainContent_ClipStartsAtZero_NoLeadingMainContent( )
    {
        List<ViewPoint> points = [new( ) { Title = "片头", Start = 0, End = 90 }];
        var result = ViewPointUtil.FillGapsWithMainContent(points);
        Assert.Equal(["片头"], result.Select(p => p.Title));
    }

    [Fact]
    public void FillGapsWithMainContent_EmptyInput_ReturnsEmpty( )
    {
        Assert.Empty(ViewPointUtil.FillGapsWithMainContent([]));
    }

    // 接口下发顺序不保证，追加后必须按起点重排再补空隙
    [Fact]
    public void Append_SortsByStartBeforeFillingGaps( )
    {
        ParsedResult result = new( );

        ViewPointUtil.Append(result,
        [
            new( ) { Title = "片尾", Start = 1300, End = 1400 },
            new( ) { Title = "片头", Start = 30, End = 120 }
        ]);

        Assert.Equal(["正片", "片头", "正片", "片尾"], result.ExtraPoints.Select(p => p.Title));
    }
}