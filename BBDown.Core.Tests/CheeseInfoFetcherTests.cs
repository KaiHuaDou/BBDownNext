using System.Text.Json;
using System.Threading.Tasks;

using BBDown.Core.Fetcher;

namespace BBDown.Core.Tests;

public class CheeseInfoFetcherTests
{
    [Fact]
    public void BuildPages_IncludesWatchableAndSkipsLocked( )
    {
        const string json = """
        {
          "episodes": [
            { "aid": 1, "cid": 11, "id": 101, "index": 1, "title": "可看1", "duration": 100, "release_date": 1, "status": 1 },
            { "aid": 2, "cid": 12, "id": 102, "index": 2, "title": "锁定",  "duration": 200, "release_date": 2, "status": 2 },
            { "aid": 3, "cid": 13, "id": 103, "index": 3, "title": "可看2", "duration": 300, "release_date": 3 }
          ]
        }
        """;
        using var doc = JsonDocument.Parse(json);
        var pages = CheeseInfoFetcher.BuildPages(doc.RootElement.GetProperty("episodes"), "up", "666");

        // status=2 的「锁定」分集被跳过，仅保留可观看分集，且 index 沿用接口值而非自增。
        Assert.Equal(2, pages.Count);
        Assert.Equal("101", pages[0].EpId);
        Assert.Equal("103", pages[1].EpId);
        Assert.Equal(1, pages[0].Index);
        Assert.Equal(3, pages[1].Index);
        Assert.Equal("up", pages[0].OwnerName);
        Assert.Equal("666", pages[0].OwnerMid);
    }

    [Fact]
    public void BuildPages_MissingStatus_DefaultsToWatchable( )
    {
        // 接口偶尔省略 status 字段，应默认视为可观看，避免误删。
        const string json = """
        { "episodes": [ { "aid": 1, "cid": 11, "id": 101, "index": 1, "title": "无状态", "duration": 100, "release_date": 1 } ] }
        """;
        using var doc = JsonDocument.Parse(json);
        var pages = CheeseInfoFetcher.BuildPages(doc.RootElement.GetProperty("episodes"), "up", "666");

        Assert.Single(pages);
        Assert.Equal("101", pages[0].EpId);
    }

    [Fact]
    public void BuildPages_AllLocked_ReturnsEmpty( )
    {
        // 全部锁定时返回空列表，FetchAsync 据此抛出明确错误而非逐集报 -403。
        const string json = """
        { "episodes": [ { "aid": 2, "cid": 12, "id": 102, "index": 2, "title": "锁定", "duration": 200, "release_date": 2, "status": 2 } ] }
        """;
        using var doc = JsonDocument.Parse(json);
        var pages = CheeseInfoFetcher.BuildPages(doc.RootElement.GetProperty("episodes"), "up", "666");

        Assert.Empty(pages);
    }

    [Fact]
    public async Task Fetch_MissingUpInfo_DefaultsToEmptyOwner( )
    {
        // up_info 缺失时不应抛 KeyNotFoundException，UP 主信息退化为空
        const string json = """
        {
          "data": {
            "cover": "http://cover/x.jpg",
            "title": "课程",
            "subtitle": "副标题",
            "episodes": [
              { "aid": 1, "cid": 11, "id": 101, "index": 1, "title": "第1集", "duration": 100, "release_date": 1, "status": 1 }
            ]
          }
        }
        """;
        var info = await HttpStub.WithJsonResponse(json,
            ( ) => CheeseInfoFetcher.FetchAsync(new ResourceId.CheeseEp(101), AppConfig.Empty));

        Assert.Equal("课程", info.Title);
        Assert.Equal("101", info.PagesInfo[0].EpId);
        Assert.Equal("", info.PagesInfo[0].OwnerName);
        Assert.Equal("", info.PagesInfo[0].OwnerMid);
    }
}
