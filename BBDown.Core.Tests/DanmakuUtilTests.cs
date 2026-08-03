using System;
using System.IO;
using System.Text;

namespace BBDown.Core.Tests;

public class DanmakuUtilTests
{
    [Theory]
    [InlineData(0, "0:00:00.00")]
    [InlineData(5.5, "0:00:05.50")]
    [InlineData(65.5, "0:01:05.50")]
    [InlineData(3661.25, "1:01:01.25")]
    [InlineData(36000, "10:00:00.00")]
    // 边界进位，历史实现会产出 0:59:60.00 / 0:00:60.00
    [InlineData(3599.999, "1:00:00.00")]
    [InlineData(59.999, "0:01:00.00")]
    public void ComputeTime_FormatsAssTimestamp(double second, string expected)
    {
        Assert.Equal(expected, DanmakuUtil.DanmakuItem.ComputeTime(second));
    }

    [Fact]
    public void UpdatePosition_AssignsLinesTopDown( )
    {
        var controller = new DanmakuUtil.PositionController( );

        Assert.Equal(0, controller.UpdatePosition(1, 0, 10));
        Assert.Equal(40, controller.UpdatePosition(1, 0, 10));
        Assert.Equal(80, controller.UpdatePosition(1, 0, 10));
    }

    [Fact]
    public void UpdatePosition_ReturnsMinusOneWhenAllLinesBusy( )
    {
        var controller = new DanmakuUtil.PositionController( );

        // 1080 * 50% / 40 = 13 行
        for (var i = 0; i < 13; i++)
        {
            Assert.Equal(i * 40, controller.UpdatePosition(2, 0, 4));
        }

        Assert.Equal(-1, controller.UpdatePosition(2, 0, 4));
    }

    [Fact]
    public void UpdatePosition_ReusesLineAfterItExpires( )
    {
        var controller = new DanmakuUtil.PositionController( );

        Assert.Equal(0, controller.UpdatePosition(2, 0, 4));
        Assert.Equal(40, controller.UpdatePosition(2, 0, 4));
        // 顶部弹幕停留 4 秒，此刻第一行刚好释放
        Assert.Equal(0, controller.UpdatePosition(2, 4.0, 4));
    }

    [Fact]
    public void UpdatePosition_TracksEachModeIndependently( )
    {
        var controller = new DanmakuUtil.PositionController( );

        Assert.Equal(0, controller.UpdatePosition(1, 0, 4));
        Assert.Equal(0, controller.UpdatePosition(2, 0, 4));
        Assert.Equal(0, controller.UpdatePosition(3, 0, 4));
        Assert.Equal(40, controller.UpdatePosition(1, 0, 4));
    }

    [Fact]
    public void ParseXml_ReadsModeColorAndTiming( )
    {
        const string xml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <i>
          <d p="10.5,1,25,16777215,1600000000,0,abc,1001">滚动弹幕</d>
          <d p="20.25,5,25,16711680,1600000001,0,abc,1002">顶部弹幕</d>
          <d p="30,4,25,255,1600000002,0,abc,1003">底部弹幕</d>
          <d p="40,1,25">字段不足</d>
        </i>
        """;

        var danmakus = WithTempXml(xml, DanmakuUtil.ParseXml);

        Assert.NotNull(danmakus);
        Assert.Equal(3, danmakus.Length);

        var move = danmakus[0];
        Assert.Equal("滚动弹幕", move.Content);
        Assert.Equal(1, move.DanmakuMode);
        Assert.Equal(10.5, move.Second);
        Assert.Equal("0:00:10.50", move.StartTime);
        Assert.Equal("0:00:18.50", move.EndTime);
        Assert.Equal("FFFFFF", move.Color);
        Assert.Equal("25", move.FontSize);
        Assert.Equal("1600000000", move.Timestamp);

        var top = danmakus[1];
        Assert.Equal(2, top.DanmakuMode);
        Assert.Equal("0:00:24.25", top.EndTime);
        Assert.Equal("FF0000", top.Color);

        var bottom = danmakus[2];
        Assert.Equal(3, bottom.DanmakuMode);
        Assert.Equal("0000FF", bottom.Color);
    }

    [Fact]
    public void ParseXml_ReturnsNullOnMalformedDocument( )
    {
        Assert.Null(WithTempXml("这不是 xml", DanmakuUtil.ParseXml));
    }

    [Fact]
    public void ParseXml_ReturnsEmptyWhenNoDanmakuNode( )
    {
        var danmakus = WithTempXml("<i></i>", DanmakuUtil.ParseXml);

        Assert.NotNull(danmakus);
        Assert.Empty(danmakus);
    }

    private static T WithTempXml<T>(string content, Func<string, T> action)
    {
        var path = Path.Combine(Path.GetTempPath( ), $"bbdown-danmaku-{Guid.NewGuid( ):N}.xml");
        File.WriteAllText(path, content, Encoding.UTF8);
        try
        {
            return action(path);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
