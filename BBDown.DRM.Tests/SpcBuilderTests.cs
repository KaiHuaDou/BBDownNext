using System;
using System.Security.Cryptography;

namespace BBDown.DRM.Tests;

// 协议强制 SHA1/ECB（biliDRM 规范），测试验证协议本身
#pragma warning disable CA5350, CA5358
public class SpcBuilderTests
{
    private const string PublicKeyPem =
        """
        -----BEGIN PUBLIC KEY-----
        MIGfMA0GCSqGSIb3DQEBAQUAA4GNADCBiQKBgQDSfEEsrgqk6ioBSTsXPTRmimQv
        Eff3xSXIl9WMUn2hlI8bYNKg7whKXvuLtxp1azqBddCcaw0rIU8r4ypSVh3UcA3Y
        IwxuwmONNCLhVGuGsePgjtA0DCP2cHa2c2DOTMiQXWcPCMm907SMrsj/rKkYYrAr
        CiSvpVOe/jBesgxsowIDAQAB
        -----END PUBLIC KEY-----
        """;

    // 结构向量：SDK 实测样本（固定 kid/sessionKey/IV 由测试自身控制，RSA 段因 OAEP 随机不可比对，
    // 这里验证固定段与加密逻辑的确定性部分：魔数、固定头、时间戳位置、SHA1、KID 上下文）
    [Fact]
    public void Build_StructureMatchesProtocol( )
    {
        var kid = System.Text.Encoding.ASCII.GetBytes("0123456789abcdef0123456789abcdef");
        var sessionKey = new byte[16];
        var iv = new byte[16];
        for (var i = 0; i < 16; i++)
        {
            sessionKey[i] = (byte)(0x40 + i);
            iv[i] = (byte)i;
        }

        var spc = Convert.FromBase64String(SpcBuilder.Build(kid, sessionKey, iv, PublicKeyPem));

        // 240 字节：8 + 12 + 4 + 16 + 128 + 20 + 4 + 48
        Assert.Equal(240, spc.Length);
        Assert.Equal("bilibili", System.Text.Encoding.ASCII.GetString(spc, 0, 8));
        // 固定头 00000001 + 8 字节零
        Assert.Equal([0x00, 0x00, 0x00, 0x01, 0, 0, 0, 0, 0, 0, 0, 0], spc[8..20]);
        // 时间戳为 4 字节 BE（非零）
        var ts = (spc[20] << 24) | (spc[21] << 16) | (spc[22] << 8) | spc[23];
        Assert.True(ts > 1700000000);
        // IV 原样写入
        Assert.Equal(iv, spc[24..40]);
        // 公钥 PEM 全文 SHA1
        Assert.Equal(SHA1.HashData(System.Text.Encoding.ASCII.GetBytes(PublicKeyPem)), spc[168..188]);
        // KID 上下文长度 48
        Assert.Equal(48, (spc[188] << 24) | (spc[189] << 16) | (spc[190] << 8) | spc[191]);
    }

    [Fact]
    public void Build_KidContextDecryptsBackToPlaintext( )
    {
        var kid = System.Text.Encoding.ASCII.GetBytes("0123456789abcdef0123456789abcdef");
        var sessionKey = Convert.FromHexString("00112233445566778899aabbccddeeff");
        var iv = Convert.FromHexString("0102030405060708090a0b0c0d0e0f10");

        var spc = Convert.FromBase64String(SpcBuilder.Build(kid, sessionKey, iv, PublicKeyPem));

        // CBC 解密 KID 上下文，验证 salt + ECB(sessionKey, kid前16) + kid后16
        using var aes = Aes.Create( );
        aes.Key = sessionKey;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.None;
        using var decryptor = aes.CreateDecryptor( );
        var plain = decryptor.TransformFinalBlock(spc, 192, 48);

        Assert.Equal([0x1b, 0xf7, 0xf5, 0x3f, 0x5d, 0x5d, 0x5a, 0x1f, 0x00, 0x00, 0x00, 0x20, 0x00, 0x00, 0x00, 0x20], plain[..16]);
        // ECB 解密 enc_kid 段应还原 kid 前 16 字节
        using var ecb = Aes.Create( );
        ecb.Key = sessionKey;
        ecb.Mode = CipherMode.ECB;
        ecb.Padding = PaddingMode.None;
        using var ecbDecryptor = ecb.CreateDecryptor( );
        Assert.Equal(kid[..16], ecbDecryptor.TransformFinalBlock(plain, 16, 16));
        // kid 后 16 字节明文透传
        Assert.Equal(kid[16..], plain[32..]);
    }

    [Fact]
    public void Build_ProducesValidBase64( )
    {
        var spc = SpcBuilder.Build(
            System.Text.Encoding.ASCII.GetBytes("0123456789abcdef0123456789abcdef"),
            new byte[16],
            new byte[16],
            PublicKeyPem);

        Assert.NotNull(Convert.FromBase64String(spc));
        Assert.Equal(320, spc.Length); // 240 字节 → base64
    }
}
