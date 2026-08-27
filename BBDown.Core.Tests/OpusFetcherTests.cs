using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core.Opus;
using BBDown.Core.Util;

namespace BBDown.Core.Tests;

// HTTP 桩测试必须串行：它们会替换进程级静态 AppHttpClient，并行会互相踩踏
[Collection<HttpStubCollectionDefinition>]
public class OpusFetcherTests
{
    // opus/detail 带 htmlNewStyle 时用 item.basic.rid_str 回落到 cv 号；type==1 表示专栏动态
    private const string OpusDetailJson = """
    {
      "code": 0,
      "data": {
        "item": {
          "type": 1,
          "basic": { "comment_id_str": "51908655", "rid_str": "51908655", "title": "专栏标题", "uid": 12345 },
          "modules": [
            {
              "module_type": "MODULE_TYPE_TOP",
              "module_top": {
                "display": { "type": 1, "album": { "pics": [ { "url": "//i0.hdslb.com/top.jpg" } ] }, "video": null }
              }
            }
          ]
        }
      }
    }
    """;

    // 纯图文动态：type==0，rid_str 不是 cv 号，正文在 MODULE_TYPE_CONTENT，顶部相册在 MODULE_TYPE_TOP
    private const string Type0DynamicJson = """
    {
      "code": 0,
      "data": {
        "item": {
          "type": 0,
          "basic": { "comment_id_str": "356382283", "rid_str": "356382283", "title": "动态标题", "uid": 201296348 },
          "modules": [
            {
              "module_type": "MODULE_TYPE_TOP",
              "module_top": {
                "display": {
                  "type": 1,
                  "album": {
                    "pics": [
                      { "url": "http://i0.hdslb.com/bfs/new_dyn/a.jpg" },
                      { "url": "http://i0.hdslb.com/bfs/new_dyn/b.jpg" }
                    ]
                  },
                  "video": null
                }
              }
            },
            { "module_type": "MODULE_TYPE_TITLE", "module_title": { "text": "身边故事" } },
            {
              "module_type": "MODULE_TYPE_CONTENT",
              "module_content": {
                "paragraphs": [
                  {
                    "para_type": 1,
                    "text": {
                      "nodes": [
                        { "word": { "words": "投稿人：" } },
                        { "rich": { "orig_text": "@一本正经的米娜里", "jump_url": "", "rid": "453198525" } }
                      ]
                    }
                  }
                ]
              }
            }
          ]
        }
      }
    }
    """;

