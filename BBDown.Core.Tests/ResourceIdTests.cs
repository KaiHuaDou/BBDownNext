using BBDown.Core;

namespace BBDown.Core.Tests;

public class ResourceIdTests
{
    // ToString 保持旧字符串格式：serve API 的 DownloadTask.Aid 字段依赖此契约，不可随意变更
    public static TheoryData<ResourceId, string> ToStringCases => new( )
    {
        { new ResourceId.Av(123456), "123456" },
        { new ResourceId.Ep(123), "ep:123" },
        { new ResourceId.Season(2539), "ep:ss2539" },
        { new ResourceId.CheeseEp(790), "cheese:790" },
        { new ResourceId.CheeseSeason(61), "cheese:ss61" },
        { new ResourceId.Fav(12345, 3), "favId:12345:3" },
        { new ResourceId.MediaList(1560264), "listBizId:1560264" },
        { new ResourceId.Series(1560264), "seriesBizId:1560264" },
        { new ResourceId.Space(402787936), "spaceMid:402787936" },
        { new ResourceId.WatchLater( ), "watchLater:" },
    };

    [Theory]
    [MemberData(nameof(ToStringCases))]
    public void ToString_KeepsLegacyFormat(ResourceId id, string expected)
    {
        Assert.Equal(expected, id.ToString( ));
    }
}
