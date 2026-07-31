using System.Linq;

using BBDown.Core.Entity;
using BBDown.Core.Protobuf;

using static BBDown.Core.Entity.Entity;

namespace BBDown.Core.Tests;

// Parser.BuildAppParsedResult 与旧 JSON 链路逐字段对比。
// 旧链路删除后本文件一并移除, 新链路的行为由 AppParserTests 单独钉住。
public class AppParserParityTests
{
    private const string Aid = "114514";
    private const string Cid = "1919810";

    private static (ParsedResult Legacy, ParsedResult Current) Both(PlayViewReply reply, bool isEpisode)
    {
        return (AppLegacyParseTests.ParseViaLegacyJson(reply, isEpisode, Aid, Cid),
            Parser.BuildAppParsedResult(reply, isEpisode, Aid, Cid));
    }

    // Video.Equals 不比较 baseUrl / size, Audio.Equals 不比较 baseUrl, 都得单独拎出来比
    private static void AssertTracksMatch(ParsedResult legacy, ParsedResult current)
    {
        Assert.Equal(legacy.VideoTracks, current.VideoTracks);
        Assert.Equal(legacy.VideoTracks.Select(v => v.baseUrl), current.VideoTracks.Select(v => v.baseUrl));

        Assert.Equal(legacy.AudioTracks, current.AudioTracks);
        Assert.Equal(legacy.AudioTracks.Select(a => a.baseUrl), current.AudioTracks.Select(a => a.baseUrl));

        Assert.Equal(legacy.BackgroundAudioTracks, current.BackgroundAudioTracks);
        Assert.Equal(legacy.BackgroundAudioTracks.Select(a => a.baseUrl), current.BackgroundAudioTracks.Select(a => a.baseUrl));

        Assert.Equal(legacy.ExtraPoints, current.ExtraPoints);
        Assert.Equal(legacy.Clips, current.Clips);
        Assert.Equal(legacy.Dfns, current.Dfns);
    }

    [Fact]
    public void Ugc_Matches( )
    {
        var (legacy, current) = Both(PlayViewReplyFixtures.Ugc( ), false);
        AssertTracksMatch(legacy, current);
        Assert.Empty(current.RoleAudioList);
    }

    [Fact]
    public void Bangumi_Matches( )
    {
        var (legacy, current) = Both(PlayViewReplyFixtures.Bangumi( ), true);
        AssertTracksMatch(legacy, current);

        // AudioMaterialInfo 是 record 但带 List<Audio>, 结构比较会退化成引用比较, 只能逐字段比
        Assert.Equal(legacy.RoleAudioList.Count, current.RoleAudioList.Count);
        foreach (var (l, c) in legacy.RoleAudioList.Zip(current.RoleAudioList))
        {
            Assert.Equal(l.path, c.path);
            Assert.Equal(l.audio, c.audio);
            Assert.Equal(l.audio.Select(a => a.baseUrl), c.audio.Select(a => a.baseUrl));
        }
    }

    [Fact]
    public void Empty_ProducesNoTracksInsteadOfThrowing( )
    {
        var current = Parser.BuildAppParsedResult(PlayViewReplyFixtures.Empty( ), true, Aid, Cid);

        Assert.Empty(current.VideoTracks);
        Assert.Empty(current.AudioTracks);
        Assert.Empty(current.BackgroundAudioTracks);
        Assert.Empty(current.RoleAudioList);
        Assert.Empty(current.ExtraPoints);
    }

    [Fact]
    public void Bangumi_RoleTitleFallsBackToAudioIdAndEdition( )
    {
        var current = Parser.BuildAppParsedResult(PlayViewReplyFixtures.Bangumi( ), true, Aid, Cid);

        var jp = current.RoleAudioList[1];
        Assert.Equal("1002", jp.title);
        Assert.Equal("日语原声", jp.personName);
    }

    [Fact]
    public void Ugc_VideoSizeIsNoLongerDiscarded( )
    {
        var current = Parser.BuildAppParsedResult(PlayViewReplyFixtures.Ugc( ), false, Aid, Cid);

        Assert.Equal([754_000_000d, 377_000_000d], current.VideoTracks.Select(v => v.size));
    }
}
