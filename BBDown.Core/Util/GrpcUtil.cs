using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;

namespace BBDown.Core.Util;

public static class GrpcUtil
{
    /// <summary>
    /// 读取 gRPC 响应流 通过前 5 字节信息 解析/解压后面的报文体
    /// </summary>
    public static byte[] ReadMessage(byte[] data)
    {
        if (data.Length < 5)
        {
            throw new InvalidDataException($"gRPC 响应帧头不足 5 字节(实际 {data.Length} 字节)");
        }

        var compressed = data[0] == 1;
        var size = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(1, 4));
        if (size < 0 || 5L + size > data.Length)
        {
            throw new InvalidDataException($"gRPC 帧头声明报文体 {size} 字节, 实际只有 {data.Length - 5} 字节");
        }

        var body = data[5..(5 + size)];
        return compressed ? GzipDecompress(body) : body;
    }

    /// <summary>
    /// 给请求载荷添加头部信息
    /// </summary>
    public static byte[] PackMessage(byte[] input)
    {
        using var stream = new MemoryStream( );
        using (var writer = new BinaryWriter(stream))
        {
            var comp = GzipCompress(input);
            Span<byte> reverse = stackalloc byte[4];
            writer.Write((byte) 1);
            BinaryPrimitives.WriteInt32BigEndian(reverse, comp.Length);
            writer.Write(reverse);
            writer.Write(comp);
        }

        return stream.ToArray( );
    }

    private static byte[] GzipCompress(byte[] data)
    {
        using var output = new MemoryStream( );
        using (var comp = new GZipStream(output, CompressionMode.Compress))
        {
            comp.Write(data, 0, data.Length);
        }

        return output.ToArray( );
    }

    private static byte[] GzipDecompress(byte[] data)
    {
        using var output = new MemoryStream( );
        using (var input = new MemoryStream(data))
        {
            using var decomp = new GZipStream(input, CompressionMode.Decompress);
            decomp.CopyTo(output);
        }

        return output.ToArray( );
    }
}
