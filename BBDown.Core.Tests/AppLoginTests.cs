using System;
using System.Text.Json;

namespace BBDown.Core.Tests;

public class AppLoginTests
{
    private static JsonElement Parse(string json)
    {
        return JsonDocument.Parse(json).RootElement.Clone( );
    }

    private static string TvPoll(string code, string data = "null")
    {
        return """{"code":CODE,"message":"0","ttl":1,"data":DATA}"""
                .Replace("CODE", code)
                .Replace("DATA", data);
    }

    [Fact]
    public void InterpretTv_NumericSuccessCode_ReturnsAccessToken( )
    {
        var root = Parse(TvPoll("0", """{"mid":10086,"access_token":"tok","refresh_token":"rt","expires_in":15552000}"""));
        Assert.Equal((Login.QrState.Success, "tok"), Login.InterpretTv(root));
    }

    [Theory]
    [InlineData("86038", Login.QrState.Expired)]
    [InlineData("86039", Login.QrState.WaitingScan)]
    [InlineData("86090", Login.QrState.WaitingConfirm)]
    public void InterpretTv_MapsDocumentedCodes(string code, Login.QrState expected)
    {
        var (state, data) = Login.InterpretTv(Parse(TvPoll(code)));
        Assert.Equal(expected, state);
        Assert.Null(data);
    }

    [Theory]
    [InlineData("-3")]
    [InlineData("-400")]
    [InlineData("-404")]
    public void InterpretTv_UnknownCode_Throws(string code)
    {
        Assert.Throws<InvalidOperationException>(( ) => Login.InterpretTv(Parse(TvPoll(code))));
    }
}
