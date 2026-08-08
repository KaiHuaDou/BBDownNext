using BBDown.Core.Entity;

namespace BBDown.Core.Tests;

public class EntityTests
{
    private static Page MakePage(string aid = "1", string cid = "2", string epid = "3")
    {
        return new( )
        {
            Index = 1,
            Aid = aid,
            Cid = cid,
            EpId = epid,
            Title = "t",
            Dur = 10,
            Res = "1920x1080",
            PubTime = 123,
        };
    }

    [Fact]
    public void Page_Equality_OnlyByAidCidEpid( )
    {
        var a = MakePage( );
        var b = MakePage( );
        b.Title = "different";
        b.Dur = 999;
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode( ), b.GetHashCode( ));

        var c = MakePage(cid: "changed");
        Assert.NotEqual(a, c);
    }

    [Fact]
    public void Page_CopyWith_KeepsIdentityFields_NotDescAndPoints( )
    {
        var src = MakePage( );
        src.Cover = "cover";
        src.Desc = "desc";
        src.OwnerName = "owner";
        src.OwnerMid = "42";
        src.Points.Add(new ViewPoint { Title = "p", Start = 0, End = 1 });

        var copy = src.CopyWith(7);

        Assert.Equal(7, copy.Index);
        Assert.Equal(src.Aid, copy.Aid);
        Assert.Equal(src.Cid, copy.Cid);
        Assert.Equal(src.EpId, copy.EpId);
        Assert.Equal(src.Cover, copy.Cover);
        Assert.Equal(src.OwnerName, copy.OwnerName);
        Assert.Equal(src.OwnerMid, copy.OwnerMid);
        // 沿用原拷贝构造语义：desc 与 points 不复制
        Assert.Null(copy.Desc);
        Assert.Empty(copy.Points);
    }

    [Fact]
    public void Page_Bvid_ComputedFromAid( )
    {
        var p = MakePage(aid: "626497566");
        Assert.Equal("BV1qt4y1X7TW", p.Bvid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("ep123456")]
    [InlineData("99999999999999999999")]
    public void Page_Bvid_EmptyWhenAidIsNotAnAvNumber(string aid)
    {
        Assert.Equal("", MakePage(aid: aid).Bvid);
    }

    [Fact]
    public void Video_Equality_ExcludesBaseUrlAndSize( )
    {
        var a = new Video { Id = "80", Dfn = "1080P", BaseUrl = "http://a", Codecs = "AVC", Bandwidth = 100, Dur = 60, Size = 1 };
        var b = new Video { Id = "80", Dfn = "1080P", BaseUrl = "http://b", Codecs = "AVC", Bandwidth = 100, Dur = 60, Size = 2 };
        Assert.Equal(a, b);

        var c = new Video { Id = "80", Dfn = "1080P", BaseUrl = "http://a", Codecs = "HEVC", Bandwidth = 100, Dur = 60 };
        Assert.NotEqual(a, c);
    }

    [Fact]
    public void Audio_Equality_ExcludesBaseUrl( )
    {
        var a = new Audio { Id = "30280", Dfn = "192K", BaseUrl = "http://a", Codecs = "M4A", Bandwidth = 192, Dur = 60 };
        var b = new Audio { Id = "30280", Dfn = "192K", BaseUrl = "http://b", Codecs = "M4A", Bandwidth = 192, Dur = 60 };
        Assert.Equal(a, b);
    }

    [Fact]
    public void Audio_ShortCodecs_StripsDashAndUppercases( )
    {
        var a = new Audio { Id = "1", Dfn = "", BaseUrl = "", Codecs = "E-AC-3", Bandwidth = 1, Dur = 1 };
        Assert.Equal("EAC3", a.ShortCodecs);
    }
}