using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core.Util;
using BBDown.DRM.Proto;

using Google.Protobuf;

namespace BBDown.DRM;

// 一次完整 Widevine 取钥：加载设备 → 解析 PSSH 取 KID → 构建 LicenseRequest 并签名 →
// 请求 bvc-drm → 解密响应会话密钥、派生 enc/mac 密钥、校验签名后解出内容密钥。
// B 站不使用 service certificate / privacy mode，无证书环节。
internal static class WidevineLicense
{
    private const string LicenseUrl = "https://bvc-drm.bilivideo.com/bili_widevine";

    public static async Task<(string Kid, string Key)[]?> FetchAsync(string psshBase64, string wvdPath, CancellationToken token)
    {
        using var device = WvdDevice.Load(wvdPath);
        var (payload, keyIds) = PsshBox.Parse(psshBase64);
        if (keyIds.Count == 0)
        {
            return null;
        }

        var (challenge, plaintext) = BuildChallenge(device, payload);
        var response = await SendRequestAsync(challenge, token);
        return ParseResponse(response, plaintext, device, keyIds);
    }

    private static (byte[] Challenge, byte[] Plaintext) BuildChallenge(WvdDevice device, byte[] payload)
    {
        // request_id：16 随机字节，按 B 站协议以大写 hex 的 ASCII 形式传输
        var requestIdRaw = new byte[16];
        RandomNumberGenerator.Fill(requestIdRaw);
        var requestIdBytes = Encoding.ASCII.GetBytes(Convert.ToHexString(requestIdRaw));

        var request = new LicenseRequest
        {
            ClientId = device.ClientIdentification,
            Type = LicenseRequest.Types.RequestType.New,
            RequestTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds( ),
            ProtocolVersion = ProtocolVersion.Version21,
            KeyControlNonce = (uint)RandomNumberGenerator.GetInt32(1, int.MaxValue),
            ContentId = new LicenseRequest.Types.ContentIdentification
            {
                WidevinePsshData = new LicenseRequest.Types.ContentIdentification.Types.WidevinePsshData
                {
                    PsshData = { ByteString.CopyFrom(payload) },
                    LicenseType = LicenseType.Streaming,
                    RequestId = ByteString.CopyFrom(requestIdBytes)
                }
            }
        };

        var plaintext = request.ToByteArray( );
        // 设备私钥签名：RSASSA-PKCS1-v1_5 + SHA-1（Widevine 协议标准）
        var signature = device.Rsa.SignData(plaintext, HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1);
        var signedMessage = new SignedMessage
        {
            Type = SignedMessage.Types.MessageType.LicenseRequest,
            Msg = ByteString.CopyFrom(plaintext),
            Signature = ByteString.CopyFrom(signature)
        };
        return (signedMessage.ToByteArray( ), plaintext);
    }

    private static async Task<byte[]> SendRequestAsync(byte[] challenge, CancellationToken token)
    {
        using var content = new ByteArrayContent(challenge);
        content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/x-protobuf");
        using var request = new HttpRequestMessage(HttpMethod.Post, LicenseUrl) { Content = content };
        request.Headers.TryAddWithoutValidation("User-Agent", HTTPUtil.UserAgent);
        request.Headers.TryAddWithoutValidation("Referer", "https://www.bilibili.com");
        using var response = await HTTPUtil.AppHttpClient.SendAsync(request, token);
        response.EnsureSuccessStatusCode( );
        return await response.Content.ReadAsByteArrayAsync(token);
    }

    private static (string Kid, string Key)[]? ParseResponse(byte[] data, byte[] plaintext, WvdDevice device, List<byte[]> keyIds)
    {
        SignedMessage signedMessage;
        try
        {
            signedMessage = SignedMessage.Parser.ParseFrom(data);
        }
        catch (InvalidProtocolBufferException)
        {
            return null;
        }

        if (signedMessage.Type != SignedMessage.Types.MessageType.License)
        {
            return null;
        }

        // 会话密钥用设备私钥解密：OAEP-SHA1 优先，旧设备回退 OAEP-SHA256
        var sessionKeyBytes = signedMessage.SessionKey.ToByteArray( );
        byte[] sessionKey;
        try
        {
            sessionKey = device.Rsa.Decrypt(sessionKeyBytes, RSAEncryptionPadding.OaepSHA1);
        }
        catch (CryptographicException)
        {
            sessionKey = device.Rsa.Decrypt(sessionKeyBytes, RSAEncryptionPadding.OaepSHA256);
        }

        if (sessionKey.Length != 16)
        {
            return null;
        }

        var (encKey, macKeyServer, _) = WidevineCrypto.DeriveKeys(sessionKey, plaintext);

        // 响应签名：HMAC-SHA256(macKeyServer, oemcrypto_core_message || msg)
        var msg = signedMessage.Msg.ToByteArray( );
        var oem = signedMessage.OemcryptoCoreMessage.ToByteArray( );
        using var hmac = new HMACSHA256(macKeyServer);
        var buffer = new byte[oem.Length + msg.Length];
        Buffer.BlockCopy(oem, 0, buffer, 0, oem.Length);
        Buffer.BlockCopy(msg, 0, buffer, oem.Length, msg.Length);
        var computed = hmac.ComputeHash(buffer);
        if (!CryptographicOperations.FixedTimeEquals(computed, signedMessage.Signature.ToByteArray( )))
        {
            return null;
        }

        var license = License.Parser.ParseFrom(msg);
        var keys = new List<(string Kid, string Key)>( );
        foreach (var container in license.Key)
        {
            if (container.Type != License.Types.KeyContainer.Types.KeyType.Content)
            {
                continue;
            }

            var kid = container.Id.ToByteArray( );
            var encryptedKey = container.Key.ToByteArray( );
            if (kid.Length == 0 || encryptedKey.Length == 0)
            {
                continue;
            }

            var iv = NormalizeIv(container.Iv.ToByteArray( ));
            // IV 全零或缺失时按 ECB 解（Widevine 规范的特例），否则 CBC + 去填充
            byte[] contentKey;
            if (iv.All(b => b == 0))
            {
                contentKey = WidevineCrypto.AesEcb(encryptedKey, encKey, encrypt: false);
            }
            else
            {
                contentKey = WidevineCrypto.Pkcs7Unpad(WidevineCrypto.AesCbcDecrypt(encryptedKey, encKey, iv));
            }

            keys.Add((Convert.ToHexString(kid).ToLowerInvariant( ), Convert.ToHexString(contentKey).ToLowerInvariant( )));
        }

        if (keys.Count == 0)
        {
            return null;
        }

        // 多 key 时优先返回与 PSSH KID 匹配的 key，避免首个 key 与当前轨道 KID 不符导致解密失败
        var matched = keys.Where(k => keyIds.Any(kid => Convert.FromHexString(k.Kid).AsSpan().SequenceEqual(kid))).ToArray( );
        return matched.Length > 0 ? matched : keys.ToArray( );
    }

    private static byte[] NormalizeIv(byte[] iv)
    {
        if (iv.Length == 16)
        {
            return iv;
        }

        var result = new byte[16];
        Buffer.BlockCopy(iv, 0, result, 0, Math.Min(iv.Length, 16));
        return result;
    }
}
