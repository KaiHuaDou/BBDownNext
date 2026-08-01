using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

using BBDown;

using Xunit;

namespace BBDown.Tests;

/// <summary>
/// P0-9：为 <see cref="BBDownApiServer"/> 补回归测试。
/// 重点是安全过滤 <see cref="BBDownApiServer.OverrideHostControlledOptions"/>（防止漏清字段静默重开 RCE）
/// 与 <see cref="BBDownApiServer.IsSafeWebHook"/>（SSRF 防护），以及变更类端点必须是 POST（P1-15）。
/// </summary>
public class BBDownApiServerTests
{
    #region OverrideHostControlledOptions（P0-2 / P0-9）

    [Fact]
    public void OverrideHostControlledOptions_ClearsAllHostControlledFields( )
    {
        var server = new BBDownApiServer( );
        server.SetUpServer(null);
        var req = new ServeRequestOptions
        {
            // 模拟攻击者通过请求注入的恶意主机可控字段
            FFmpegPath = "/evil/ffmpeg",
            Mp4boxPath = "/evil/mp4box",
            Aria2cPath = "/evil/aria2c",
            Aria2cArgs = "--on-download-complete /evil.sh",
            WorkDir = "/tmp/escape",
            FilePattern = "../../../etc/cron.d/pwn",
            MultiFilePattern = "../../../root/.bashrc",
            Debug = true,
            UserAgent = "Mozilla/5.0 (attacker)",
            Url = "https://www.bilibili.com/video/BV1xx411c7XD"
        };

        server.OverrideHostControlledOptions(req);

        // 这些字段直接决定被拉起的进程、参数与落盘位置，必须以服务端为准，绝不允许请求注入
        Assert.Equal("", req.FFmpegPath);
        Assert.Equal("", req.Mp4boxPath);
        Assert.Equal("", req.Aria2cPath);
        Assert.Equal("", req.Aria2cArgs);
        Assert.Equal("", req.FilePattern);
        Assert.Equal("", req.MultiFilePattern);
        Assert.False(req.Debug);
        Assert.Equal("", req.UserAgent);
        // 非主机可控字段不受影响
        Assert.Equal("https://www.bilibili.com/video/BV1xx411c7XD", req.Url);
    }

    [Fact]
    public void OverrideHostControlledOptions_UsesServerWorkDirNotInjected( )
    {
        const string workDir = "/srv/bbdown/work";
        var server = new BBDownApiServer( );
        server.SetUpServer(workDir);
        var req = new ServeRequestOptions { WorkDir = "/attacker/want/this" };

        server.OverrideHostControlledOptions(req);

        // WorkDir 必须被强制改为服务端配置的工作目录，忽略请求注入值
        Assert.Equal(workDir, req.WorkDir);
    }

    [Fact]
    public void OverrideHostControlledOptions_RepeatedCallStillSafe( )
    {
        var server = new BBDownApiServer( );
        server.SetUpServer(null);
        var req = new ServeRequestOptions
        {
            FFmpegPath = "/x",
            FilePattern = "../../pwn",
            Aria2cArgs = "rm -rf /"
        };

        server.OverrideHostControlledOptions(req);
        server.OverrideHostControlledOptions(req);

        Assert.Equal("", req.FFmpegPath);
        Assert.Equal("", req.FilePattern);
        Assert.Equal("", req.Aria2cArgs);
    }

    #endregion

    #region IsSafeWebHook（P1-14 SSRF 防护）

    [Theory]
    [InlineData("http://example.com/hook")]
    [InlineData("https://example.com/callback")]
    [InlineData("https://1.2.3.4/report")]
    public void IsSafeWebHook_AllowsPublicHttpHttps(string url)
        => Assert.True(BBDownApiServer.IsSafeWebHook(new Uri(url)));

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
        => Assert.False(BBDownApiServer.IsSafeWebHook(new Uri(url)));

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

            using (var post = await client.PostAsync("/remove-finished", null, TestContext.Current.CancellationToken))
            {
                Assert.True(post.IsSuccessStatusCode);
            }
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

    #endregion
}
