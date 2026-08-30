namespace BBDown.Tests;

using BBDown.Serve.Http;

/// <summary>
/// 内嵌 WebUI 静态托管的纯函数测试：扩展名 → MIME 查表。BuildResourceMap / 端点涉及程序集资源与 HTTP 上下文，属耗时 / IO，不测。
/// </summary>
public class WebUiEndpointsTests
{
    [Theory]
    [InlineData("assets/index-abc123.js", "application/javascript")]
    [InlineData("app.mjs", "application/javascript")]
    [InlineData("style.css", "text/css")]
    [InlineData("index.html", "text/html")]
    [InlineData("data.json", "application/json")]
    [InlineData("logo.svg", "image/svg+xml")]
    [InlineData("img.png", "image/png")]
    [InlineData("photo.jpg", "image/jpeg")]
    [InlineData("icon.ico", "image/x-icon")]
    [InlineData("font.woff2", "font/woff2")]
    [InlineData("app.map", "application/json")]
    public void GetContentType_KnownExtension_ReturnsMime(string path, string expected)
    {
        Assert.Equal(expected, WebUiEndpoints.GetContentType(path));
    }

    [Theory]
    // 查表区分大小写：dist 文件名均为小写，故大写扩展名按未知处理
    [InlineData("assets/index-abc123.JS")]
    [InlineData("README")]
    [InlineData("file.unknown")]
    [InlineData("")]
    public void GetContentType_UnknownExtension_ReturnsOctetStream(string path)
    {
        Assert.Equal("application/octet-stream", WebUiEndpoints.GetContentType(path));
    }
}
