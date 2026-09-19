using BBDown.Core.Entity;

namespace BBDown.Core.Tests;

/// <summary>
/// Page 的 aid / cid 净化（对应 Entity.cs）。这两个值逐字来自 API 响应（对端可控），
/// 是工作区目录与文件名模板的组成段，setter 统一过 GetValidFileName。
/// </summary>
public class PageTests
{
    private static Page Page(string aid, string cid)
    {
        return new( )
        {
            Index = 1,
            Aid = aid,
            Cid = cid,
            EpId = "",
            Title = "标题",
            Dur = 100,
            Res = "",
            PubTime = 0
        };
    }

    [Theory]
    [InlineData("../evil", "..\\..\\x")]
    [InlineData("..", "....")]
    [InlineData("a/b", "..")]
    public void Create_SanitizesAidAndCid(string aid, string cid)
    {
        var page = Page(aid, cid);

        Assert.DoesNotContain('/', page.Aid);
        Assert.DoesNotContain('\\', page.Aid);
        Assert.DoesNotContain('/', page.Cid);
        Assert.DoesNotContain('\\', page.Cid);
    }

    [Fact]
    public void Create_KeepsLegalNumericIds( )
    {
        var page = Page("170001", "252745299");

        Assert.Equal("170001", page.Aid);
        Assert.Equal("252745299", page.Cid);
    }

    [Fact]
    public void Create_PureDotsFallBackToUnderscore( )
    {
        Assert.Equal("_", Page("..", "..").Aid);
    }
}