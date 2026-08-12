using System;
using System.Collections.Generic;

using BBDown.DRM.Proto;

using Google.Protobuf;

namespace BBDown.DRM.Tests;

public class PsshBoxTests
{
    private static readonly byte[] WidevineSystemId =
    [
        0xed, 0xef, 0x8b, 0xa9, 0x79, 0xd6, 0x4a, 0xce,
        0xa3, 0xc8, 0x27, 0xdc, 0xd5, 0x1d, 0x21, 0xed
    ];

    // 构造标准 PSSH box：v1 带 KID 列表 + payload（WidevineCencHeader 编码）
    private static string BuildPssh(byte[] kid, byte[]? payload = null)
    {
        var header = new WidevineCencHeader
        {
            ProtectionScheme = 0x63626373 // cbcs
        };
        header.KeyIds.Add(ByteString.CopyFrom(kid));
        var data = payload ?? header.ToByteArray( );

        // box: size(4) + "pssh"(4) + version/flags(4) + system_id(16) + kid_count(4) + kid + data_size(4) + data
        var body = new List<byte>( );
        body.AddRange([1, 0, 0, 0]); // version 1 + flags
        body.AddRange(WidevineSystemId);
        body.AddRange(ToU32Be(1));
        body.AddRange(kid);
        body.AddRange(ToU32Be(data.Length));
        body.AddRange(data);

        var box = new byte[8 + body.Count];
        var size = box.Length;
        box[0] = (byte)(size >> 24);
        box[1] = (byte)(size >> 16);
        box[2] = (byte)(size >> 8);
        box[3] = (byte)size;
        box[4] = (byte)'p';
        box[5] = (byte)'s';
        box[6] = (byte)'s';
        box[7] = (byte)'h';
        body.CopyTo(box, 8);
        return Convert.ToBase64String(box);
    }

    [Fact]
    public void Parse_V1WithKidList_ExtractsKidAndPayload( )
    {
        var kid = new byte[16];
        for (var i = 0; i < 16; i++)
        {
            kid[i] = (byte)i;
        }

        var (payload, keyIds) = PsshBox.Parse(BuildPssh(kid));

        Assert.Single(keyIds);
        Assert.Equal(kid, keyIds[0]);
        Assert.NotEmpty(payload);
    }

    [Fact]
    public void Parse_NonWidevineSystemId_ReturnsEmpty( )
    {
        var other = new byte[16];
        other[0] = 0x01;

        var body = new List<byte>( );
        body.AddRange([0, 0, 0, 0]);
        body.AddRange(other);
        body.AddRange(ToU32Be(0));
        var box = new byte[8 + body.Count];
        var size = box.Length;
        box[0] = (byte)(size >> 24);
        box[1] = (byte)(size >> 16);
        box[2] = (byte)(size >> 8);
        box[3] = (byte)size;
        box[4] = (byte)'p';
        box[5] = (byte)'s';
        box[6] = (byte)'s';
        box[7] = (byte)'h';
        body.CopyTo(box, 8);

        var (payload, keyIds) = PsshBox.Parse(Convert.ToBase64String(box));

        Assert.Empty(keyIds);
        Assert.Empty(payload);
    }

    [Fact]
    public void Parse_InvalidBase64_ReturnsEmpty( )
    {
        var (payload, keyIds) = PsshBox.Parse("!!!not-base64!!!");

        Assert.Empty(keyIds);
        Assert.Empty(payload);
    }

    private static byte[] ToU32Be(int value)
    {
        return [(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];
    }
}
