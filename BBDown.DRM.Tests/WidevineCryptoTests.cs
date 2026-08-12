using System;
using System.IO;

namespace BBDown.DRM.Tests;

public class WidevineCryptoTests
{
    [Theory]
    // RFC 4493 附录 A 测试向量
    [InlineData("2b7e151628aed2a6abf7158809cf4f3c", "", "bb1d6929e95937287fa37d129b756746")]
    [InlineData("2b7e151628aed2a6abf7158809cf4f3c", "6bc1bee22e409f96e93d7e117393172a", "070a16b46b4d4144f79bdd9dd04a287c")]
    [InlineData("2b7e151628aed2a6abf7158809cf4f3c", "6bc1bee22e409f96e93d7e117393172aae2d8a571e03ac9c9eb76fac45af8e5130c81c46a35ce411", "dfa66747de9ae63030ca32611497c827")]
    public void AesCmac_MatchesRfc4493Vectors(string keyHex, string messageHex, string expectedHex)
    {
        var key = Convert.FromHexString(keyHex);
        var message = Convert.FromHexString(messageHex);

        var actual = WidevineCrypto.AesCmac(key, message);

        Assert.Equal(expectedHex, Convert.ToHexString(actual).ToLowerInvariant( ));
    }

    [Fact]
    public void AesEcb_RoundTrip( )
    {
        var key = Convert.FromHexString("2b7e151628aed2a6abf7158809cf4f3c");
        var data = Convert.FromHexString("6bc1bee22e409f96e93d7e117393172a");

        var encrypted = WidevineCrypto.AesEcb(data, key, encrypt: true);
        var decrypted = WidevineCrypto.AesEcb(encrypted, key, encrypt: false);

        Assert.Equal(data, decrypted);
    }

    [Fact]
    public void AesCbcDecrypt_MatchesKnownVector( )
    {
        // NIST SP 800-38A CBC-AES128 向量
        var key = Convert.FromHexString("2b7e151628aed2a6abf7158809cf4f3c");
        var iv = Convert.FromHexString("000102030405060708090a0b0c0d0e0f");
        var ciphertext = Convert.FromHexString("7649abac8119b246cee98e9b12e9197d");

        var plaintext = WidevineCrypto.AesCbcDecrypt(ciphertext, key, iv);

        Assert.Equal(Convert.FromHexString("6bc1bee22e409f96e93d7e117393172a"), plaintext);
    }

    [Fact]
    public void Pkcs7Unpad_StripsPadding( )
    {
        var padded = new byte[16];
        Array.Fill(padded, (byte)0x05, 11, 5);

        var unpadded = WidevineCrypto.Pkcs7Unpad(padded);

        Assert.Equal(11, unpadded.Length);
    }

    [Fact]
    public void Pkcs7Unpad_InvalidPadding_Throws( )
    {
        var bad = new byte[16];
        bad[15] = 0x08; // 声称 8 字节填充但实际不是

        Assert.Throws<InvalidDataException>(() => WidevineCrypto.Pkcs7Unpad(bad));
    }

    [Fact]
    public void DeriveKeys_LengthsAreProtocolExpected( )
    {
        var sessionKey = Convert.FromHexString("00112233445566778899aabbccddeeff");
        var message = Convert.FromHexString("deadbeef");

        var (encKey, macKeyServer, macKeyClient) = WidevineCrypto.DeriveKeys(sessionKey, message);

        Assert.Equal(16, encKey.Length);
        Assert.Equal(32, macKeyServer.Length);
        Assert.Equal(32, macKeyClient.Length);
    }
}
