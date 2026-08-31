using System.Text.Json;

namespace BBDown.Core.Tests;

// TryGetOpus 为纯函数（JsonElement 内存输入，与 TrackReader 系列同性质）：
// 动态 entry 结构取自 bilibili-API-collect docs/dynamic/space.md 的响应示例
public class SpaceOpusDownloadTests
{
    private static JsonElement Parse(string json)
    {
        return JsonDocument.Parse(json).RootElement.Clone( );
    }

    [Fact]
    public void TryGetOpus_MajorTypeOpus_ExtractsItem( )
    {
        var entry = Parse("""
        {
          "id_str": "1063487284684259332",
          "modules": {
            "module_author": { "name": "UP主名" },
            "module_dynamic": {
              "major": {
                "type": "MAJOR_TYPE_OPUS",
                "opus": { "title": "图文标题" }
              }
            }
          }
        }
        """);
        Assert.True(SpaceOpusDownload.TryGetOpus(entry, out var item));
        Assert.Equal(new SpaceOpusDownload.OpusItem("1063487284684259332", "图文标题", "UP主名"), item);
    }

    [Fact]
    public void TryGetOpus_MajorTypeOpusWithoutOpusNode_EmptyTitle( )
    {
        // 图文动态的 major.opus 可能缺失（纯图片动态无标题节点），仍应提取动态 id
        var entry = Parse("""
        {
          "id_str": "123",
          "modules": { "module_dynamic": { "major": { "type": "MAJOR_TYPE_OPUS" } } }
        }
        """);
        Assert.True(SpaceOpusDownload.TryGetOpus(entry, out var item));
        Assert.Equal("123", item.OpusId);
        Assert.Equal("", item.Title);
        Assert.Equal("", item.Author);
    }

    [Theory]
    [InlineData("MAJOR_TYPE_ARCHIVE")]
    [InlineData("MAJOR_TYPE_NONE")]
    [InlineData("MAJOR_TYPE_LIVE_RCMD")]
    [InlineData("MAJOR_TYPE_NOTE")]
    public void TryGetOpus_NonOpusMajor_Rejected(string majorType)
    {
        // 视频 / 转发 / 直播 / 笔记等类型均不在提取范围（用户确认仅图文）
        var entry = Parse($$"""
        {
          "id_str": "123",
          "modules": { "module_dynamic": { "major": { "type": "{{majorType}}" } } }
        }
        """);
        Assert.False(SpaceOpusDownload.TryGetOpus(entry, out _));
    }

    [Fact]
    public void TryGetOpus_MissingIdStr_Rejected( )
    {
        var entry = Parse("""
        { "modules": { "module_dynamic": { "major": { "type": "MAJOR_TYPE_OPUS" } } } }
        """);
        Assert.False(SpaceOpusDownload.TryGetOpus(entry, out _));
    }

    [Fact]
    public void TryGetOpus_MissingModulesOrMajor_Rejected( )
    {
        Assert.False(SpaceOpusDownload.TryGetOpus(Parse("""{ "id_str": "123" }"""), out _));
        Assert.False(SpaceOpusDownload.TryGetOpus(Parse("""{ "id_str": "123", "modules": {} }"""), out _));
        Assert.False(SpaceOpusDownload.TryGetOpus(Parse("""{ "id_str": "123", "modules": { "module_dynamic": {} } }"""), out _));
    }

    [Fact]
    public void TryGetOpus_NonObjectEntry_Rejected( )
    {
        using var doc = JsonDocument.Parse("""[1, 2]""");
        Assert.False(SpaceOpusDownload.TryGetOpus(doc.RootElement, out _));
    }
}