    private const string ArticleViewJson = """
    {
      "code": 0,
      "data": {
        "title": "专栏标题",
        "summary": "摘要",
        "publish_time": 1700000000,
        "dyn_id_str": "1230485246732926996",
        "author": { "name": "作者名", "mid": 12345 },
        "tags": [ { "name": "标签A" }, { "name": "标签B" } ],
        "opus": {
          "content": {
            "paragraphs": [
              { "para_type": 1, "text": { "nodes": [ { "word": { "words": "第一段", "font_size": 17 } } ] } },
              { "para_type": 1, "text": { "nodes": [ { "word": { "words": "大标题", "font_size": 24, "style": { "bold": true } } } ] } },
              { "para_type": 2, "pic": { "pics": [ { "url": "https://i0.hdslb.com/a.png", "width": 100, "height": 200 } ] } },
              { "para_type": 3 },
              { "para_type": 4, "text": { "nodes": [ { "word": { "words": "引用文字" } } ] } },
              { "para_type": 5, "list": { "style": 1, "items": [ { "level": 1, "order": 1, "nodes": [ { "word": { "words": "条目一" } } ] } ] } },
              { "code": { "lang": "language-python", "content": "print(1)" } },
              { "text": { "nodes": [ { "rich": { "orig_text": "站内链接", "jump_url": "//www.bilibili.com/video/BV1?spm=1" } } ] } }
            ]
          }
        }
      }
    }
    """;

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(responder(request));
        }
    }

    private static HttpResponseMessage Ok(string body)
    {
        return new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    private static async Task<T> WithRoutedStub<T>(Func<HttpRequestMessage, HttpResponseMessage> responder, Func<Task<T>> act)
    {
        var original = HTTPUtil.AppHttpClient;
        using var handler = new StubHttpMessageHandler(responder);
        using var client = new HttpClient(handler, disposeHandler: false);
        HTTPUtil.AppHttpClient = client;
        try
        {
            return await act( );
        }
        finally
        {
            HTTPUtil.AppHttpClient = original;
        }
    }

    [Fact]
    public async Task FetchAsync_OpusId_ResolvesCvThenParsesArticle( )
    {
        var urls = new List<string>( );
        var doc = await WithRoutedStub(req =>
        {
            var url = req.RequestUri!.AbsoluteUri;
            urls.Add(url);
            return url.Contains("/opus/detail", StringComparison.Ordinal) ? Ok(OpusDetailJson) : Ok(ArticleViewJson);
        }, ( ) => OpusFetcher.FetchAsync(new OpusTarget("1230485246732926996", ""), AppConfig.Empty, TestContext.Current.CancellationToken));

        Assert.Equal(2, urls.Count);
        Assert.Contains("id=1230485246732926996", urls[0], StringComparison.Ordinal);
        Assert.Contains("htmlNewStyle", urls[0], StringComparison.Ordinal);
        Assert.Contains("/x/article/view?id=51908655", urls[1], StringComparison.Ordinal);

        Assert.Equal("专栏标题", doc.Title);
        Assert.Equal("作者名", doc.AuthorName);
        Assert.Equal("12345", doc.AuthorMid);
        Assert.Equal("51908655", doc.CvId);
        Assert.Equal(1700000000, doc.PublishTime);
        Assert.Equal(["标签A", "标签B"], doc.Tags);

        // 专栏顶部相册图（module_top）置于正文最前
        Assert.Equal(OpusParagraphKind.Image, doc.Paragraphs[0].Kind);
        Assert.Equal("//i0.hdslb.com/top.jpg", doc.Paragraphs[0].Images[0].Url);
    }

    // 已知 cv 号时不该多打一次 opus/detail
    [Fact]
    public async Task FetchAsync_KnownCvId_SkipsOpusDetailRequest( )
    {
        var urls = new List<string>( );
        var doc = await WithRoutedStub(req =>
        {
            urls.Add(req.RequestUri!.AbsoluteUri);
            return Ok(ArticleViewJson);
        }, ( ) => OpusFetcher.FetchAsync(new OpusTarget("", "51908655"), AppConfig.Empty, TestContext.Current.CancellationToken));

        Assert.Single(urls);
        Assert.Contains("/x/article/view", urls[0], StringComparison.Ordinal);
        Assert.Equal("51908655", doc.CvId);
    }

    // 纯图文动态（type==0）：rid_str 不是 cv 号，不请求 article/view，直接按动态导出
    [Fact]
    public async Task FetchAsync_Type0Dynamic_ExportsAsImageText( )
    {
        var urls = new List<string>( );
        var doc = await WithRoutedStub(req =>
        {
            urls.Add(req.RequestUri!.AbsoluteUri);
            return Ok(Type0DynamicJson);
        }, ( ) => OpusFetcher.FetchAsync(new OpusTarget("1084525121139376134", ""), AppConfig.Empty, TestContext.Current.CancellationToken));

        Assert.Single(urls);
        Assert.Contains("/opus/detail", urls[0], StringComparison.Ordinal);
        Assert.Equal("", doc.CvId);
        Assert.Equal("动态标题", doc.Title);

        // 顶部相册图置于正文最前
        var top = doc.Paragraphs[0];
        Assert.Equal(OpusParagraphKind.Image, top.Kind);
        Assert.Equal(2, top.Images.Count);
        Assert.Equal("http://i0.hdslb.com/bfs/new_dyn/a.jpg", top.Images[0].Url);

        var p = doc.Paragraphs[1];
        Assert.Equal(OpusParagraphKind.Text, p.Kind);
        Assert.Equal("投稿人：", p.TextNodes[0].Text);
        Assert.Equal("@一本正经的米娜里", p.TextNodes[1].Text);
        Assert.Null(p.TextNodes[1].Url);
    }

    [Fact]
    public async Task FetchAsync_ParagraphKinds_AreDetectedByStructure( )
    {
        var doc = await WithRoutedStub(_ => Ok(ArticleViewJson),
            ( ) => OpusFetcher.FetchAsync(new OpusTarget("", "51908655"), AppConfig.Empty, TestContext.Current.CancellationToken));

        var kinds = doc.Paragraphs.ConvertAll(p => p.Kind);
        Assert.Equal(
            [
                OpusParagraphKind.Text,
                OpusParagraphKind.Heading,
                OpusParagraphKind.Image,
                OpusParagraphKind.Divider,
                OpusParagraphKind.Quote,
                OpusParagraphKind.List,
                OpusParagraphKind.Code,
                OpusParagraphKind.Text,
            ],
            kinds);

        Assert.Equal(2, doc.Paragraphs[1].HeadingLevel);
        Assert.Equal("https://i0.hdslb.com/a.png", doc.Paragraphs[2].Images[0].Url);
        Assert.Equal(OpusListStyle.Ordered, doc.Paragraphs[5].ListStyle);
        Assert.Equal("language-python", doc.Paragraphs[6].CodeLang);
        Assert.Equal("//www.bilibili.com/video/BV1?spm=1", doc.Paragraphs[7].TextNodes[0].Url);
    }

    [Fact]
    public async Task FetchAsync_LegacyHtmlArticle_FallsBackToHtmlConversion( )
    {
        const string LegacyJson = """
        {
          "code": 0,
          "data": {
            "title": "旧版专栏",
            "type": 0,
            "author": { "name": "作者", "mid": 1 },
            "content": "<p>段落一</p><p><strong>加粗</strong></p><img src=\"https://i0.hdslb.com/x.png\"><figure class=\"img-box\"><img src=\"//i0.hdslb.com/b.png\"></figure><p><span class=\"color-pink-03\">粉字</span></p>"
          }
        }
        """;

        var doc = await WithRoutedStub(_ => Ok(LegacyJson),
            ( ) => OpusFetcher.FetchAsync(new OpusTarget("", "1"), AppConfig.Empty, TestContext.Current.CancellationToken));

        var node = doc.Paragraphs[0].TextNodes[0];
        var text = node.Text!;
        Assert.True(node.IsRawMarkdown);
        Assert.Contains("段落一", text, StringComparison.Ordinal);
        Assert.Contains("**加粗**", text, StringComparison.Ordinal);
        // 图片与样式标签原样保留，不转 ![]()
        Assert.Contains("<img src=\"https://i0.hdslb.com/x.png\">", text, StringComparison.Ordinal);
        Assert.Contains("<figure class=\"img-box\"><img src=\"//i0.hdslb.com/b.png\"></figure>", text, StringComparison.Ordinal);
        Assert.Contains("<span class=\"color-pink-03\">粉字</span>", text, StringComparison.Ordinal);
        Assert.DoesNotContain("![", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FetchAsync_EmptyContent_Throws( )
    {
        const string EmptyJson = """{ "code": 0, "data": { "title": "空", "author": { "name": "作者" } } }""";

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(( ) => WithRoutedStub(_ => Ok(EmptyJson),
            ( ) => OpusFetcher.FetchAsync(new OpusTarget("", "1"), AppConfig.Empty, TestContext.Current.CancellationToken)));
        Assert.Contains("专栏正文为空", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(-404, "专栏不存在或已被删除")]
    [InlineData(-403, "无权访问")]
    [InlineData(-352, "风控")]
    public async Task FetchAsync_ApiErrorCode_MappedToFriendlyMessage(int code, string expectedFragment)
    {
        var body = $$"""{ "code": {{code}}, "message": "err", "data": null }""";

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(( ) => WithRoutedStub(_ => Ok(body),
            ( ) => OpusFetcher.FetchAsync(new OpusTarget("", "1"), AppConfig.Empty, TestContext.Current.CancellationToken)));
        Assert.Contains(expectedFragment, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TryGetCvId_PrefersFallbackWhenTypeIsArticle( )
    {
        using var doc = JsonDocument.Parse("""
        { "fallback": { "type": 2, "id": "12345" }, "item": { "basic": { "rid_str": "99999" } } }
        """);
        Assert.Equal("12345", OpusFetcher.TryGetCvId(doc.RootElement));
    }

    [Fact]
    public void TryGetCvId_NonArticleFallback_ReturnsNullWhenNoRid( )
    {
        using var doc = JsonDocument.Parse("""{ "fallback": { "type": 1, "id": "12345" }, "item": { "basic": {} } }""");
        Assert.Null(OpusFetcher.TryGetCvId(doc.RootElement));
    }

    // 纯动态（type==0）的 rid_str 不是 cv 号，必须返回 null 走图文动态导出
    [Fact]
    public void TryGetCvId_Type0Dynamic_ReturnsNull( )
    {
        using var doc = JsonDocument.Parse("""{ "fallback": null, "item": { "type": 0, "basic": { "rid_str": "356382283" } } }""");
        Assert.Null(OpusFetcher.TryGetCvId(doc.RootElement));
    }

    [Fact]
    public void TryGetCvId_Type1Article_UsesRidStr( )
    {
        using var doc = JsonDocument.Parse("""{ "item": { "type": 1, "basic": { "rid_str": "51908655" } } }""");
        Assert.Equal("51908655", OpusFetcher.TryGetCvId(doc.RootElement));
    }

    // para_type 3 的 line.pic 是图片（figure 里的图），line.line_type 才是分割线
    [Fact]
    public void ParseParagraph_ParaType3WithLinePic_ReturnsImage( )
    {
        using var doc = JsonDocument.Parse("""
        { "para_type": 3, "line": { "pic": { "url": "//i0.hdslb.com/bfs/article/a.png" } } }
        """);
        var p = OpusFetcher.ParseParagraph(doc.RootElement);
        Assert.Equal(OpusParagraphKind.Image, p.Kind);
        Assert.Equal("//i0.hdslb.com/bfs/article/a.png", p.Images[0].Url);
    }

    [Fact]
    public void ParseParagraph_ParaType3WithLineType_ReturnsDivider( )
    {
        using var doc = JsonDocument.Parse("""{ "para_type": 3, "line": { "line_type": 1 } }""");
        Assert.Equal(OpusParagraphKind.Divider, OpusFetcher.ParseParagraph(doc.RootElement).Kind);
    }

    // article/view 版 link_card 只有 show_text / biz_id，没有 title / jump_url
    [Fact]
    public void ParseParagraph_LinkCardWithoutTitle_UsesShowText( )
    {
        using var doc = JsonDocument.Parse("""
        { "para_type": 7, "link_card": { "card": { "show_text": "马场芳郎【某科学的超电磁炮】", "link_type": 3, "biz_id": "20233251" } } }
        """);
        var p = OpusFetcher.ParseParagraph(doc.RootElement);
        Assert.Equal(OpusParagraphKind.LinkCard, p.Kind);
        Assert.Equal("马场芳郎【某科学的超电磁炮】", p.LinkTitle);
        Assert.Equal("", p.LinkUrl);
    }

    // 缺陷回归：图文动态里图片段落的 text 为 null（属性存在而非缺失），TryGetProperty 对 null 元素
    // 抛 InvalidOperationException（JsonElementHasWrongType, Object, Null），须按缺失处理而非下钻
    [Fact]
    public void ParseOpusDetail_ImageParagraphWithNullText_DoesNotThrow( )
    {
        const string json = """
        {
          "item": {
            "basic": { "title": "动态", "uid": 1 },
            "modules": [
              {
                "module_type": "MODULE_TYPE_CONTENT",
                "module_content": {
                  "paragraphs": [
                    {
                      "para_type": 1,
                      "format": { "align": 0, "indent": null },
                      "text": { "nodes": [ { "word": { "words": "文字" }, "rich": null, "formula": null } ] }
                    },
                    { "para_type": 2, "text": null, "pic": { "pics": [ { "url": "http://i0.hdslb.com/a.jpg" } ] } }
                  ]
                }
              }
            ]
          }
        }
        """;
        using var doc = JsonDocument.Parse(json);
        var parsed = OpusFetcher.ParseOpusDetail(doc.RootElement, "url");

        Assert.Equal(2, parsed.Paragraphs.Count);
        Assert.Equal(OpusParagraphKind.Text, parsed.Paragraphs[0].Kind);
        Assert.Equal("文字", parsed.Paragraphs[0].TextNodes[0].Text);
        Assert.Equal(OpusParagraphKind.Image, parsed.Paragraphs[1].Kind);
        Assert.Equal("http://i0.hdslb.com/a.jpg", parsed.Paragraphs[1].Images[0].Url);
    }
}
