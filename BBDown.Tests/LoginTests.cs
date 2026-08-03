using System;
using System.Text.Json;

namespace BBDown.Tests;

public class LoginTests
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

    private static string WebPoll(int dataCode, string url = "", int outerCode = 0)
    {
        return """
        {"code":OUTER,"message":"0","ttl":1,
         "data":{"url":"URL","refresh_token":"rt","timestamp":1769618093579,"code":INNER,"message":""}}
        """
                .Replace("OUTER", outerCode.ToString( ))
                .Replace("INNER", dataCode.ToString( ))
                .Replace("URL", url);
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

    [Fact]
    public void InterpretWeb_SuccessCode_ReturnsCrossDomainUrl( )
    {
        var (state, data) = Login.InterpretWeb(Parse(WebPoll(0, "https://x/y?SESSDATA=a")));
        Assert.Equal(Login.QrState.Success, state);
        Assert.Equal("https://x/y?SESSDATA=a", data);
    }

    [Theory]
    [InlineData(86038, Login.QrState.Expired)]
    [InlineData(86090, Login.QrState.WaitingConfirm)]
    [InlineData(86101, Login.QrState.WaitingScan)]
    public void InterpretWeb_MapsDocumentedCodes(int code, Login.QrState expected)
    {
        var (state, data) = Login.InterpretWeb(Parse(WebPoll(code)));
        Assert.Equal(expected, state);
        Assert.Null(data);
    }

    [Fact]
    public void InterpretWeb_UnknownDataCode_ThrowsInsteadOfReportingSuccess( )
    {
        Assert.Throws<InvalidOperationException>(( ) => Login.InterpretWeb(Parse(WebPoll(86083))));
    }

    [Fact]
    public void InterpretWeb_NonZeroOuterCode_Throws( )
    {
        Assert.Throws<InvalidOperationException>(( ) => Login.InterpretWeb(Parse(WebPoll(0, outerCode: -400))));
    }

    [Fact]
    public void BuildWebCookie_KeepsOnlyRealCookies( )
    {
        const string url = "https://passport.biligame.com/x/passport-login/web/crossDomain"
            + "?DedeUserID=1&DedeUserID__ckMd5=md5&Expires=1234567890&SESSDATA=sess&bili_jct=csrf"
            + "&gourl=https%3A%2F%2Fwww.bilibili.com&first_domain=.bilibili.com";
        Assert.Equal("DedeUserID=1;DedeUserID__ckMd5=md5;SESSDATA=sess;bili_jct=csrf", Login.BuildWebCookie(url));
    }

    [Fact]
    public void BuildWebCookie_EscapesComma( )
    {
        Assert.Equal("SESSDATA=a%2Cb", Login.BuildWebCookie("https://x/y?SESSDATA=a,b"));
    }

    [Fact]
    public void BuildWebCookie_PreservesExistingPercentEncoding( )
    {
        Assert.Equal("SESSDATA=a%2Cb", Login.BuildWebCookie("https://x/y?SESSDATA=a%2Cb"));
    }

    [Fact]
    public void BuildWebCookie_ThrowsWhenNoCookiePresent( )
    {
        Assert.Throws<InvalidOperationException>(( ) => Login.BuildWebCookie("https://x/y?gourl=z"));
    }
}
