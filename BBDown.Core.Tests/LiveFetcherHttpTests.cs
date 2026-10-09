using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BBDown.Core.Tests;

[Collection<HttpStubCollectionDefinition>]
public class LiveFetcherHttpTests
{
    private const string RoomInitJson = """
    {"code":0,"msg":"ok","message":"ok","data":{"room_id":23058,"short_id":3,"uid":11153765,
    "is_hidden":false,"is_locked":false,"is_portrait":false,"live_status":1,"hidden_till":0,
    "lock_till":0,"encrypted":false,"pwd_verified":false,"live_time":1700000000,"room_shield":1}}
    """;

    private const string RoomBaseInfoJson = """
    {"code":0,"message":"OK","ttl":1,"data":{"by_uids":{},"by_room_ids":{"23058":{
    "room_id":23058,"uid":11153765,"live_status":1,"title":"哔哩哔哩音悦台","uname":"3号直播间",
    "cover":"https://i0.hdslb.com/bfs/live/cover.jpg","short_id":3}}}}
    """;

    // 短号 3 必须先经 room_init 换成真实房间号 23058，后续接口才查得到
    [Fact]
    public async Task FetchRoomAsync_ShortId_ResolvesToRealRoomId( )
    {
        var requested = new List<string>( );
        var info = await HttpStub.WithRoute(
            url =>
            {
                requested.Add(url);
                return url.Contains("room_init", StringComparison.Ordinal) ? RoomInitJson : RoomBaseInfoJson;
            },
            ( ) => LiveFetcher.FetchRoomAsync(new LiveTarget("3"), new AppConfig( ), TestContext.Current.CancellationToken));

        Assert.Equal("23058", info.RoomId);
        Assert.Equal("3", info.ShortId);
        Assert.Equal("11153765", info.Uid);
        Assert.Equal("3号直播间", info.Uname);
        Assert.Equal("哔哩哔哩音悦台", info.Title);
        Assert.True(info.IsLiving);
        Assert.Contains(requested, u => u.Contains("room_ids=23058", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FetchRoomAsync_NotLiving_IsLivingFalse( )
    {
        var info = await HttpStub.WithRoute(
            url => url.Contains("room_init", StringComparison.Ordinal)
                ? RoomInitJson.Replace("\"live_status\":1", "\"live_status\":0", StringComparison.Ordinal)
                : RoomBaseInfoJson,
            ( ) => LiveFetcher.FetchRoomAsync(new LiveTarget("3"), new AppConfig( ), TestContext.Current.CancellationToken));

        Assert.False(info.IsLiving);
    }

    [Fact]
    public async Task FetchRoomAsync_ApiError_Throws( )
    {
        var ex = await Assert.ThrowsAsync<ApiException>(( ) => HttpStub.WithRoute(
            _ => """{"code":1,"message":"房间不存在","data":null}""",
            ( ) => LiveFetcher.FetchRoomAsync(new LiveTarget("999999999"), new AppConfig( ), TestContext.Current.CancellationToken)));

        Assert.Equal(1, ex.Code);
    }

    [Fact]
    public async Task FetchPlayInfoAsync_PassesQnAndRoomId( )
    {
        string? seen = null;
        await HttpStub.WithRoute(
            url =>
            {
                seen = url;
                return """{"code":0,"data":{"live_status":0}}""";
            },
            ( ) => LiveFetcher.FetchPlayInfoAsync("23058", 400, new AppConfig( ), TestContext.Current.CancellationToken));

        Assert.NotNull(seen);
        Assert.Contains("room_id=23058", seen, StringComparison.Ordinal);
        Assert.Contains("qn=400", seen, StringComparison.Ordinal);
        Assert.Contains("protocol=0", seen, StringComparison.Ordinal);
        Assert.Contains("format=0", seen, StringComparison.Ordinal);
    }
}
