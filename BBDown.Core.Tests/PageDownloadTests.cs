using System.Collections.Generic;

using BBDown.Core.Entity;
using BBDown.Core.Media;

using Xunit;

namespace BBDown.Core.Tests;

public class PageDownloadTests
{
    private static VInfo VInfoWithPic(string pic) => new( )
    {
        Title = "",
        Desc = "",
        Pic = pic,
        PubTime = 0,
        PagesInfo = [],
    };

    private static Page PageWith(int index, string aid, string cover = "") => new( )
    {
        Index = index,
        Aid = aid,
        Cid = "1",
        EpId = "",
        Title = "",
        Dur = 0,
        Res = "",
        PubTime = 0,
        Cover = cover,
    };

    // ---- ResolveCoverUrl ----

    [Fact]
    public void ResolveCoverUrl_PicSet_ReturnsPic( )
    {
        var vInfo = VInfoWithPic("https://i0.hdslb.com/cover.jpg");
        Assert.Equal("https://i0.hdslb.com/cover.jpg", PageDownload.ResolveCoverUrl(vInfo, PageWith(1, "1", "per-page.jpg")));
    }

    [Fact]
    public void ResolveCoverUrl_PicEmpty_FallsBackToPageCover( )
    {
        var vInfo = VInfoWithPic("");
        Assert.Equal("per-page.jpg", PageDownload.ResolveCoverUrl(vInfo, PageWith(1, "1", "per-page.jpg")));
    }

    [Fact]
    public void ResolveCoverUrl_PicEmptyAndPageCoverNull_ReturnsEmpty( )
    {
        var vInfo = VInfoWithPic("");
        Assert.Equal("", PageDownload.ResolveCoverUrl(vInfo, PageWith(1, "1")));
    }

    // ---- CoverKey ----

    [Fact]
    public void CoverKey_SameUrl_SameKey( )
    {
        Assert.Equal(PageDownload.CoverKey("https://x/cover.jpg"), PageDownload.CoverKey("https://x/cover.jpg"));
    }

    [Fact]
    public void CoverKey_DifferentUrl_DifferentKey( )
    {
        Assert.NotEqual(PageDownload.CoverKey("https://x/a.jpg"), PageDownload.CoverKey("https://x/b.jpg"));
    }

    [Fact]
    public void CoverKey_EmptyUrl_ReturnsEmptyMarker( )
    {
        Assert.Equal("empty", PageDownload.CoverKey(""));
    }

    [Fact]
    public void CoverKey_Deterministic_NotRandom( )
    {
        Assert.Equal(PageDownload.CoverKey("https://x/a.jpg"), PageDownload.CoverKey("https://x/a.jpg"));
    }

    // ---- ShouldDeleteCover ----

    [Fact]
    public void ShouldDeleteCover_SharedCover_NeverDeletes( )
    {
        var pages = new List<Page> { PageWith(1, "1"), PageWith(2, "2") };
        Assert.False(PageDownload.ShouldDeleteCover(pages[0], pages, sharedCover: true));
        Assert.False(PageDownload.ShouldDeleteCover(pages[1], pages, sharedCover: true));
    }

    [Fact]
    public void ShouldDeleteCover_SinglePage_Deletes( )
    {
        var pages = new List<Page> { PageWith(1, "1") };
        Assert.True(PageDownload.ShouldDeleteCover(pages[0], pages, sharedCover: false));
    }

    [Fact]
    public void ShouldDeleteCover_LastPage_Deletes( )
    {
        var pages = new List<Page> { PageWith(1, "1"), PageWith(2, "2") };
        Assert.True(PageDownload.ShouldDeleteCover(pages[1], pages, sharedCover: false));
    }

    [Fact]
    public void ShouldDeleteCover_MiddlePageSameAid_Keeps( )
    {
        var pages = new List<Page> { PageWith(1, "1"), PageWith(2, "1"), PageWith(3, "1") };
        Assert.False(PageDownload.ShouldDeleteCover(pages[1], pages, sharedCover: false));
    }

    [Fact]
    public void ShouldDeleteCover_DifferentAidFromLast_Deletes( )
    {
        var pages = new List<Page> { PageWith(1, "1"), PageWith(2, "2") };
        Assert.True(PageDownload.ShouldDeleteCover(pages[0], pages, sharedCover: false));
    }
}
