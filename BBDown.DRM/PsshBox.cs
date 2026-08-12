using System;
using System.Collections.Generic;

using BBDown.DRM.Proto;

using Google.Protobuf;

namespace BBDown.DRM;

// 标准 PSSH box 解析：size/type 头 + version+flags + system_id +（v1 起 KID 列表）+ data 载荷。
// 返回载荷与 KID 列表，KID 缺失时从载荷内的 WidevineCencHeader 补取；任何格式异常返回空。
internal static class PsshBox
{
    private static readonly byte[] WidevineSystemId = [0xed, 0xef, 0x8b, 0xa9, 0x79, 0xd6, 0x4a, 0xce, 0xa3, 0xc8, 0x27, 0xdc, 0xd5, 0x1d, 0x21, 0xed];

    public static (byte[] Payload, List<byte[]> KeyIds) Parse(string psshBase64)
    {
        var keyIds = new List<byte[]>( );
        byte[] payload = [];
        byte[] raw;
        try
        {
            raw = Convert.FromBase64String(psshBase64);
        }
        catch (FormatException)
        {
            return (payload, keyIds);
        }

        if (raw.Length < 32)
        {
            return (payload, keyIds);
        }

        var pos = 12; // 跳过 size + type + version/flags
        var version = raw[8];
        if (!raw.AsSpan(pos, 16).SequenceEqual(WidevineSystemId))
        {
            return (payload, keyIds);
        }

        pos += 16;
        if (version >= 1)
        {
            if (pos + 4 > raw.Length)
            {
                return (payload, keyIds);
            }

            var count = (int)ReadU32Be(raw, pos);
            pos += 4;
            for (var i = 0; i < count && pos + 16 <= raw.Length; i++)
            {
                var kid = new byte[16];
                Buffer.BlockCopy(raw, pos, kid, 0, 16);
                keyIds.Add(kid);
                pos += 16;
            }
        }

        if (pos + 4 > raw.Length)
        {
            return (payload, keyIds);
        }

        var dataSize = (int)ReadU32Be(raw, pos);
        pos += 4;
        if (dataSize <= 0 || dataSize > 4096 || pos + dataSize > raw.Length)
        {
            return (payload, keyIds);
        }

        payload = new byte[dataSize];
        Buffer.BlockCopy(raw, pos, payload, 0, dataSize);
        if (keyIds.Count == 0)
        {
            // v0 box 不含 KID 列表，从载荷内的 WidevineCencHeader 补取
            try
            {
                var header = WidevineCencHeader.Parser.ParseFrom(payload);
                foreach (var kid in header.KeyIds)
                {
                    keyIds.Add(kid.ToByteArray( ));
                }
            }
            catch (InvalidProtocolBufferException)
            {
                // 载荷不可解析时保持无 KID，由调用方判定取钥失败
            }
        }

        return (payload, keyIds);
    }

    private static uint ReadU32Be(byte[] buffer, int offset)
    {
        return ((uint)buffer[offset] << 24) | ((uint)buffer[offset + 1] << 16) | ((uint)buffer[offset + 2] << 8) | buffer[offset + 3];
    }
}
