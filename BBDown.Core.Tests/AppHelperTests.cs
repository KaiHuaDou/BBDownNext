using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Text;

namespace BBDown.Core.Tests;

public class AppHelperTests
{
    [Fact]
    public void PackMessage_ThenReadMessage_RoundTrips( )
    {
        var payload = Encoding.UTF8.GetBytes("BV1uv411q7Mv 的 gRPC 请求体");

        var restored = AppHelper.ReadMessage(AppHelper.PackMessage(payload));

        Assert.Equal(payload, restored);
    }

    [Fact]
    public void PackMessage_ThenReadMessage_RoundTripsLargePayload( )
    {
        var payload = Enumerable.Range(0, 200_000).Select(i => (byte) (i % 251)).ToArray( );

        var packed = AppHelper.PackMessage(payload);

        Assert.True(packed.Length < payload.Length);
        Assert.Equal(payload, AppHelper.ReadMessage(packed));
    }

    [Fact]
    public void PackMessage_WritesGzipFlagAndBigEndianBodyLength( )
    {
        var packed = AppHelper.PackMessage(Encoding.UTF8.GetBytes("hello"));

        Assert.Equal(1, packed[0]);
        Assert.Equal(packed.Length - 5, BinaryPrimitives.ReadInt32BigEndian(packed.AsSpan(1, 4)));
    }

    [Fact]
    public void PackMessage_HandlesEmptyPayload( )
    {
        Assert.Empty(AppHelper.ReadMessage(AppHelper.PackMessage([])));
    }

    [Fact]
    public void ReadMessage_ReadsUncompressedBodyAndIgnoresTrailingBytes( )
    {
        byte[] data = [0, 0, 0, 0, 3, 1, 2, 3, 9, 9];

        Assert.Equal([1, 2, 3], AppHelper.ReadMessage(data));
    }

    [Theory]
    [InlineData(new byte[] { })]
    [InlineData(new byte[] { 0 })]
    [InlineData(new byte[] { 0, 1, 2 })]
    [InlineData(new byte[] { 0, 0, 0, 0 })]
    public void ReadMessage_ThrowsOnTruncatedHeader(byte[] data)
    {
        Assert.Throws<InvalidDataException>(( ) => AppHelper.ReadMessage(data));
    }

    [Theory]
    [InlineData(new byte[] { 0, 0, 0, 0, 4, 1, 2, 3 })]
    [InlineData(new byte[] { 0, 0x7F, 0xFF, 0xFF, 0xFF, 1 })]
    [InlineData(new byte[] { 0, 0xFF, 0xFF, 0xFF, 0xFF, 1 })]
    public void ReadMessage_ThrowsWhenDeclaredSizeExceedsBuffer(byte[] data)
    {
        Assert.Throws<InvalidDataException>(( ) => AppHelper.ReadMessage(data));
    }

    [Fact]
    public void ReadMessage_DecompressesExactBodyIgnoringTrailer( )
    {
        var packed = AppHelper.PackMessage(Encoding.UTF8.GetBytes("带 trailer 的 gRPC 帧"));
        byte[] withTrailer = [.. packed, 0x80, 0, 0, 0, 0x10, .. Encoding.ASCII.GetBytes("grpc-status:0\r\n")];

        Assert.Equal(Encoding.UTF8.GetBytes("带 trailer 的 gRPC 帧"), AppHelper.ReadMessage(withTrailer));
    }
}
