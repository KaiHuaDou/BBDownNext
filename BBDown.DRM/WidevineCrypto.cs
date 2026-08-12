using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace BBDown.DRM;

// Widevine 协议专用密码学原语：key 派生、AES-CMAC、AES 加解密与 PKCS7。
// context 与 counter 的拼接规则是协议标准（与 pywidevine 一致），改动会使 license 校验失败。
internal static class WidevineCrypto
{
    /// <summary>AES-CMAC（RFC 4493），用于会话密钥派生与签名密钥计算。</summary>
    public static byte[] AesCmac(byte[] key, byte[] message)
    {
        var zero = new byte[16];
        var subKey1 = SubKey(AesEcb(zero, key, encrypt: true));
        var subKey2 = SubKey(subKey1);

        // 最后一块：完整则异或 K1，否则补 0x80 后异或 K2
        var blockCount = (message.Length + 15) / 16;
        var lastBlock = new byte[16];
        if (message.Length > 0 && message.Length % 16 == 0)
        {
            Buffer.BlockCopy(message, message.Length - 16, lastBlock, 0, 16);
            Xor(lastBlock, subKey1);
        }
        else
        {
            Buffer.BlockCopy(message, message.Length - message.Length % 16, lastBlock, 0, message.Length % 16);
            lastBlock[message.Length % 16] = 0x80;
            Xor(lastBlock, subKey2);
        }

        var accumulator = new byte[16];
        for (var i = 0; i < blockCount - 1; i++)
        {
            Xor(accumulator, message.AsSpan(i * 16, 16));
            accumulator = AesEcb(accumulator, key, encrypt: true);
        }

        Xor(accumulator, lastBlock);
        return AesEcb(accumulator, key, encrypt: true);
    }

    /// <summary>会话密钥按 counter || context 的 CMAC 结果：enc=1、server mac=1+2、client mac=3+4。</summary>
    public static (byte[] EncKey, byte[] MacKeyServer, byte[] MacKeyClient) DeriveKeys(byte[] sessionKey, byte[] message)
    {
        var (encContext, macContext) = DeriveContext(message);
        var encKey = Derive(sessionKey, encContext, 1);
        var macKeyServer = Concat(Derive(sessionKey, macContext, 1), Derive(sessionKey, macContext, 2));
        var macKeyClient = Concat(Derive(sessionKey, macContext, 3), Derive(sessionKey, macContext, 4));
        return (encKey, macKeyServer, macKeyClient);
    }

    // CA5358：Widevine 协议规定 IV 全零/缺失时按 ECB 解密内容密钥，属协议要求而非可替换的安全缺口
#pragma warning disable CA5358
    public static byte[] AesEcb(byte[] data, byte[] key, bool encrypt)
    {
        using var aes = Aes.Create( );
        aes.Key = key;
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;
        using var transform = encrypt ? aes.CreateEncryptor( ) : aes.CreateDecryptor( );
        return transform.TransformFinalBlock(data, 0, data.Length);
    }
#pragma warning restore CA5358

    public static byte[] AesCbcDecrypt(byte[] data, byte[] key, byte[] iv)
    {
        using var aes = Aes.Create( );
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.None;
        using var transform = aes.CreateDecryptor( );
        return transform.TransformFinalBlock(data, 0, data.Length);
    }

    public static byte[] Pkcs7Unpad(byte[] data)
    {
        var pad = data[^1];
        if (pad == 0 || pad > 16 || pad > data.Length)
        {
            throw new InvalidDataException("非法的 PKCS7 填充");
        }

        for (var i = data.Length - pad; i < data.Length; i++)
        {
            if (data[i] != pad)
            {
                throw new InvalidDataException("非法的 PKCS7 填充");
            }
        }

        return data[..(data.Length - pad)];
    }

    // ENCRYPTION\0 + message + 128bit 大端 / AUTHENTICATION\0 + message + 512bit 大端
    private static (byte[] EncContext, byte[] MacContext) DeriveContext(byte[] message)
    {
        var encContext = Concat(Encoding.ASCII.GetBytes("ENCRYPTION\0"), message);
        var macContext = Concat(Encoding.ASCII.GetBytes("AUTHENTICATION\0"), message);
        encContext = Concat(encContext, [0, 0, 0, 0x80]);
        macContext = Concat(macContext, [0, 0, 2, 0]);
        return (encContext, macContext);
    }

    private static byte[] Derive(byte[] sessionKey, byte[] context, int counter)
    {
        var input = new byte[1 + context.Length];
        input[0] = (byte)counter;
        Buffer.BlockCopy(context, 0, input, 1, context.Length);
        return AesCmac(sessionKey, input);
    }

    // K1/K2 生成：左移一位，最高位进位时末尾异或 0x87（GF(2^128) 乘法）
    private static byte[] SubKey(byte[] key)
    {
        var result = new byte[16];
        var carry = 0;
        for (var i = 15; i >= 0; i--)
        {
            result[i] = (byte)((key[i] << 1) | carry);
            carry = key[i] >> 7;
        }

        if (carry != 0)
        {
            result[15] ^= 0x87;
        }

        return result;
    }

    private static void Xor(byte[] target, ReadOnlySpan<byte> source)
    {
        for (var i = 0; i < target.Length; i++)
        {
            target[i] ^= source[i];
        }
    }

    private static byte[] Concat(byte[] first, byte[] second)
    {
        var result = new byte[first.Length + second.Length];
        Buffer.BlockCopy(first, 0, result, 0, first.Length);
        Buffer.BlockCopy(second, 0, result, first.Length, second.Length);
        return result;
    }
}
