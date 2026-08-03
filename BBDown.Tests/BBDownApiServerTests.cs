using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace BBDown.Tests;

/// <summary>
/// P0-9：为 <see cref="BBDownApiServer"/> 补回归测试。
/// 重点是 serve 请求契约 <see cref="ServeRequestOptions"/>（受控子集，结构上无法注入主机可控字段）
/// 与 <see cref="BBDownApiServer.IsSafeWebHook"/>（SSRF 防护），以及变更类端点必须是 POST（P1-15）。
/// </summary>
public class BBDownApiServerTests
{
    #region ServeRequestOptions 受控子集（P0-2 / P0-9）

    [Fact]
    public void ServeRequestOptions_ToDownloadOptions_IgnoresHostControlledInjection( )
    {
        // 模拟攻击者试图在请求体中注入主机可控字段；这些字段不在 ServeRequestOptions 中，
        // 反序列化时被忽略，转换后的 DownloadOptions 回落为安全默认值，结构上杜绝 RCE / 路径逃逸
        const string maliciousJson = """
        {
            "Url": "https://www.bilibili.com/video/BV1xx411c7XD",
            "FFmpegPath": "/evil/ffmpeg",
            "Mp4boxPath": "/evil/mp4box",
            "Aria2cPath": "/evil/aria2c",
            "Aria2cArgs": "--on-download-complete /evil.sh",
            "WorkDir": "/tmp/escape",
            "FilePattern": "../../../etc/cron.d/pwn",
            "MultiFilePattern": "../../../root/.bashrc",
            "Debug": true,
            "UserAgent": "Mozilla/5.0 (attacker)",
            "ConfigFile": "/etc/passwd"
        }
        """;
        var req = JsonSerializer.Deserialize<ServeRequestOptions>(maliciousJson, DownloadOptionsJsonContext.Default.ServeRequestOptions)!;
        var opts = req.ToDownloadOptions( );

        // 这些字段直接决定被拉起的进程、参数与落盘位置，必须以服务端为准，绝不允许请求注入
        Assert.Equal("", opts.FFmpegPath);
        Assert.Equal("", opts.Mp4boxPath);
        Assert.Equal("", opts.Aria2cPath);
        Assert.Equal("", opts.Aria2cArgs);
        Assert.Equal("", opts.WorkDir);
        Assert.Equal("", opts.FilePattern);
        Assert.Equal("", opts.MultiFilePattern);
        Assert.False(opts.Debug);
        Assert.Equal("", opts.UserAgent);
        Assert.Null(opts.ConfigFile);
        // 正常字段仍正确透传
        Assert.Equal("https://www.bilibili.com/video/BV1xx411c7XD", opts.Url);
    }

    [Fact]
    public void ServeRequestOptions_ToDownloadOptions_PreservesClientFields( )
    {
        var req = new ServeRequestOptions
        {
            Url = "https://www.bilibili.com/video/BV1xx411c7XD",
            UseTvApi = true,
            Cookie = "SESSDATA=abc",
            Host = "https://biliplus.example.com"
        };
        var opts = req.ToDownloadOptions( );

        Assert.True(opts.UseTvApi);
        Assert.Equal("SESSDATA=abc", opts.Cookie);
        Assert.Equal("https://biliplus.example.com", opts.Host);
    }

    #endregion

    #region IsSafeWebHook（P1-14 SSRF 防护）

    [Theory]
    [InlineData("http://example.com/hook")]
    [InlineData("https://example.com/callback")]
    [InlineData("https://1.2.3.4/report")]
    public void IsSafeWebHook_AllowsPublicHttpHttps(string url)
    {
        Assert.True(BBDownApiServer.IsSafeWebHook(new Uri(url)));
    }

    [Theory]
    [InlineData("http://localhost/hook")]
    [InlineData("http://127.0.0.1/hook")]
    [InlineData("http://[::1]/hook")]
    [InlineData("http://10.0.0.5/x")]
    [InlineData("http://172.16.0.1/x")]
    [InlineData("http://192.168.1.1/x")]
    [InlineData("http://172.31.255.255/x")]
    [InlineData("http://169.254.169.254/latest/meta-data/")] // 云元数据服务
    [InlineData("http://[fc00::1]/x")]
    [InlineData("http://[fd12:3456::1]/x")]
    [InlineData("http://0.0.0.0/x")]
    [InlineData("ftp://example.com/x")]
    [InlineData("file:///etc/passwd")]
    public void IsSafeWebHook_RejectsLoopbackPrivateAndNonHttp(string url)
    {
        Assert.False(BBDownApiServer.IsSafeWebHook(new Uri(url)));
    }

