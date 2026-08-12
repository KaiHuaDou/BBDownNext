using System;
using System.Collections.Generic;
using System.Text;

namespace BBDown.Core.Util;

//code from: https://github.com/Colerar/abv/blob/main/src/lib.rs
public static class BilibiliBvConverter
{
    private const long XOR_CODE = 23442827791579L;
    private const long MASK_CODE = (1L << 51) - 1;

    private const long MAX_AID = MASK_CODE + 1;
    private const long MIN_AID = 1L;

    private const long BASE = 58L;
    private const byte BV_LEN = 9;

    private static readonly byte[] ALPHABET = Encoding.ASCII.GetBytes("FcwAPNKTMug3GV5Lj7EJnHpWsx4tb8haYeviqBz6rkCy12mUSDQX9RdoZf");

    private static readonly Dictionary<byte, long> REV_ALPHABETA = [];

    static BilibiliBvConverter( )
    {
        for (byte i = 0; i < ALPHABET.Length; i++)
        {
            REV_ALPHABETA[ALPHABET[i]] = i;
        }
    }

    public static string Encode(long avid)
    {
        if (avid < MIN_AID)
        {
            throw new InvalidOperationException($"Av {avid} is smaller than {MIN_AID}");
        }

        if (avid >= MAX_AID)
        {
            throw new InvalidOperationException($"Av {avid} is bigger than {MAX_AID}");
        }

        var bvid = new byte[BV_LEN];
        var tmp = (MAX_AID | avid) ^ XOR_CODE;

        for (byte i = BV_LEN - 1; tmp != 0; i--)
        {
            bvid[i] = ALPHABET[tmp % BASE];
            tmp /= BASE;
        }

        (bvid[0], bvid[6]) = (bvid[6], bvid[0]);
        (bvid[1], bvid[4]) = (bvid[4], bvid[1]);

        return "BV1" + Encoding.ASCII.GetString(bvid);
    }

    public static long Decode(string bvidStr)
    {
        if (bvidStr.Length != BV_LEN)
        {
            throw new InvalidOperationException($"无效的 BV 号：BV1{bvidStr} 长度应为 12 个字符，实际 {bvidStr.Length} 个");
        }

        var bvid = Encoding.ASCII.GetBytes(bvidStr);
        (bvid[0], bvid[6]) = (bvid[6], bvid[0]);
        (bvid[1], bvid[4]) = (bvid[4], bvid[1]);

        long avid = 0;
        foreach (var b in bvid)
        {
            // 字符集校验：避免字典查找直接抛 KeyNotFoundException，给出可读错误
            if (!REV_ALPHABETA.TryGetValue(b, out var value))
            {
                throw new InvalidOperationException($"无效的 BV 字符：{(char) b}（不在 BV 字母表中）");
            }

            avid = avid * BASE + value;
        }

        return (avid & MASK_CODE) ^ XOR_CODE;
    }
}
