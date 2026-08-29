using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace BBDown.Core.Auth;

public static partial class Login
{
    // appkey 与签名密钥必须配对；手机端登录使用粉版 appkey 时须传入对应密钥
    public static string GetSign(string parms, string secret)
    {
        var toEncode = parms + secret;
        return string.Concat(MD5.HashData(Encoding.UTF8.GetBytes(toEncode)).Select(i => i.ToString("x2")));
    }

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

    // 手写拼接，避免 System.Web.HttpUtility（AOT 裁剪告警且类型不可静态分析）
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