    #endregion

    #region 变更类端点必须是 POST（P1-15）+ 整体管线冒烟

    [Fact]
    public async Task Serve_GetTasks_ReturnsOk( )
    {
        var server = new BBDownApiServer( );
        var baseUrl = await server.StartForTestAsync( );
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(baseUrl) };
            using var resp = await client.GetAsync("/get-tasks", TestContext.Current.CancellationToken);
            Assert.True(resp.IsSuccessStatusCode);
        }
        finally
        {
            await server.StopForTestAsync( );
        }
    }

    [Fact]
    public async Task Serve_RemoveFinished_RequiresPost( )
    {
        var server = new BBDownApiServer( );
        var baseUrl = await server.StartForTestAsync( );
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(baseUrl) };
            // GET 不应被允许（避免与全开 CORS 叠加形成 CSRF）
            using (var get = await client.GetAsync("/remove-finished", TestContext.Current.CancellationToken))
            {
                Assert.Equal(HttpStatusCode.MethodNotAllowed, get.StatusCode);
            }

            using var post = await client.PostAsync("/remove-finished", null, TestContext.Current.CancellationToken);
            Assert.True(post.IsSuccessStatusCode);
        }
        finally
        {
            await server.StopForTestAsync( );
        }
    }

    [Fact]
    public async Task Serve_RemoveFinishedFailed_AcceptsPost( )
    {
        var server = new BBDownApiServer( );
        var baseUrl = await server.StartForTestAsync( );
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(baseUrl) };
            using var resp = await client.PostAsync("/remove-finished/failed", null, TestContext.Current.CancellationToken);
            Assert.True(resp.IsSuccessStatusCode);
        }
        finally
        {
            await server.StopForTestAsync( );
        }
    }

    [Fact]
    public async Task Serve_RemoveFinishedById_AcceptsPost( )
    {
        var server = new BBDownApiServer( );
        var baseUrl = await server.StartForTestAsync( );
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(baseUrl) };
            using var resp = await client.PostAsync("/remove-finished/BV1xx411c7XD", null, TestContext.Current.CancellationToken);
            Assert.True(resp.IsSuccessStatusCode);
        }
        finally
        {
            await server.StopForTestAsync( );
        }
    }

    [Fact]
    public async Task Serve_AddTask_RequiresPost( )
    {
        var server = new BBDownApiServer( );
        var baseUrl = await server.StartForTestAsync( );
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(baseUrl) };
            // /add-task 仅允许 POST，GET 必须 405（不会触发实际下载逻辑）
            using var resp = await client.GetAsync("/add-task", TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.MethodNotAllowed, resp.StatusCode);
        }
        finally
        {
            await server.StopForTestAsync( );
        }
    }

    [Fact]
    public async Task Serve_AddTask_RejectsMalformedBodyWithoutNetwork( )
    {
        var server = new BBDownApiServer( );
        var baseUrl = await server.StartForTestAsync( );
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(baseUrl) };
            // 发送无法绑定为 ServeRequestOptions 的内容，应在进入下载逻辑前返回 400，不触发任何网络请求
            using var content = new StringContent("\"not-an-object\"", System.Text.Encoding.UTF8, "application/json");
            using var resp = await client.PostAsync("/add-task", content, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        }
        finally
        {
            await server.StopForTestAsync( );
        }
    }

    [Fact]
    public async Task Serve_WorkDir_FallsBackToServerConfig( )
    {
        // 缺陷回归：此前 SetUpServer 丢弃了 --work-dir，serve 任务始终落到进程当前目录。
        // 验证服务端配置的工作目录会被注入到每个任务（且请求体不含该字段，无法被客户端覆盖）。
        var server = new BBDownApiServer( );
        var tmp = Path.Combine(Path.GetTempPath( ), "bbdown-workdir-" + Guid.NewGuid( ).ToString("N"));
        server.SetUpServer(tmp);
        try
        {
            var opts = server.ApplyServeWorkDir(new DownloadOptions { Url = "https://www.bilibili.com/video/BV1xx411c7XD" });
            Assert.Equal(tmp, opts.WorkDir);
        }
        finally
        {
            await server.StopForTestAsync( );
        }
    }

    #endregion
}
