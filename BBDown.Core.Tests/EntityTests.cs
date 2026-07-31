using static BBDown.Core.Entity.Entity;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;


namespace BBDown.Core.Tests;

public class EntityTests
{
    private static Page MakePage(string aid = "1", string cid = "2", string epid = "3")
    {
        return new( )
        {
            index = 1,
            aid = aid,
            cid = cid,
            epid = epid,
            title = "t",
            dur = 10,
            res = "1920x1080",
            pubTime = 123,
        };
    }

    [Fact]
    public void Page_Equality_OnlyByAidCidEpid( )
    {
        var a = MakePage( );
        var b = MakePage( );
        b.title = "different";
        b.dur = 999;
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode( ), b.GetHashCode( ));

        var c = MakePage(cid: "changed");
        Assert.NotEqual(a, c);
    }

    [Fact]
    public void Page_CopyWith_KeepsIdentityFields_NotDescAndPoints( )
    {
        var src = MakePage( );
        src.cover = "cover";
        src.desc = "desc";
        src.ownerName = "owner";
        src.ownerMid = "42";
        src.points.Add(new ViewPoint { title = "p", start = 0, end = 1 });

        var copy = src.CopyWith(7);

        Assert.Equal(7, copy.index);
        Assert.Equal(src.aid, copy.aid);
        Assert.Equal(src.cid, copy.cid);
        Assert.Equal(src.epid, copy.epid);
        Assert.Equal(src.cover, copy.cover);
        Assert.Equal(src.ownerName, copy.ownerName);
        Assert.Equal(src.ownerMid, copy.ownerMid);
        // 沿用原拷贝构造语义：desc 与 points 不复制
        Assert.Null(copy.desc);
        Assert.Empty(copy.points);
    }

    [Fact]
    public void Page_Bvid_ComputedFromAid( )
    {
        var p = MakePage(aid: "626497566");
        Assert.Equal("BV1qt4y1X7TW", p.bvid);
    }

    [Fact]
    public void Video_Equality_ExcludesBaseUrlAndSize( )
    {
        var a = new Video { id = "80", dfn = "1080P", baseUrl = "http://a", codecs = "AVC", bandwidth = 100, dur = 60, size = 1 };
        var b = new Video { id = "80", dfn = "1080P", baseUrl = "http://b", codecs = "AVC", bandwidth = 100, dur = 60, size = 2 };
        Assert.Equal(a, b);

        var c = new Video { id = "80", dfn = "1080P", baseUrl = "http://a", codecs = "HEVC", bandwidth = 100, dur = 60 };
        Assert.NotEqual(a, c);
    }

    [Fact]
    public void Audio_Equality_ExcludesBaseUrl( )
    {
        var a = new Audio { id = "30280", dfn = "192K", baseUrl = "http://a", codecs = "M4A", bandwidth = 192, dur = 60 };
        var b = new Audio { id = "30280", dfn = "192K", baseUrl = "http://b", codecs = "M4A", bandwidth = 192, dur = 60 };
        Assert.Equal(a, b);
    }

    [Fact]
    public void Audio_ShortCodecs_StripsDashAndUppercases( )
    {
        var a = new Audio { id = "1", dfn = "", baseUrl = "", codecs = "E-AC-3", bandwidth = 1, dur = 1 };
        Assert.Equal("EAC3", a.shortCodecs);
    }
}
