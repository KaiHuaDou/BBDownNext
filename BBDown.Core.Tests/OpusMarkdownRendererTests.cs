using System.Collections.Generic;

using BBDown.Core.Opus;

namespace BBDown.Core.Tests;

public class OpusMarkdownRendererTests
{
    private static OpusDocument Doc(params OpusParagraph[] paragraphs)
    {
        return new OpusDocument
        {
            Title = "标题",
            Paragraphs = [.. paragraphs],
        };
    }

    private static OpusParagraph Text(string text, bool bold = false, int fontSize = 0)
    {
        return new OpusParagraph
        {
            Kind = OpusParagraphKind.Text,
            TextNodes = [new OpusTextNode { Text = text, Bold = bold, FontSize = fontSize }],
        };
    }

    private static string RenderBody(OpusDocument doc, IReadOnlyDictionary<string, string>? map = null)
    {
        return OpusMarkdownRenderer.Render(doc, new OpusRenderOptions(EmbedFrontMatter: false, ImagePathMap: map));
    }

    [Fact]
    public void Render_FrontMatter_EmittedOnlyWhenEnabled( )
    {
        var doc = new OpusDocument
        {
            Title = "带\"引号\"的标题",
            AuthorName = "作者",
            AuthorMid = "12345",
            PublishTime = 1700000000,
            SourceUrl = "https://www.bilibili.com/read/cv1",
            Tags = ["标签A", "标签B"],
            Paragraphs = [Text("正文")],
        };

        var withFm = OpusMarkdownRenderer.Render(doc, new OpusRenderOptions( ));
        Assert.StartsWith("---\n", withFm.Replace("\r\n", "\n"), System.StringComparison.Ordinal);
        Assert.Contains("title: \"带\\\"引号\\\"的标题\"", withFm, System.StringComparison.Ordinal);
        Assert.Contains("author: \"作者\"", withFm, System.StringComparison.Ordinal);
        Assert.Contains("mid: 12345", withFm, System.StringComparison.Ordinal);
        Assert.Contains("  - 标签A", withFm, System.StringComparison.Ordinal);

        var withoutFm = OpusMarkdownRenderer.Render(doc, new OpusRenderOptions(EmbedFrontMatter: false));
        Assert.StartsWith("# 带", withoutFm, System.StringComparison.Ordinal);
        Assert.DoesNotContain("author:", withoutFm, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Render_Heading_UsesHeadingLevel( )
    {
        var p = new OpusParagraph
        {
            Kind = OpusParagraphKind.Heading,
            HeadingLevel = 2,
            TextNodes = [new OpusTextNode { Text = "小节" }],
        };
        Assert.Contains("## 小节", RenderBody(Doc(p)), System.StringComparison.Ordinal);
    }

    [Fact]
    public void Render_Bold_MovesEdgeWhitespaceOutside( )
    {
        // "** 文字 **" 在多数渲染器里不会被识别为加粗；末段的尾随空格会被 TrimEnd 吃掉，故补一段正文
        var md = RenderBody(Doc(Text(" 加粗 ", bold: true), Text("尾段")));
        Assert.Contains(" **加粗** ", md, System.StringComparison.Ordinal);
        Assert.DoesNotContain("** 加粗", md, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Render_InlineMarkdownChars_AreEscaped( )
    {
        var md = RenderBody(Doc(Text("a*b_c[d]e`f")));
        Assert.Contains(@"a\*b\_c\[d\]e\`f", md, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Render_Image_UsesLocalPathWhenMapped( )
    {
        var p = new OpusParagraph { Kind = OpusParagraphKind.Image };
        p.Images.Add(new OpusImage { Url = "//i0.hdslb.com/a.png@1000w.webp", Caption = "图注" });

        var remote = RenderBody(Doc(p));
        Assert.Contains("![图注](https://i0.hdslb.com/a.png)", remote, System.StringComparison.Ordinal);
        Assert.Contains("*图注*", remote, System.StringComparison.Ordinal);

        var map = new Dictionary<string, string> { ["https://i0.hdslb.com/a.png"] = "标题/images/001-abc.png" };
        Assert.Contains("![图注](标题/images/001-abc.png)", RenderBody(Doc(p), map), System.StringComparison.Ordinal);
    }

    [Fact]
    public void Render_Image_WithoutCaption_UsesPlaceholderAlt( )
    {
        var p = new OpusParagraph { Kind = OpusParagraphKind.Image };
        p.Images.Add(new OpusImage { Url = "https://i0.hdslb.com/a.png" });
        Assert.Contains("![image](https://i0.hdslb.com/a.png)", RenderBody(Doc(p)), System.StringComparison.Ordinal);
    }

    // 远程 URL 的 ? # % 是有效语法，百分号转义会把链接改写成打不开的地址
    [Fact]
    public void Render_RemoteUrlWithQuery_IsNotPercentEscaped( )
    {
        var p = new OpusParagraph
        {
            Kind = OpusParagraphKind.Text,
            TextNodes = [new OpusTextNode { Text = "链接", Url = "https://www.bilibili.com/video/BV1?spm_id_from=333.999#reply" }],
        };
        var md = RenderBody(Doc(p));
        Assert.Contains("[链接](https://www.bilibili.com/video/BV1?spm_id_from=333.999#reply)", md, System.StringComparison.Ordinal);
        Assert.DoesNotContain("%3F", md, System.StringComparison.Ordinal);
        Assert.DoesNotContain("%23", md, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Render_RemoteUrlWithParens_UsesAngleBrackets( )
    {
        var p = new OpusParagraph
        {
            Kind = OpusParagraphKind.Text,
            TextNodes = [new OpusTextNode { Text = "维基", Url = "https://en.wikipedia.org/wiki/A_(b)" }],
        };
        Assert.Contains("[维基](<https://en.wikipedia.org/wiki/A_(b)>)", RenderBody(Doc(p)), System.StringComparison.Ordinal);
    }

    // 本地相对路径反过来必须转义，否则空格与括号会截断链接
    [Fact]
    public void Render_LocalPathWithSpaceAndParens_IsPercentEscaped( )
    {
        var p = new OpusParagraph { Kind = OpusParagraphKind.Image };
        p.Images.Add(new OpusImage { Url = "https://i0.hdslb.com/a.png" });
        var map = new Dictionary<string, string> { ["https://i0.hdslb.com/a.png"] = "my title (1)/images/001 a.png" };
        Assert.Contains("(my%20title%20%281%29/images/001%20a.png)", RenderBody(Doc(p), map), System.StringComparison.Ordinal);
    }

    [Fact]
    public void Render_List_OrderedAndNested( )
    {
        var p = new OpusParagraph { Kind = OpusParagraphKind.List, ListStyle = OpusListStyle.Ordered };
        p.ListItems.Add(new OpusListItem { Level = 1, Order = 1, Nodes = [new OpusTextNode { Text = "一" }] });
        p.ListItems.Add(new OpusListItem { Level = 2, Order = 2, Nodes = [new OpusTextNode { Text = "二" }] });

        var md = RenderBody(Doc(p)).Replace("\r\n", "\n");
        Assert.Contains("  1. 一\n", md, System.StringComparison.Ordinal);
        Assert.Contains("    2. 二\n", md, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Render_Code_StripsLanguagePrefixAndDecodesEntities( )
    {
        var p = new OpusParagraph { Kind = OpusParagraphKind.Code, CodeLang = "language-csharp", Code = "if (a &lt; b) { }" };
        var md = RenderBody(Doc(p)).Replace("\r\n", "\n");
        Assert.Contains("```csharp\nif (a < b) { }\n```", md, System.StringComparison.Ordinal);
    }

    // 内容自带 ``` 时围栏必须加长，否则代码块提前闭合
    [Fact]
    public void Render_Code_WidensFenceWhenContentHasBackticks( )
    {
        var p = new OpusParagraph { Kind = OpusParagraphKind.Code, Code = "```\nnested\n```" };
        var md = RenderBody(Doc(p)).Replace("\r\n", "\n");
        Assert.Contains("````\n```\nnested\n```\n````", md, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Render_Formula_WrappedInDollars( )
    {
        var p = new OpusParagraph
        {
            Kind = OpusParagraphKind.Text,
            TextNodes = [new OpusTextNode { IsFormula = true, FormulaLatex = "E=mc^2" }],
        };
        Assert.Contains("$E=mc^2$", RenderBody(Doc(p)), System.StringComparison.Ordinal);
    }

    [Fact]
    public void Render_DividerAndQuote( )
    {
        var quote = new OpusParagraph
        {
            Kind = OpusParagraphKind.Quote,
            TextNodes = [new OpusTextNode { Text = "引用" }],
        };
        var md = RenderBody(Doc(new OpusParagraph { Kind = OpusParagraphKind.Divider }, quote));
        Assert.Contains("---", md, System.StringComparison.Ordinal);
        Assert.Contains("> 引用", md, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Render_UnknownParagraph_IsSkipped( )
    {
        var md = RenderBody(Doc(new OpusParagraph { Kind = OpusParagraphKind.Unknown }, Text("正文")));
        Assert.Contains("正文", md, System.StringComparison.Ordinal);
    }

    // 旧版 HTML 转换产物（含保留 HTML 标签与 Markdown 结构）必须原样输出，不能再转义一次
    [Fact]
    public void Render_RawMarkdownNode_IsNotEscaped( )
    {
        var p = new OpusParagraph
        {
            Kind = OpusParagraphKind.Text,
            TextNodes = [new OpusTextNode
            {
                Text = "**加粗** [链接](https://x) <span class=\"color-pink-03\">粉字</span>",
                IsRawMarkdown = true,
            }],
        };
        var md = RenderBody(Doc(p));
        Assert.Contains("**加粗**", md, System.StringComparison.Ordinal);
        Assert.Contains("[链接](https://x)", md, System.StringComparison.Ordinal);
        Assert.Contains("<span class=\"color-pink-03\">粉字</span>", md, System.StringComparison.Ordinal);
        Assert.DoesNotContain(@"\*\*加粗\*\*", md, System.StringComparison.Ordinal);
    }

    // link_card 缺跳转地址时输出引用文本，杜绝 "> []( )" 空链接
    [Fact]
    public void Render_LinkCardWithoutUrl_EmitsQuoteText( )
    {
        var p = new OpusParagraph { Kind = OpusParagraphKind.LinkCard, LinkTitle = "角色名", LinkUrl = "" };
        var md = RenderBody(Doc(p));
        Assert.Contains("> 角色名", md, System.StringComparison.Ordinal);
        Assert.DoesNotContain("> []( )", md, System.StringComparison.Ordinal);
    }
}
