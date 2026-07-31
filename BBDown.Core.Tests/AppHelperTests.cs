using System;
using System.Buffers.Binary;
using System.Linq;
using System.Text;
using Xunit;

namespace BBDown.Core.Tests;

public class AppHelperTests
{
    [Fact]
    public void PackMessage_ThenReadMessage_RoundTrips()
    {
        var payload = Encoding.UTF8.GetBytes("BV1uv411q7Mv 的 gRPC 请求体");

        var restored = AppHelper.ReadMessage(AppHelper.PackMessage(payload));

        Assert.Equal(payload, restored);
    }

    [Fact]
    public void PackMessage_ThenReadMessage_RoundTripsLargePayload()
    {
        var payload = Enumerable.Range(0, 200_000).Select(i => (byte) (i % 251)).ToArray( );

        var packed = AppHelper.PackMessage(payload);

        Assert.True(packed.Length < payload.Length);
        Assert.Equal(payload, AppHelper.ReadMessage(packed));
    }

    [Fact]
    public void PackMessage_WritesGzipFlagAndBigEndianBodyLength()
    {
        var packed = AppHelper.PackMessage(Encoding.UTF8.GetBytes("hello"));

        Assert.Equal(1, packed[0]);
        Assert.Equal(packed.Length - 5, BinaryPrimitives.ReadInt32BigEndian(packed.AsSpan(1, 4)));
    }

    [Fact]
    public void PackMessage_HandlesEmptyPayload()
    {
        Assert.Empty(AppHelper.ReadMessage(AppHelper.PackMessage([])));
    }

    [Fact]
    public void ReadMessage_ReadsUncompressedBodyAndIgnoresTrailingBytes()
    {
        byte[] data = [0, 0, 0, 0, 3, 1, 2, 3, 9, 9];

        Assert.Equal([1, 2, 3], AppHelper.ReadMessage(data));
    }

    [Fact]
    public void ReadMessage_ThrowsOnTruncatedHeader()
    {
        Assert.ThrowsAny<ArgumentException>(( ) => AppHelper.ReadMessage([0, 1, 2]));
    }
}
