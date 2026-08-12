using System;
using System.Security.Cryptography;

namespace BBDown.DRM.Tests;

// 测试构造 CKC 时使用固定 IV（协议场景），CA5401 不适用
#pragma warning disable CA5401
public class CkcParserTests
{
    // 构造合法 CKC：header12 + time4 + iv16 + len4 + AES-CBC(会话密钥, IV) 加密的 data
    // data 末 16 字节为内容密钥（密文任意，解密逻辑只取末 16 字节）
    private static byte[] BuildCkc(byte[] sessionKey, byte[] contentKey)
    {
        var data = new byte[48];
        RandomNumberGenerator.Fill(data);
        Array.Copy(contentKey, 0, data, data.Length - 16, 16);

        using var aes = Aes.Create( );
        aes.Key = sessionKey;
        aes.IV = new byte[16];
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.None;
        using var encryptor = aes.CreateEncryptor( );
        var encrypted = encryptor.TransformFinalBlock(data, 0, data.Length);

        var ckc = new byte[12 + 4 + 16 + 4 + encrypted.Length];
        Array.Fill(ckc, (byte)0xAB, 0, 12); // header
        ckc[16] = 1; // iv[0] 非零避免与全零混淆（IV 全零也是合法值，这里仅构造）
        ckc[32] = (byte)(encrypted.Length >> 24);
        ckc[33] = (byte)(encrypted.Length >> 16);
        ckc[34] = (byte)(encrypted.Length >> 8);
        ckc[35] = (byte)encrypted.Length;
        Array.Copy(encrypted, 0, ckc, 36, encrypted.Length);
        return ckc;
    }

    [Fact]
    public void ParseKey_ExtractsLast16BytesAsKey( )
    {
        var sessionKey = Convert.FromHexString("00112233445566778899aabbccddeeff");
        var contentKey = Convert.FromHexString("d8f66b93db284984b4e7fc50d71278ff");

        var key = CkcParser.ParseKey(BuildCkc(sessionKey, contentKey), sessionKey);

        Assert.Equal(contentKey, key);
    }

    [Fact]
    public void ParseKey_WrongSessionKey_ReturnsGarbageButNotNull( )
    {
        // CBC 解密不校验 key 正确性，密钥错误时返回的是解密乱码而非 null；
        // 调用方（BiliDrmLicense）不校验 key 真伪，服务器端已通过 SPC 保证会话密钥正确
        var sessionKey = Convert.FromHexString("00112233445566778899aabbccddeeff");
        var wrongKey = Convert.FromHexString("ffeeddccbbaa99887766554433221100");

        var key = CkcParser.ParseKey(BuildCkc(sessionKey, new byte[16]), wrongKey);

        Assert.NotNull(key);
        Assert.Equal(16, key.Length);
    }

    [Fact]
    public void ParseKey_TooShort_ReturnsNull( )
    {
        Assert.Null(CkcParser.ParseKey([1, 2, 3], new byte[16]));
    }

    [Fact]
    public void ParseKey_BadLength_ReturnsNull( )
    {
        var ckc = new byte[64];
        ckc[32] = 0xFF; // 声称 0xFF000000 字节长度，远超实际
        ckc[33] = 0x00;
        ckc[34] = 0x00;
        ckc[35] = 0x10;

        Assert.Null(CkcParser.ParseKey(ckc, new byte[16]));
    }
}
