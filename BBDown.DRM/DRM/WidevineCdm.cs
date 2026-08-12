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

// Widevine CDM：持 device.wvd 与 B 站 license 服务器完成取钥。
// 流程：解析 PSSH 取 KID → 构建 LicenseRequest 并用设备私钥签名 → 请求 bvc-drm →
// 解密响应中的会话密钥，派生 enc/mac 密钥，校验签名后解出内容密钥。
// B 站不使用 service certificate / privacy mode，无证书环节。
internal static class WidevineCdm
{
    private const string LicenseUrl = "https://bvc-drm.bilivideo.com/bili_widevine";
    private static readonly byte[] WidevineSystemId = [0xed, 0xef, 0x8b, 0xa9, 0x79, 0xd6, 0x4a, 0xce, 0xa3, 0xc8, 0x27, 0xdc, 0xd5, 0x1d, 0x21, 0xed];

    public static async Task<(string Kid, string Key)[]?> GetKeysAsync(string psshBase64, string wvdPath, CancellationToken ct = default)
    {
        using var device = WvdDevice.Load(wvdPath);
        var (payload, keyIds) = ParsePsshBox(psshBase64);
        if (keyIds.Count == 0)
        {
            return null;
        }

        var (challenge, plaintext) = BuildChallenge(device, payload);
        var response = await SendRequestAsync(challenge, ct);
        return ParseResponse(response, plaintext, device);
    }

    // 标准 PSSH box：size/type 头 + version+flags + system_id + （v1 起 KID 列表）+ data 载荷
    private static (byte[] Payload, List<byte[]> KeyIds) ParsePsshBox(string psshBase64)
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

    private static async Task<byte[]> SendRequestAsync(byte[] challenge, CancellationToken ct)
    {
        using var content = new ByteArrayContent(challenge);
        content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/x-protobuf");
        using var request = new HttpRequestMessage(HttpMethod.Post, LicenseUrl) { Content = content };
        request.Headers.TryAddWithoutValidation("User-Agent", HTTPUtil.UserAgent);
        request.Headers.TryAddWithoutValidation("Referer", "https://www.bilibili.com");
        using var response = await HTTPUtil.AppHttpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode( );
        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    private static (string Kid, string Key)[]? ParseResponse(byte[] data, byte[] plaintext, WvdDevice device)
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

        return keys.Count > 0 ? keys.ToArray( ) : null;
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

    private static uint ReadU32Be(byte[] buffer, int offset)
    {
        return ((uint)buffer[offset] << 24) | ((uint)buffer[offset + 1] << 16) | ((uint)buffer[offset + 2] << 8) | buffer[offset + 3];
    }
}
