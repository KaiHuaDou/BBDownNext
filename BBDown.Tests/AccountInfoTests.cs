using System.Text.Json;
using BBDown;

namespace BBDown.Tests;

public class AccountInfoTests
{
    [Fact]
    public void ParseNav_ParsesLoggedInVip( )
    {
        const string json = "{\"data\":{\"isLogin\":true,\"uname\":\"测试用户\",\"level_info\":{\"current_level\":6},\"vip\":{\"vipStatus\":1,\"vipType\":2,\"label\":{\"text\":\"大会员\"}}}}";
        var data = JsonDocument.Parse(json).RootElement.GetProperty("data");
        var info = Utils.ParseNav(data);
        Assert.True(info.IsLogin);
        Assert.Equal("测试用户", info.UserName);
        Assert.Equal(6, info.Level);
        Assert.True(info.IsVip);
        Assert.Equal("大会员", info.VipLabel);
    }

    [Fact]
    public void ParseNav_ParsesLoggedOutNoVip( )
    {
        const string json = "{\"data\":{\"isLogin\":false,\"uname\":\"\",\"level_info\":{\"current_level\":0},\"vip\":{\"vipStatus\":0,\"vipType\":0,\"label\":{\"text\":\"\"}}}}";
        var data = JsonDocument.Parse(json).RootElement.GetProperty("data");
        var info = Utils.ParseNav(data);
        Assert.False(info.IsLogin);
        Assert.Equal("", info.UserName);
        Assert.Equal(0, info.Level);
        Assert.False(info.IsVip);
        Assert.Equal("", info.VipLabel);
    }

    [Fact]
    public void ParseNav_ToleratesMissingVip( )
    {
        const string json = "{\"data\":{\"isLogin\":true,\"uname\":\"路人\"}}";
        var data = JsonDocument.Parse(json).RootElement.GetProperty("data");
        var info = Utils.ParseNav(data);
        Assert.True(info.IsLogin);
        Assert.Equal("路人", info.UserName);
        Assert.False(info.IsVip);
        Assert.Equal("", info.VipLabel);
    }
}
