using System;

using BBDown.Core.Auth;

namespace BBDown.Core.Tests;

public class LoginStatusTests
{
    private static readonly AccountInfo Member = new(true, "测试用户", 6, true, "大会员");
    private static readonly AccountInfo Guest = new(true, "路人", 2, false, "");
    private static readonly AccountInfo None = new(false, "", 0, false, "");

    private static LoginStatus Status(string channel, bool saved, bool? verified, long? issueTs = null, bool refreshPending = false)
    {
        return new LoginStatus(channel, saved, verified, verified == true ? Member : None, issueTs, refreshPending);
    }

    // 通道列与状态列按 cell 宽度补齐（CJK 占 2 cell），三行详情须起始于同一列
    [Theory]
    [InlineData("WEB", "WEB 未登录    本地无 Cookie")]
    [InlineData("TV", "TV  未登录    本地无 access_token")]
    [InlineData("APP", "APP 未登录    本地无 access_token")]
    public void Format_AlignsDetailColumnAcrossChannels(string channel, string expected)
    {
        Assert.Equal(expected, Login.Format(Status(channel, false, null)));
    }

    [Fact]
    public void Format_ProbeFailed_DoesNotClaimInvalidCredential( )
    {
        Assert.Equal("WEB 探测失败  无法连接服务端", Login.Format(Status("WEB", true, null)));
    }

    [Fact]
    public void Format_RejectedCredential_SaysServerRefusedIt( )
    {
        Assert.Equal("WEB 凭据无效  本地 Cookie 存在，服务端未认可", Login.Format(Status("WEB", true, false)));
        Assert.Equal("TV  凭据无效  本地 access_token 存在，服务端未认可", Login.Format(Status("TV", true, false)));
    }

    [Fact]
    public void Format_LoggedIn_ShowsNameLevelAndVip( )
    {
        Assert.Equal("WEB 已登录    测试用户（LV6 · 大会员）", Login.Format(Status("WEB", true, true)));
    }

    [Fact]
    public void Format_LoggedIn_OmitsVipWhenNotMember( )
    {
        Assert.Equal("WEB 已登录    路人（LV2）", Login.Format(new LoginStatus("WEB", true, true, Guest, null)));
    }

    // 签发时间只在有值时出现，且换算到本地时间后按固定格式呈现
    [Fact]
    public void Format_LoggedIn_ShowsIssueTimeOnlyWhenRecorded( )
    {
        const long ts = 1700000000;
        var issued = DateTimeOffset.FromUnixTimeSeconds(ts).ToLocalTime( ).ToString("yyyy-MM-dd HH:mm");
        Assert.Equal($"WEB 已登录    测试用户（LV6 · 大会员）    凭据签发 {issued}", Login.Format(Status("WEB", true, true, ts)));
        Assert.Equal("WEB 已登录    测试用户（LV6 · 大会员）", Login.Format(Status("WEB", true, true, null)));
        Assert.Equal("WEB 已登录    测试用户（LV6 · 大会员）", Login.Format(Status("WEB", true, true, 0)));
    }

    [Fact]
    public void Format_RefreshPending_AppendsHintOnlyWhenFlagged( )
    {
        Assert.Equal("WEB 已登录    测试用户（LV6 · 大会员） · Cookie 需要续期", Login.Format(Status("WEB", true, true, null, true)));
        Assert.DoesNotContain("需要续期", Login.Format(Status("WEB", true, true)));
    }

    [Fact]
    public void Format_DoesNotLeakCredentialValues( )
    {
        var text = Login.Format(Status("WEB", true, false));
        Assert.DoesNotContain("SESSDATA", text);
        Assert.DoesNotContain("bili_jct", text);
        Assert.DoesNotContain("access_token=", text);
    }

    [Fact]
    public void ExitCode_AnyChannelVerified_ReturnsZero( )
    {
        Assert.Equal(0, Login.ExitCode([Status("WEB", false, null), Status("TV", false, null), Status("APP", true, true)]));
    }

    [Fact]
    public void ExitCode_NoneVerified_ReturnsOne( )
    {
        Assert.Equal(1, Login.ExitCode([Status("WEB", false, null), Status("TV", true, false), Status("APP", true, null)]));
    }

    [Fact]
    public void ExitCode_Empty_ReturnsOne( )
    {
        Assert.Equal(1, Login.ExitCode([]));
    }
}
