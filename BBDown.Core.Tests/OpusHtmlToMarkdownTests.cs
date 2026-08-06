using BBDown.Core.Opus;

namespace BBDown.Core.Tests;

public class OpusHtmlToMarkdownTests
{
    private static string Convert(string html) => OpusHtmlToMarkdown.Convert(html);

    [Fact]
    public void Convert_ImgTag_IsPreservedAsHtml( )
    {
        var md = Convert("<p>图：</p><img src=\"//i0.hdslb.com/a.png\" class=\"cut-off-2\">");
        Assert.Contains("<img src=\"//i0.hdslb.com/a.png\" class=\"cut-off-2\">", md, System.StringComparison.Ordinal);
        Assert.DoesNotContain("![", md, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Convert_SpanWithClass_IsPreserved( )
    {
        var md = Convert("<p><span class=\"color-pink-03 font-size-20\">粉字</span></p>");
        Assert.Contains("<span class=\"color-pink-03 font-size-20\">粉字</span>", md, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Convert_StrongEm_ConvertedWithNestedSpanKept( )
    {
        var md = Convert("<strong>加粗</strong><em>斜体</em>");
        Assert.Contains("**加粗**", md, System.StringComparison.Ordinal);
        Assert.Contains("*斜体*", md, System.StringComparison.Ordinal);

        var nested = Convert("<strong><span class=\"color-pink-03\">粉</span>字</strong>");
        Assert.Contains("**<span class=\"color-pink-03\">粉</span>字**", nested, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Convert_Anchor_ConvertedToLink( )
    {
        var md = Convert("<a href=\"https://www.bilibili.com/video/BV1\">链接</a>");
        Assert.Contains("[链接](https://www.bilibili.com/video/BV1)", md, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Convert_Entities_DecodedInTextKeptInAttribute( )
    {
        var md = Convert("<p><span title=\"a&quot;b\">1 &lt; 2 &amp; 3</span></p>");
        Assert.Contains("<span title=\"a&quot;b\">1 < 2 & 3</span>", md, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Convert_BlockElements_ProduceMarkdown( )
    {
        var md = Convert("<h2>标题</h2><blockquote>引用</blockquote><ul><li>项一</li><li>项二</li></ul><hr>");
        Assert.Contains("## 标题", md, System.StringComparison.Ordinal);
        Assert.Contains("> 引用", md, System.StringComparison.Ordinal);
        Assert.Contains("- 项一", md, System.StringComparison.Ordinal);
        Assert.Contains("- 项二", md, System.StringComparison.Ordinal);
        Assert.Contains("---", md, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Convert_FigureBlock_PreservedAsHtml( )
    {
        var md = Convert("<figure class=\"img-box\"><img src=\"//i0.hdslb.com/b.png\"></figure>");
        Assert.Contains("<figure class=\"img-box\"><img src=\"//i0.hdslb.com/b.png\"></figure>", md, System.StringComparison.Ordinal);
        Assert.DoesNotContain("</figure>\n</figure>", md, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Convert_CodeBlock_StripsTagsInside( )
    {
        var md = Convert("<pre><code>if (a &lt; b) &amp;&amp; c</code></pre>");
        Assert.Contains("```\nif (a < b) && c\n```", md, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Convert_ScriptStyle_Removed( )
    {
        var md = Convert("<script>alert(1)</script><style>.x{}</style><p>正文</p>");
        Assert.DoesNotContain("alert", md, System.StringComparison.Ordinal);
        Assert.DoesNotContain(".x", md, System.StringComparison.Ordinal);
        Assert.Contains("正文", md, System.StringComparison.Ordinal);
    }
}
