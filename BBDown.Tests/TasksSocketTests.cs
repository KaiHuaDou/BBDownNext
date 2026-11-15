using BBDown.Serve;
using BBDown.Serve.Http;

namespace BBDown.Tests;

/// <summary>
/// WebSocket 事件通道的 Origin 校验（CSWSH）与写端点 CSRF 共用的纯函数测试
/// 真实握手 / 帧协议 / 连接上限依赖起服务器，属耗时复杂操作，一律不测（见 AGENTS.md「dotnet 命令执行方式」）
/// </summary>
public class TasksSocketTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("http://localhost:23333")]
    [InlineData("http://127.0.0.1:23333")]
    [InlineData("https://app.example.com")]
    public void IsAllowedOrigin_AllowsLoopbackSamePortAndConfigured(string? origin)
    {
        var config = new ServeConfig(CorsOrigin: "https://app.example.com");
        Assert.True(TaskSocketHub.IsAllowedOrigin(origin, config));
    }

    [Theory]
    [InlineData("https://evil.example.com")]
    [InlineData("http://192.168.1.1:80")]
    // 回环来源的端口与监听端口不一致：本机其它端口上的页面不可信
    [InlineData("http://localhost:3000")]
    [InlineData("http://127.0.0.1:3000")]
    [InlineData("http://localhost")]
    public void IsAllowedOrigin_RejectsCrossOriginAndCrossPort(string origin)
    {
        var config = new ServeConfig(CorsOrigin: "https://app.example.com");
        Assert.False(TaskSocketHub.IsAllowedOrigin(origin, config));
    }

    [Fact]
    public void IsAllowedOrigin_UsesListenUrlPort( )
    {
        var config = new ServeConfig(ListenUrl: "http://127.0.0.1:5000");
        Assert.True(TaskSocketHub.IsAllowedOrigin("http://localhost:5000", config));
        Assert.False(TaskSocketHub.IsAllowedOrigin("http://localhost:23333", config));
    }

    [Fact]
    public void IsAllowedOrigin_RejectsLoopbackWhenListenUrlUnparsable( )
    {
        var config = new ServeConfig(ListenUrl: "not-a-url");
        Assert.False(TaskSocketHub.IsAllowedOrigin("http://localhost:23333", config));
    }
}
