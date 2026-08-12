using System;
using System.Security.Cryptography;

namespace BBDown.DRM;

// biliDRM（clearkey）CKC 解析：license 服务器对 SPC 的响应体。
// 布局（字节偏移）经逆向确认：
//   0  固定头（12）
//   12 时间戳 BE（4）
//   16 IV（16）
//   32 数据长度（4）
//   36 数据：AES-CBC(会话密钥, IV) 解密后，末 16 字节即内容密钥
internal static class CkcParser
{
    public static byte[]? ParseKey(byte[] ckc, byte[] sessionKey)
    {
        if (ckc.Length < 36)
        {
            return null;
        }

        var iv = new byte[16];
        Buffer.BlockCopy(ckc, 16, iv, 0, 16);
        var dataLength = (int)ReadU32Be(ckc, 32);
        if (dataLength <= 0 || 36 + dataLength > ckc.Length)
        {
            return null;
        }

        using var aes = Aes.Create( );
        aes.Key = sessionKey;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.None;
        using var decryptor = aes.CreateDecryptor( );
        var plain = decryptor.TransformFinalBlock(ckc, 36, dataLength);

        // 内容密钥为明文末 16 字节
        if (plain.Length < 16)
        {
            return null;
        }

        var key = new byte[16];
        Buffer.BlockCopy(plain, plain.Length - 16, key, 0, 16);
        return key;
    }

    private static uint ReadU32Be(byte[] buffer, int offset)
    {
        return ((uint)buffer[offset] << 24) | ((uint)buffer[offset + 1] << 16) | ((uint)buffer[offset + 2] << 8) | buffer[offset + 3];
    }
}
