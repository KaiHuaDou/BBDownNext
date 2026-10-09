using System;

namespace BBDown.Core.Tests;

public class WorkSetupTests
{
    // Area 会被逐字拼进官方 API 的 query（playurl 的 area= 参数），白名单收在 ResolveConfig，
    // CLI 与 serve 共用；未命中回落空串，避免任意文本注入 query 参数
    [Theory]
    [InlineData("hk", "hk")]
    [InlineData("tw", "tw")]
    [InlineData("th", "th")]
    [InlineData("TW", "tw")]
    [InlineData(" th ", "th")]
    [InlineData("th&fnval=0", "")]
    [InlineData("cn", "")]
    [InlineData("", "")]
    public void ResolveConfig_Area_MatchesWhitelistOrEmpty(string area, string expected)
    {
        var request = new DownloadRequest { Url = "av1", Area = area };
        var config = WorkSetup.ResolveConfig(request, ApiType.Web);
        Assert.Equal(expected, config.Area);
    }

    // host 三兄弟为空串 / 纯空白时回落官方默认，避免拼出 https:///... 抛 UriFormatException
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveConfig_BlankHost_FallsBackToBiliApi(string host)
    {
        var request = new DownloadRequest { Url = "av1", Host = host };
        var config = WorkSetup.ResolveConfig(request, ApiType.Web);
        Assert.Equal(BiliApi.MainHost, config.Host);
    }
}
