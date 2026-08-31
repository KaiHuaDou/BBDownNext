using System;
using System.Security.Cryptography;
using System.Text;

namespace BBDown.DRM;

// biliDRM（clearkey）SPC 构造：B 站自研 DRM 的许可证请求体。
// 240 字节二进制经 base64 后随 {"spc": ...} 提交。布局（字节偏移）经逆向与实测确认：
//   0  "bilibili" 魔数（8）
//   8  固定头 00000001 + 8 字节零（12）
//   20 时间戳 BE（4）
//   24 随机 IV（16）
//   40 RSA-OAEP(SHA1) 加密的会话密钥（128）
//   168 公钥 PEM 全文 SHA1（20）
//   188 KID 上下文长度 = 48（4）
//   192 AES-CBC(会话密钥，IV) 加密的 KID 上下文（48）
// KID 上下文明文 = 固定 salt（16）+ AES-ECB(会话密钥，kid 前 16 字节) + kid 后 16 字节明文。
// 协议强制：SHA1 摘要、OAEP-SHA1 加密、ECB 加密 kid 前段、自定义 IV 的 CBC（均为 biliDRM 协议要求）
#pragma warning disable CA5350, CA5358, CA5401
internal static class SpcBuilder
{
    private static readonly byte[] Header = [0x00, 0x00, 0x00, 0x01, 0, 0, 0, 0, 0, 0, 0, 0];
    private static readonly byte[] Salt = [0x1b, 0xf7, 0xf5, 0x3f, 0x5d, 0x5d, 0x5a, 0x1f, 0x00, 0x00, 0x00, 0x20, 0x00, 0x00, 0x00, 0x20];

    /// <summary>
    /// 构造 SPC 的 base64 文本。会话密钥/IV 由调用方生成（解密 CKC 时仍需会话密钥，不能在此丢失）。
    /// kid 为 32 字节：bilidrm_uri 的 KID 是 32 字符 hex 字符串，其 ASCII 字节即 SDK 的 kid 输入。
    /// </summary>
    public static string Build(byte[] kid, byte[] sessionKey, byte[] iv, string publicKeyPem)
    {
        using var rsa = RSA.Create( );
        rsa.ImportFromPem(publicKeyPem);
        // 实测确认 OAEP-SHA1：其余 padding 服务器返回 get assetId failed
        var encryptedKey = rsa.Encrypt(sessionKey, RSAEncryptionPadding.OaepSHA1);

        var sha1 = SHA1.HashData(Encoding.ASCII.GetBytes(publicKeyPem));
        var context = BuildKidContext(kid, sessionKey, iv);

        var raw = new byte[8 + Header.Length + 4 + iv.Length + encryptedKey.Length + sha1.Length + 4 + context.Length];
        var pos = 0;
        Write(raw, ref pos, "bilibili"u8);
        Write(raw, ref pos, Header);
        Write(raw, ref pos, (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds( ));
        Write(raw, ref pos, iv);
        Write(raw, ref pos, encryptedKey);
        Write(raw, ref pos, sha1);
        Write(raw, ref pos, (uint)context.Length);
        Write(raw, ref pos, context);
        return Convert.ToBase64String(raw);
    }

    // salt + ECB(会话密钥，kid 前 16 字节) + kid 后 16 字节，整体按 CBC(会话密钥，IV) 加密
    private static byte[] BuildKidContext(byte[] kid, byte[] sessionKey, byte[] iv)
    {
        var plain = new byte[Salt.Length + 16 + 16];
        Buffer.BlockCopy(Salt, 0, plain, 0, Salt.Length);
        Buffer.BlockCopy(AesEcb(sessionKey, kid[..16]), 0, plain, Salt.Length, 16);
        Buffer.BlockCopy(kid, 16, plain, Salt.Length + 16, 16);
        using var aes = Aes.Create( );
        aes.Key = sessionKey;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.None;
        using var encryptor = aes.CreateEncryptor( );
        return encryptor.TransformFinalBlock(plain, 0, plain.Length);
    }

    private static byte[] AesEcb(byte[] key, byte[] block)
    {
        using var aes = Aes.Create( );
        aes.Key = key;
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;
        using var encryptor = aes.CreateEncryptor( );
        return encryptor.TransformFinalBlock(block, 0, block.Length);
    }

    private static void Write(byte[] buffer, ref int pos, ReadOnlySpan<byte> data)
    {
        data.CopyTo(buffer.AsSpan(pos));
        pos += data.Length;
    }

    private static void Write(byte[] buffer, ref int pos, uint value)
    {
        buffer[pos] = (byte)(value >> 24);
        buffer[pos + 1] = (byte)(value >> 16);
        buffer[pos + 2] = (byte)(value >> 8);
        buffer[pos + 3] = (byte)value;
        pos += 4;
    }
}
#pragma warning restore CA5350, CA5358, CA5401
