using System;
using System.Collections.Specialized;
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

    // ── 纯静态辅助方法 ──────────────────────────────────────────────────────

    [Fact]
    public void GetSign_AppendsTvSecretAndMd5sLowerHex( )
    {
        // 无密钥重载默认用 TV 密钥 (P0-4)，与 Login.GetSign 实现逐字节一致
        Assert.Equal("b87ec59fee04ef877bbee79f1b0e55ff", Login.GetSign("x"));
        Assert.Equal("5638bbf60cf4ecf5a72ebcfe2eb57b84", Login.GetSign("foo=1&wts=1"));
    }

    [Theory]
    [InlineData("abc", "secret", "33e7cb694fb6fb2f848af6774d9ff138")]
    [InlineData("a=1", "sec", "66af090a3f7b90241b548ea0371db311")]
    [InlineData("", "secret", "5ebe2294ecd0e0f08eab7690d2a6ee69")]
    public void GetSign_WithExplicitSecret(string parms, string secret, string expected)
    {
        // parms + secret 拼接后做 MD5，输出小写十六进制
        Assert.Equal(expected, Login.GetSign(parms, secret));
    }

    [Fact]
    public void GetTimeStamp_SecondsVsMilliseconds( )
    {
        var secs = long.Parse(Login.GetTimeStamp(true));
        var ms = long.Parse(Login.GetTimeStamp(false));

        // 秒级：与当前时间差在 5 秒内
        Assert.InRange(secs, DateTimeOffset.Now.ToUnixTimeSeconds( ) - 5, DateTimeOffset.Now.ToUnixTimeSeconds( ) + 5);
        // 毫秒级：与当前时间差在 5 秒内
        Assert.InRange(ms, DateTimeOffset.Now.ToUnixTimeMilliseconds( ) - 5000, DateTimeOffset.Now.ToUnixTimeMilliseconds( ) + 5000);
        // 毫秒级至少是秒级的 1000 倍
        Assert.True(ms >= secs * 1000);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(20)]
    [InlineData(64)]
    public void GetRandomString_HasRequestedLengthAndAllowedCharset(int length)
    {
        // 去掉了易混字符 I/O/1/l/0，避免人工抄写 token 时出错
        const string Allowed = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz_0123456789";
        var s = Login.GetRandomString(length);
        Assert.Equal(length, s.Length);
        Assert.All(s, c => Assert.Contains(c, Allowed));
    }

    [Fact]
    public void ToQueryString_PreservesInsertionOrderAndJoins( )
    {
        // HttpUtility.ParseQueryString 不排序，原样按插入顺序拼回 key=value&...
        var nvc = new NameValueCollection { ["b"] = "2", ["a"] = "1" };
        Assert.Equal("b=2&a=1", Login.ToQueryString(nvc));
    }

    [Fact]
    public void ToQueryString_EmptyCollection_ReturnsEmpty( )
    {
        Assert.Equal("", Login.ToQueryString(new NameValueCollection( )));
    }

    [Fact]
    public void ToDictionary_FromNameValueCollection( )
    {
        var nvc = new NameValueCollection { ["appkey"] = "4409e2ce8ffd12b8", ["ts"] = "123" };
        var dict = nvc.ToDictionary( );
        Assert.Equal(2, dict.Count);
        Assert.Equal("4409e2ce8ffd12b8", dict["appkey"]);
        Assert.Equal("123", dict["ts"]);
    }

    // MaskSecret 是 private，借反射覆盖：日志里只露凭据首尾，避免明文泄露 (P0-3)
    private static readonly System.Reflection.MethodInfo MaskSecretMethod =
        typeof(Login).GetMethod("MaskSecret", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

    private static string MaskSecret(string? s)
    {
        object?[] args = new object?[] { s };
        return (string) MaskSecretMethod.Invoke(null, args)!;
    }

    [Theory]
    [InlineData(null, "***")]
    [InlineData("", "***")]
    [InlineData("abc", "***")]
    [InlineData("12345678", "***")]            // 长度恰好 8 → 仍遮罩
    [InlineData("123456789", "1234****6789")]  // 长度 9 → 首尾各 4 位
    [InlineData("abcdefghijklmnop", "abcd****mnop")]
    public void MaskSecret_ShowsOnlyFirstAndLastFour(string? input, string expected)
    {
        Assert.Equal(expected, MaskSecret(input));
    }
}
