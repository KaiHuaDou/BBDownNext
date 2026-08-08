using System.Text.Json;

using BBDown.Core.Fetcher;

namespace BBDown.Core.Tests;

// 国内番剧与 INTL 番剧共用的分集构造
public class EpisodePagesTests
{
    private static JsonElement Parse(string json)
    {
        return JsonDocument.Parse(json).RootElement.Clone( );
    }

    private const string Episodes = """
        [
          {"id":1,"aid":10,"cid":100,"title":"1","long_title":"起点","pub_time":1700000000,
           "duration":2826000,"dimension":{"width":1920,"height":1080}},
          {"id":2,"aid":11,"cid":101,"title":"PV","long_title":"","badge":"预告",
           "dimension":{"width":1920,"height":1080}},
          {"id":3,"aid":12,"cid":102,"title":"2","long_title":"终点"}
        ]
        """;

    [Fact]
    public void BuildEpisodePages_SkipsTrailersAndKeepsIndexContiguous( )
    {
        var pages = BangumiInfoFetcher.BuildEpisodePages(Parse(Episodes));

        Assert.Equal(2, pages.Count);
        Assert.Equal([1, 2], pages.ConvertAll(p => p.Index));
        Assert.Equal(["1", "3"], pages.ConvertAll(p => p.EpId));
    }

    [Fact]
    public void BuildEpisodePages_JoinsTitleAndTrims( )
    {
        var pages = BangumiInfoFetcher.BuildEpisodePages(Parse(Episodes));

        Assert.Equal("1 起点", pages[0].Title);
        Assert.Equal("2 终点", pages[1].Title);
    }

    // dimension / pub_time 在部分分集上缺失，旧实现分别靠 catch 和 TryGetProperty 兜底，行为不一致
    [Fact]
    public void BuildEpisodePages_ToleratesMissingDimensionAndPubTime( )
    {
        var pages = BangumiInfoFetcher.BuildEpisodePages(Parse(Episodes));

        Assert.Equal("1920x1080", pages[0].Res);
        Assert.Equal(1700000000, pages[0].PubTime);
        Assert.Equal("", pages[1].Res);
        Assert.Equal(0, pages[1].PubTime);
    }

    // 毫秒到秒的换算与脏值容忍在 JsonUtilTests 覆盖，这里只确认 dur 确实被填进 Page
    [Fact]
    public void BuildEpisodePages_FillsDurationInSeconds( )
    {
        var pages = BangumiInfoFetcher.BuildEpisodePages(Parse(Episodes));

        Assert.Equal(2826, pages[0].Dur);
        Assert.Equal(0, pages[1].Dur);
    }

    [Fact]
    public void BuildEpisodePages_NonArrayGivesEmptyList( )
    {
        Assert.Empty(BangumiInfoFetcher.BuildEpisodePages(Parse("{}")));
        Assert.Empty(BangumiInfoFetcher.BuildEpisodePages(default));
    }
}
