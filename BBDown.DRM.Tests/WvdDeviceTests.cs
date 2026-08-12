using System;
using System.IO;
using System.Security.Cryptography;

using BBDown.DRM.Proto;

using Google.Protobuf;

namespace BBDown.DRM.Tests;

public class WvdDeviceTests
{
    [Fact]
    public void Load_PywidevineV1Format( )
    {
        using var rsa = RSA.Create( );
        var privateKey = rsa.ExportRSAPrivateKey( );
        var clientId = new ClientIdentification
        {
            Type = ClientIdentification.Types.TokenType.DrmDeviceCertificate,
            Token = Google.Protobuf.ByteString.CopyFrom(new byte[16])
        }.ToByteArray( );

        // version=1, type=0, security_level=3, flags=0
        using var stream = new MemoryStream( );
        stream.WriteByte(1);
        stream.WriteByte(0);
        stream.WriteByte(3);
        stream.WriteByte(0);
        stream.Write(ReverseU16(privateKey.Length));
        stream.Write(privateKey);
        stream.Write(ReverseU16(clientId.Length));
        stream.Write(clientId);

        var path = Path.GetTempFileName( );
        File.WriteAllBytes(path, stream.ToArray( ));
        try
        {
            using var device = WvdDevice.Load(path);

            Assert.Equal(privateKey, device.Rsa.ExportRSAPrivateKey( ));
            Assert.Equal(clientId, device.ClientIdBytes);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_WvdMagicFormat( )
    {
        using var rsa = RSA.Create( );
        var privateKey = rsa.ExportRSAPrivateKey( );
        var clientId = new ClientIdentification
        {
            Token = Google.Protobuf.ByteString.CopyFrom([1, 2, 3, 4])
        }.ToByteArray( );

        using var stream = new MemoryStream( );
        stream.WriteByte((byte)'W');
        stream.WriteByte((byte)'V');
        stream.WriteByte((byte)'D');
        stream.WriteByte(1);
        stream.WriteByte(0);
        stream.WriteByte(3);
        stream.WriteByte(0);
        stream.Write(ReverseU16(privateKey.Length));
        stream.Write(privateKey);
        stream.Write(ReverseU16(clientId.Length));
        stream.Write(clientId);

        var path = Path.GetTempFileName( );
        File.WriteAllBytes(path, stream.ToArray( ));
        try
        {
            using var device = WvdDevice.Load(path);

            Assert.Equal(privateKey, device.Rsa.ExportRSAPrivateKey( ));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_PemPrivateKeyWithClientIdSidecar( )
    {
        using var rsa = RSA.Create( );
        var pem = rsa.ExportRSAPrivateKeyPem( );
        var clientId = new ClientIdentification
        {
            Type = ClientIdentification.Types.TokenType.Keybox,
            Token = Google.Protobuf.ByteString.CopyFrom([9, 8, 7, 6])
        }.ToByteArray( );

        var dir = Path.Combine(Path.GetTempPath( ), $"wvdtest_{Guid.NewGuid( ):N}");
        Directory.CreateDirectory(dir);
        try
        {
            var wvdPath = Path.Combine(dir, "device.wvd");
            var clientPath = Path.Combine(dir, "device_client_id.bin");
            File.WriteAllText(wvdPath, pem);
            File.WriteAllBytes(clientPath, clientId);

            using var device = WvdDevice.Load(wvdPath);

            Assert.Equal(clientId, device.ClientIdBytes);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Load_UnsupportedVersion_Throws( )
    {
        // "WVD" magic 后的版本字节为 9，ParseBinary 应拒绝
        var path = Path.GetTempFileName( );
        File.WriteAllBytes(path, [(byte)'W', (byte)'V', (byte)'D', 9, 0, 3, 0, 0, 0]);
        try
        {
            Assert.Throws<InvalidDataException>(() => WvdDevice.Load(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static byte[] ReverseU16(int value)
    {
        return [(byte)(value >> 8), (byte)value];
    }
}
