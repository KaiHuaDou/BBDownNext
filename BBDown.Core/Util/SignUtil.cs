using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace BBDown.Core.Util;

/// <summary>
/// B 站请求签名：Web 端 WBI 与 APP/TV 端 appkey 两套算法，以及两者共用的时间戳。
/// </summary>
public static class SignUtil
{
    // CA5351: MD5 由 B 站 wbi 签名协议规定，哈希值必须与服务端保持一致，不能替换为 SHA256
    // 算法见 bilibili-API-collect/docs/misc/sign/wbi.md：把含 wts 的参数按 key 升序排序后，对值做
    // encodeURIComponent 风格编码（并过滤 !'()*），末尾直接拼接 mixinKey 取 MD5 得 w_rid，再追加回原始 query。
    // 当前 playurl 参数均为数字/固定字面量，编码为恒等变换；排序是关键修正点（旧实现按书写序拼接，与服务端不一致）。
    public static string WbiSign(string api, AppConfig cfg)
    {
        if (cfg.Wbi.Length == 0)
        {
            return api;
        }

        // 先剔除任何已存在的 w_rid：既用于计算 canonical，也避免重签名时把旧 w_rid 残留在输出里
        var withoutWrid = string.Join("&",
            api.Split('&')
                .Select(part => part.Split('=', 2))
                .Where(kv => kv.Length == 2 && kv[0] != "w_rid")
                .Select(kv => $"{kv[0]}={kv[1]}"));

        var canonical = string.Join("&",
            withoutWrid.Split('&')
                .Select(part => part.Split('=', 2))
                .Where(kv => kv.Length == 2)
                .Select(kv => (Key: kv[0], Value: WbiEncodeValue(kv[1])))
                .OrderBy(p => p.Key, StringComparer.Ordinal)
                .Select(p => $"{p.Key}={p.Value}"))
            + cfg.Wbi;

        return $"{withoutWrid}&w_rid={Md5Hex(canonical)}";
    }

    // 与浏览器 encodeURIComponent 一致：保留 A-Za-z0-9-_.~，过滤 wbi.md 要求的 !'()*，其余按 UTF-8 字节大写十六进制转义（空格 -> %20）。
    private static string WbiEncodeValue(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (ch is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '_' or '.' or '~')
            {
                sb.Append(ch);
            }
            else if (ch is '!' or '\'' or '(' or ')' or '*')
            {
                // wbi.md 要求过滤 !'()*
            }
            else
            {
                foreach (var b in Encoding.UTF8.GetBytes([ch]))
                {
                    sb.Append('%').Append(b.ToString("X2"));
                }
            }
        }

        return sb.ToString( );
    }

    /// <summary>
    /// 以键值对集合构建 WBI 签名 query：剔除旧 w_rid、按 key 升序、对每个值做 encodeURIComponent 风格编码，
    /// 缺失 wts 时自动补当前时间戳，最后追加 w_rid。reply/wbi/main 的 pagination_str 是一段 JSON，
    /// 必须经此编码后参与签名，才能保证 canonical 与线上 URL 同源（<see cref="WbiEncodeValue"/> 仅过滤 !'()*）。
    /// </summary>
    public static string WbiSignedQuery(IEnumerable<KeyValuePair<string, string>> parameters, AppConfig cfg)
    {
        var pairs = parameters.Where(p => p.Key != "w_rid").ToList( );
        if (!pairs.Exists(p => p.Key == "wts"))
        {
            pairs.Add(new KeyValuePair<string, string>("wts", UnixTimestamp( )));
        }

        var query = string.Join("&",
            pairs
                .Select(p => (p.Key, Value: WbiEncodeValue(p.Value)))
                .OrderBy(p => p.Key, StringComparer.Ordinal)
                .Select(p => $"{p.Key}={p.Value}"));

        return cfg.Wbi.Length == 0 ? query : $"{query}&w_rid={Md5Hex(query + cfg.Wbi)}";
    }

    /// <summary>
    /// 补上当前时间戳再签名。wts 是 WBI 的必填参数且参与排序，缺失时服务端直接判签名无效。
    /// </summary>
    public static string WbiSignNow(string query, AppConfig cfg)
    {
        return WbiSign($"{query}&wts={UnixTimestamp( )}", cfg);
    }

    /// <summary>
    /// appkey 体系签名：query 末尾拼接与 appkey 配对的密钥后取 MD5。密钥必须与 query 里的 appkey 对应。
    /// </summary>
    public static string AppSign(string query, string secret)
    {
        return Md5Hex(query + secret);
    }

    public static string UnixTimestamp(bool seconds = true)
    {
        var ts = DateTimeOffset.Now;
        return (seconds ? ts.ToUnixTimeSeconds( ) : ts.ToUnixTimeMilliseconds( )).ToString(CultureInfo.InvariantCulture);
    }

    // CA5351: MD5 由 B 站签名协议规定，哈希值必须与服务端保持一致，不能替换为 SHA256
    private static string Md5Hex(string value)
    {
        return string.Concat(MD5.HashData(Encoding.UTF8.GetBytes(value)).Select(b => b.ToString("x2")));
    }
}
