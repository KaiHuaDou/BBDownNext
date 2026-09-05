using System.Text.Json;

namespace BBDown.Core.Tests;

// TryResolveItem 为纯函数（JsonElement 内存输入，与 TrackReader 系列同性质）：
// 动态 entry 结构取自 bilibili-API-collect docs/dynamic/space.md 的响应示例
public class SpaceDynamicDownloadTests
{
    private static JsonElement Parse(string json)
    {
        return JsonDocument.Parse(json).RootElement.Clone( );
    }

    [Fact]
    public void TryResolveItem_MajorTypeOpus_ExtractsOpusId( )
    {
        var entry = Parse("""
        {
          "id_str": "1063487284684259332",
          "modules": {
            "module_dynamic": {
              "major": { "type": "MAJOR_TYPE_OPUS", "opus": { "title": "图文标题" } }
            }
          }
        }
        """);
        Assert.True(SpaceDynamicDownload.TryResolveItem(entry, 0, out var item));
        Assert.Equal(new SpaceDynamicDownload.DynamicItem("1063487284684259332", ""), item);
        Assert.True(item.HasOpus);
        Assert.False(item.HasVideo);
    }

    [Fact]
    public void TryResolveItem_MajorTypeArchive_ExtractsBvId( )
    {
        var entry = Parse("""
        {
          "id_str": "716526237365829703",
          "modules": {
            "module_dynamic": {
              "major": {
                "type": "MAJOR_TYPE_ARCHIVE",
                "archive": { "aid": "BV 来源", "bvid": "BV1vmZAYDEcT", "title": "上号成为人机了！" }
              }
            }
          }
        }
        """);
        Assert.True(SpaceDynamicDownload.TryResolveItem(entry, 0, out var item));
        Assert.Equal(new SpaceDynamicDownload.DynamicItem("", "BV1vmZAYDEcT"), item);
        Assert.True(item.HasVideo);
        Assert.False(item.HasOpus);
    }

    [Fact]
    public void TryResolveItem_ForwardOfArchive_ResolvedViaOrig( )
    {
        // 转发条目 major 缺失（MAJOR_TYPE_NONE），内容在 orig（与根原动态同构）
        var entry = Parse("""
        {
          "id_str": "866756840240709701",
          "modules": {
            "module_dynamic": {
              "orig": {
                "id_str": "716526237365829703",
                "modules": {
                  "module_dynamic": {
                    "major": {
                      "type": "MAJOR_TYPE_ARCHIVE",
                      "archive": { "bvid": "BV1vmZAYDEcT" }
                    }
                  }
                }
              }
            }
          }
        }
        """);
        Assert.True(SpaceDynamicDownload.TryResolveItem(entry, 0, out var item));
        Assert.Equal(new SpaceDynamicDownload.DynamicItem("", "BV1vmZAYDEcT"), item);
    }

    [Fact]
    public void TryResolveItem_ForwardOfOpus_ResolvedViaOrig( )
    {
        var entry = Parse("""
        {
          "id_str": "866756840240709701",
          "modules": {
            "module_dynamic": {
              "orig": {
                "id_str": "1063487284684259332",
                "modules": {
                  "module_dynamic": {
                    "major": { "type": "MAJOR_TYPE_OPUS" }
                  }
                }
              }
            }
          }
        }
        """);
        Assert.True(SpaceDynamicDownload.TryResolveItem(entry, 0, out var item));
        Assert.Equal(new SpaceDynamicDownload.DynamicItem("1063487284684259332", ""), item);
    }

    [Theory]
    [InlineData("MAJOR_TYPE_LIVE_RCMD")]
    [InlineData("MAJOR_TYPE_PGC")]
    [InlineData("MAJOR_TYPE_DRAW")]
    [InlineData("MAJOR_TYPE_NONE")]
    [InlineData("MAJOR_TYPE_COURSES")]
    public void TryResolveItem_UnsupportedMajor_Rejected(string majorType)
    {
        // 直播 / 剧集 / 带图 / 失效等类型不在下载范围；NONE 无 orig（非转发形态）同样拒绝
        var entry = Parse($$"""
        {
          "id_str": "123",
          "modules": { "module_dynamic": { "major": { "type": "{{majorType}}" } } }
        }
        """);
        Assert.False(SpaceDynamicDownload.TryResolveItem(entry, 0, out _));
    }

    [Fact]
    public void TryResolveItem_ArchiveWithoutBvId_Rejected( )
    {
        var entry = Parse("""
        {
          "id_str": "123",
          "modules": { "module_dynamic": { "major": { "type": "MAJOR_TYPE_ARCHIVE", "archive": { } } } }
        }
        """);
        Assert.False(SpaceDynamicDownload.TryResolveItem(entry, 0, out _));
    }

    [Fact]
    public void TryResolveItem_OpusWithoutIdStr_Rejected( )
    {
        var entry = Parse("""
        {
          "modules": { "module_dynamic": { "major": { "type": "MAJOR_TYPE_OPUS" } } }
        }
        """);
        Assert.False(SpaceDynamicDownload.TryResolveItem(entry, 0, out _));
    }

    [Fact]
    public void TryResolveItem_MissingModulesOrMajor_Rejected( )
    {
        Assert.False(SpaceDynamicDownload.TryResolveItem(Parse("""{ "id_str": "123" }"""), 0, out _));
        Assert.False(SpaceDynamicDownload.TryResolveItem(Parse("""{ "id_str": "123", "modules": { } }"""), 0, out _));
        Assert.False(SpaceDynamicDownload.TryResolveItem(Parse("""{ "id_str": "123", "modules": { "module_dynamic": { } } }"""), 0, out _));
    }

    [Fact]
    public void TryResolveItem_NonObjectEntry_Rejected( )
    {
        using var doc = JsonDocument.Parse("""[1, 2]""");
        Assert.False(SpaceDynamicDownload.TryResolveItem(doc.RootElement, 0, out _));
    }

    [Fact]
    public void TryResolveItem_ForwardDepthExceeded_Rejected( )
    {
        // 自嵌套转发数据异常时按深度上限止损（正常 orig 一层即达根原动态）；
        // 第 4 层仍见 orig（depth == MaxForwardDepth）即整体拒绝
        var entry = Parse("""
        {
          "id_str": "1",
          "modules": {
            "module_dynamic": {
              "orig": {
                "id_str": "2",
                "modules": {
                  "module_dynamic": {
                    "orig": {
                      "id_str": "3",
                      "modules": {
                        "module_dynamic": {
                          "orig": {
                            "id_str": "4",
                            "modules": {
                              "module_dynamic": {
                                "orig": {
                                  "id_str": "5",
                                  "modules": {
                                    "module_dynamic": {
                                      "major": { "type": "MAJOR_TYPE_OPUS" }
                                    }
                                  }
                                }
                              }
                            }
                          }
                        }
                      }
                    }
                  }
                }
              }
            }
          }
        }
        """);
        Assert.False(SpaceDynamicDownload.TryResolveItem(entry, 0, out _));
    }
}
