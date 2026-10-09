using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Text;

namespace BBDown.Core.Auth;

public static partial class Login
{
    public static string GetTimeStamp(bool bflag)
    {
        var ts = DateTimeOffset.Now;
        return (bflag ? ts.ToUnixTimeSeconds( ) : ts.ToUnixTimeMilliseconds( )).ToString( );
    }

    // https://stackoverflow.com/questions/1344221/how-can-i-generate-random-alphanumeric-strings
    public static string GetRandomString(int length)
    {
        const string Chars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz_0123456789";
        return new string([.. Enumerable.Repeat(Chars, length).Select(s => s[Random.Shared.Next(s.Length)])]);
    }

    // 手写拼接：走 encodeURIComponent 语义而非 form-urlencoded。
    // HttpUtility.UrlEncode 的规则不同（空格编成 +、十六进制小写、* 与 ~ 的处理相反），
    // System.Net.WebUtility.UrlEncode 同样如此，故不能替换
    public static string ToQueryString(NameValueCollection nameValueCollection)
    {
        var builder = new StringBuilder( );
        foreach (var key in nameValueCollection.AllKeys)
        {
            if (builder.Length > 0)
            {
                builder.Append('&');
            }

            builder.Append(Uri.EscapeDataString(key!)).Append('=').Append(Uri.EscapeDataString(nameValueCollection[key]!));
        }

        return builder.ToString( );
    }

    public static Dictionary<string, string> ToDictionary(this NameValueCollection nameValueCollection)
    {
        Dictionary<string, string> dict = [];
        foreach (var key in nameValueCollection.AllKeys)
        {
            dict[key!] = nameValueCollection[key]!;
        }

        return dict;
    }
}
