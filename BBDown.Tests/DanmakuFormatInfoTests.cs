namespace BBDown.Tests;

public class DanmakuFormatInfoTests
{
    [Theory]
    [InlineData("xml", DanmakuFormat.Xml)]
    [InlineData("ass", DanmakuFormat.Ass)]
    public void FromFormatName_KnownNames(string name, DanmakuFormat expected)
    {
        Assert.Equal(expected, DanmakuFormatInfo.FromFormatName(name));
    }

    [Theory]
    [InlineData("XML")]   // switch 区分大小写，大写不命中
    [InlineData("Ass")]
    [InlineData("json")]
    [InlineData("")]
    [InlineData(" srt")]
    public void FromFormatName_FallsBackToXml(string name)
    {
        // 未知 / 大小写不符一律回退到 Xml（默认格式），避免下载静默失败
        Assert.Equal(DanmakuFormat.Xml, DanmakuFormatInfo.FromFormatName(name));
    }
}
