using BBDown.Serve;
using BBDown.Serve.Http;

namespace BBDown.Tests;

/// <summary>
/// WebSocket 事件通道的 Origin 校验（CSWSH）纯函数测试。
/// 真实握手 / 帧协议 / 连接上限依赖起服务器，属耗时复杂操作，一律不测（见 AGENTS.md「dotnet 命令执行方式」）。
/// </summary>
public class TasksSocketTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("http://localhost:3000")]
    [InlineData("http://127.0.0.1:3000")]
    [InlineData("https://app.example.com")]
    public void IsAllowedOrigin_AllowsLoopbackAndConfigured(string? origin)
    {
        var config = new ServeConfig(CorsOrigin: "https://app.example.com");
        Assert.True(TaskSocketHub.IsAllowedOrigin(origin, config));
    }

    [Theory]
    [InlineData("https://evil.example.com")]
    [InlineData("http://192.168.1.1:80")]
    public void IsAllowedOrigin_RejectsCrossOrigin(string origin)
    {
        var config = new ServeConfig(CorsOrigin: "https://app.example.com");
        Assert.False(TaskSocketHub.IsAllowedOrigin(origin, config));
    }
}
