using System.Text.Json;

namespace BBDown.Core.Tests;

public class AccountInfoTests
{
    [Fact]
    public void ParseNav_ParsesLoggedInVip( )
    {
        const string json = "{\"data\":{\"isLogin\":true,\"uname\":\"测试用户\",\"level_info\":{\"current_level\":6},\"vip\":{\"vipStatus\":1,\"vipType\":2,\"label\":{\"text\":\"大会员\"}}}}";
        var data = JsonDocument.Parse(json).RootElement.GetProperty("data");
        var info = Account.ParseNav(data);
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
        var info = Account.ParseNav(data);
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
        var info = Account.ParseNav(data);
        Assert.True(info.IsLogin);
        Assert.Equal("路人", info.UserName);
        Assert.False(info.IsVip);
        Assert.Equal("", info.VipLabel);
    }

    [Fact]
    public void ParseMyInfo_ParsesLoggedInVip( )
    {
        const string json = "{\"code\":0,\"data\":{\"name\":\"测试用户\",\"level\":6,\"vip\":{\"status\":1,\"label\":{\"text\":\"大会员\"}}}}";
        var data = JsonDocument.Parse(json).RootElement.GetProperty("data");
        var info = Account.ParseMyInfo(data);
        Assert.True(info.IsLogin);
        Assert.Equal("测试用户", info.UserName);
        Assert.Equal(6, info.Level);
        Assert.True(info.IsVip);
        Assert.Equal("大会员", info.VipLabel);
    }

    // myinfo 的字段名与 nav 全不相同（name / level / vip.status），缺失保护须与 ParseNav 同规格
    [Fact]
    public void ParseMyInfo_ToleratesMissingVip( )
    {
        const string json = "{\"code\":0,\"data\":{\"name\":\"路人\",\"level\":3}}";
        var data = JsonDocument.Parse(json).RootElement.GetProperty("data");
        var info = Account.ParseMyInfo(data);
        Assert.Equal("路人", info.UserName);
        Assert.Equal(3, info.Level);
        Assert.False(info.IsVip);
        Assert.Equal("", info.VipLabel);
    }

    // 服务端改字段类型时不得整体抛出：状态查询是尽力而为的旁路，不该因单字段异常丢掉整行输出
    [Fact]
    public void ParseMyInfo_ToleratesUnexpectedFieldTypes( )
    {
        const string json = "{\"code\":0,\"data\":{\"name\":123,\"level\":\"六\",\"vip\":{\"status\":\"1\"}}}";
        var data = JsonDocument.Parse(json).RootElement.GetProperty("data");
        var info = Account.ParseMyInfo(data);
        Assert.Equal(0, info.Level);
        Assert.False(info.IsVip);
    }
}
