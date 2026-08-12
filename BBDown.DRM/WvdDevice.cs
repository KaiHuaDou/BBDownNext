using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

using BBDown.DRM.Proto;

namespace BBDown.DRM;

// device.wvd 设备文件：Widevine CDM 的身份凭据，含 client_id 与 RSA 私钥。
// 兼容三种布局：带 "WVD" magic 的 wvd-rs 格式、pywidevine 标准格式（首字节为版本号）、
// 裸 PEM 私钥 + 伴生 client_id 文件。V2 格式私钥加密尚不支持。
internal sealed class WvdDevice : IDisposable
{
    private bool _disposed;

    public byte[] ClientIdBytes { get; }
    public RSA Rsa { get; }
    public ClientIdentification ClientIdentification { get; }

    private WvdDevice(byte[] clientIdBytes, RSA rsa, ClientIdentification clientIdentification)
    {
        ClientIdBytes = clientIdBytes;
        Rsa = rsa;
        ClientIdentification = clientIdentification;
    }

    public static WvdDevice Load(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length == 0)
        {
            throw new InvalidDataException("WVD 文件为空");
        }

        // 首字节 1/2 是 pywidevine 版本号；"WVD" magic 与 '-'（PEM）均不可能是 1/2
        return bytes.Length >= 4 && bytes[0] == (byte)'W' && bytes[1] == (byte)'V' && bytes[2] == (byte)'D'
            ? ParseBinary(bytes.AsSpan(3))
            : bytes[0] is 1 or 2
                ? ParseBinary(bytes.AsSpan( ))
                : ParsePem(path, bytes);
    }

    // pywidevine 布局：version(1) / type(1) / security_level(1) / flags(1) / private_key_len(2) / private_key / client_id_len(2) / client_id
    private static WvdDevice ParseBinary(ReadOnlySpan<byte> data)
    {
        var version = data[0];
        if (version is not (1 or 2))
        {
            throw new InvalidDataException($"不支持的 WVD 版本：{version}");
        }

        if (version == 2 && (data[3] & 0x01) != 0)
        {
            throw new InvalidDataException("加密的 WVD V2 私钥暂不支持");
        }

        var privateKeyLength = (data[4] << 8) | data[5];
        var privateKey = data.Slice(6, privateKeyLength).ToArray( );
        var offset = 6 + privateKeyLength;
        var clientIdLength = (data[offset] << 8) | data[offset + 1];
        var clientIdBytes = data.Slice(offset + 2, clientIdLength).ToArray( );
        return Create(privateKey, clientIdBytes);
    }

    // 裸 PEM 布局：私钥文件本身是 PEM 文本，client_id 从同目录伴生文件读取
    private static WvdDevice ParsePem(string path, byte[] pemBytes)
    {
        var dir = Path.GetDirectoryName(path) ?? ".";
        var baseName = Path.GetFileNameWithoutExtension(path);
        foreach (var candidate in new[]
        {
            Path.Combine(dir, $"{baseName}_client_id.bin"),
            Path.Combine(dir, $"{baseName}.client_id"),
            Path.Combine(dir, "client_id.bin")
        })
        {
            if (File.Exists(candidate))
            {
                return Create(pemBytes, File.ReadAllBytes(candidate));
            }
        }

        throw new InvalidDataException("PEM 格式需要配套的 client_id 文件（_client_id.bin）");
    }

    private static WvdDevice Create(byte[] privateKey, byte[] clientIdBytes)
    {
        var rsa = RSA.Create( );
        try
        {
            ImportPrivateKey(rsa, privateKey);
            // client_id 未必是 protobuf 编码（部分旧设备是裸 blob），失败时包一层 DRM_DEVICE_CERTIFICATE
            ClientIdentification clientId;
            try
            {
                clientId = ClientIdentification.Parser.ParseFrom(clientIdBytes);
            }
            catch (Exception)
            {
                clientId = new ClientIdentification
                {
                    Type = ClientIdentification.Types.TokenType.DrmDeviceCertificate,
                    Token = Google.Protobuf.ByteString.CopyFrom(clientIdBytes)
                };
            }

            return new WvdDevice(clientIdBytes, rsa, clientId);
        }
        catch
        {
            rsa.Dispose( );
            throw;
        }
    }

    // 私钥存储形态不一：优先按 DER（PKCS#1，pywidevine 标准）导入，失败依次尝试 PKCS#8 与 PEM 文本
    private static void ImportPrivateKey(RSA rsa, byte[] privateKey)
    {
        if (privateKey.Length > 10 && Encoding.ASCII.GetString(privateKey, 0, 10) == "-----BEGIN")
        {
            rsa.ImportFromPem(Encoding.ASCII.GetString(privateKey));
            return;
        }

        try
        {
            rsa.ImportRSAPrivateKey(privateKey, out _);
        }
        catch (CryptographicException)
        {
            try
            {
                rsa.ImportPkcs8PrivateKey(privateKey, out _);
            }
            catch (CryptographicException)
            {
                rsa.ImportFromPem($"-----BEGIN RSA PRIVATE KEY-----\n{Encoding.ASCII.GetString(privateKey)}\n-----END RSA PRIVATE KEY-----");
            }
        }
    }

    public void Dispose( )
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Rsa.Dispose( );
    }
}
