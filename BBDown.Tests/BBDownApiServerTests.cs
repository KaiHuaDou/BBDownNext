using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core;
using BBDown.Core.Entity;

namespace BBDown.Tests;

/// <summary>
/// P0-9：为 <see cref="BBDownApiServer"/> 补回归测试。
/// 重点是 serve 请求契约 <see cref="ServeRequestOptions"/>（受控子集，结构上无法注入主机可控字段）
/// 与 <see cref="SsrfGuard.IsSafeWebHook"/>（SSRF 防护），以及变更类端点必须是 POST（P1-15）。
/// </summary>
public class BBDownApiServerTests
{
    #region ServeRequestOptions 受控子集（P0-2 / P0-9）

    [Fact]
    public void ServeRequestOptions_ToDownloadRequest_IgnoresHostControlledInjection( )
    {
        // 模拟攻击者试图在请求体中注入主机可控字段；这些字段不在 ServeRequestOptions 中，
        // 反序列化时被忽略，转换后的 DownloadRequest 回落为安全默认值，结构上杜绝 RCE / 路径逃逸
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
            "ConfigFile": "/etc/passwd",
            "Host": "https://evil.example.com",
            "EpHost": "https://evil.example.com",
            "TvHost": "https://evil.example.com"
        }
        """;
        var req = JsonSerializer.Deserialize<ServeRequestOptions>(maliciousJson, ServeRequestOptionsJsonContext.Default.ServeRequestOptions)!;
        var opts = req.ToDownloadRequest( );

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
        // 请求不带 cookie 时会回落本机 SESSDATA，host 若可由请求体控制就成了凭据外泄链（P0-1）
        Assert.Equal(BiliApi.MainHost, opts.Host);
        Assert.Equal(BiliApi.MainHost, opts.EpHost);
        Assert.Equal(BiliApi.TvHost, opts.TvHost);
        // 正常字段仍正确透传
        Assert.Equal("https://www.bilibili.com/video/BV1xx411c7XD", opts.Url);
    }

    [Fact]
    public void ServeRequestOptions_ToDownloadRequest_PreservesClientFields( )
    {
        var req = new ServeRequestOptions
        {
            Url = "https://www.bilibili.com/video/BV1xx411c7XD",
            Api = ApiType.Tv,
            Cookie = "SESSDATA=abc",
            AllowPreview = true
        };
        var opts = req.ToDownloadRequest( );

        Assert.Equal(ApiType.Tv, opts.Api);
        Assert.Equal("SESSDATA=abc", opts.Cookie);
        Assert.True(opts.AllowPreview);
    }

    [Fact]
    public void ServeRequestOptions_ToDownloadRequest_MapsContentAndApi( )
    {
        var req = new ServeRequestOptions
        {
            Url = "https://www.bilibili.com/video/BV1xx411c7XD",
            Content = ContentSelector.FromNormalizedString("avmsCiM"),
            Api = ApiType.Intl
        };
        var opts = req.ToDownloadRequest( );

        Assert.Equal(ContentSelector.DefaultFlags, opts.Content);
        Assert.Equal(ApiType.Intl, opts.Api);
    }

    [Fact]
    public void ServeRequestOptions_JsonRoundTrip_ParsesContentAndApiStrings( )
    {
        // 请求体用字符串表达内容集与 API 通道，与 CLI 输入一致；转换后落入枚举字段
        const string json = """{"Url":"https://www.bilibili.com/video/BV1xx411c7XD","Content":"av","Api":"tv"}""";
        var req = JsonSerializer.Deserialize<ServeRequestOptions>(json, ServeRequestOptionsJsonContext.Default.ServeRequestOptions)!;
        var opts = req.ToDownloadRequest( );

        Assert.Equal(DownloadContent.Audio | DownloadContent.Video, opts.Content);
        Assert.Equal(ApiType.Tv, opts.Api);
    }

    #endregion

    #region IsSafeWebHook（P1-14 SSRF 防护）

    [Theory]
    [InlineData("http://example.com/hook")]
    [InlineData("https://example.com/callback")]
    [InlineData("https://1.2.3.4/report")]
    public void IsSafeWebHook_AllowsPublicHttpHttps(string url)
    {
        Assert.True(SsrfGuard.IsSafeWebHook(new Uri(url)));
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
        Assert.False(SsrfGuard.IsSafeWebHook(new Uri(url)));
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
            using var resp = await client.PostAsync("/remove-finished/av123", null, TestContext.Current.CancellationToken);
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

    #region 令牌认证矩阵（启用 --serve-token 后所有端点拒绝未认证请求）

    [Theory]
    [InlineData("GET", "/get-tasks")]
    [InlineData("GET", "/get-tasks/running")]
    [InlineData("GET", "/get-tasks/finished")]
    [InlineData("GET", "/get-tasks/av12345678")]
    [InlineData("POST", "/add-task")]
    [InlineData("POST", "/remove-finished")]
    [InlineData("POST", "/remove-finished/failed")]
    [InlineData("POST", "/stop-task/av12345678")]
    public async Task Serve_WithTokenEnabled_AllEndpointsRejectUnauthenticated(string method, string path)
    {
        var server = new BBDownApiServer( );
        var baseUrl = await server.StartForTestAsync(serveToken: "test-token");
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(baseUrl) };
            using var request = new HttpRequestMessage(new HttpMethod(method), path);
            using var resp = await client.SendAsync(request, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        }
        finally
        {
            await server.StopForTestAsync( );
        }
    }

    [Fact]
    public async Task Serve_WithTokenEnabled_WrongTokenRejected( )
    {
        var server = new BBDownApiServer( );
        var baseUrl = await server.StartForTestAsync(serveToken: "test-token");
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(baseUrl) };
            using var request = new HttpRequestMessage(HttpMethod.Get, "/get-tasks");
            request.Headers.TryAddWithoutValidation("X-BBDown-Token", "wrong-token");
            using var resp = await client.SendAsync(request, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        }
        finally
        {
            await server.StopForTestAsync( );
        }
    }

    [Fact]
    public async Task Serve_WithTokenEnabled_HeaderTokenAccepted( )
    {
        var server = new BBDownApiServer( );
        var baseUrl = await server.StartForTestAsync(serveToken: "test-token");
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(baseUrl) };
            using var request = new HttpRequestMessage(HttpMethod.Get, "/get-tasks");
            request.Headers.TryAddWithoutValidation("X-BBDown-Token", "test-token");
            using var resp = await client.SendAsync(request, TestContext.Current.CancellationToken);
            Assert.True(resp.IsSuccessStatusCode);
        }
        finally
        {
            await server.StopForTestAsync( );
        }
    }

    [Fact]
    public async Task Serve_WithTokenEnabled_QueryTokenAccepted( )
    {
        var server = new BBDownApiServer( );
        var baseUrl = await server.StartForTestAsync(serveToken: "test-token");
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(baseUrl) };
            using var resp = await client.GetAsync("/get-tasks?token=test-token", TestContext.Current.CancellationToken);
            Assert.True(resp.IsSuccessStatusCode);
        }
        finally
        {
            await server.StopForTestAsync( );
        }
    }

    #endregion

    #region ResourceId 规范 id 与 JSON 契约（ResourceId 重构后 serve 契约）

    [Theory]
    [InlineData("av114514", typeof(ResourceId.Av), 114514L)]
    [InlineData("ep2539", typeof(ResourceId.Ep), 2539L)]
    [InlineData("season2539", typeof(ResourceId.Season), 2539L)]
    [InlineData("cheeseEp123", typeof(ResourceId.CheeseEp), 123L)]
    [InlineData("cheeseSeason123", typeof(ResourceId.CheeseSeason), 123L)]
    [InlineData("mediaList789", typeof(ResourceId.MediaList), 789L)]
    [InlineData("series789", typeof(ResourceId.Series), 789L)]
    [InlineData("space402787936", typeof(ResourceId.Space), 402787936L)]
    public void ResourceId_TryParse_AcceptsCanonicalForms(string input, Type type, long value)
    {
        Assert.True(ResourceId.TryParse(input, out var id));
        Assert.Equal(type, id!.GetType( ));
        Assert.Equal(value, id switch
        {
            ResourceId.Av a => a.Aid,
            ResourceId.Ep e => e.EpId,
            ResourceId.Season s => s.SeasonId,
            ResourceId.CheeseEp e => e.EpId,
            ResourceId.CheeseSeason s => s.SeasonId,
            ResourceId.MediaList m => m.BizId,
            ResourceId.Series s => s.BizId,
            ResourceId.Space s => s.Mid,
            _ => 0
        });
    }

    [Fact]
    public void ResourceId_TryParse_FavAndWatchLater( )
    {
        Assert.True(ResourceId.TryParse("fav100_200", out var fav));
        Assert.Equal(new ResourceId.Fav(100, 200), fav);

        Assert.True(ResourceId.TryParse("watchLater", out var watch));
        Assert.Equal(new ResourceId.WatchLater( ), watch);
    }

    [Theory]
    [InlineData("")]
    [InlineData("114514")]              // 裸数字（旧 AID 契约，已废弃）
    [InlineData("av")]                  // 缺值
    [InlineData("avabc")]               // 非数字
    [InlineData("av:1:2")]              // 旧冒号形态，已废弃
    [InlineData("BV1xx411c7XD")]        // 输入简写，非规范 id
    [InlineData("ep:ss2539")]           // 旧打标形态
    [InlineData("fav100")]              // fav 缺 mid
    [InlineData("watchLater:")]         // watchLater 无值形态不带冒号
    public void ResourceId_TryParse_RejectsNonCanonical(string input)
    {
        Assert.False(ResourceId.TryParse(input, out _));
    }

    [Fact]
    public void DownloadTask_Json_SerializesIdAsCanonicalString( )
    {
        var task = new DownloadTask(new ResourceId.Season(2539), "ss2539", 0);
        var json = JsonSerializer.Serialize(task, AppJsonSerializerContext.Default.DownloadTask);

        Assert.Contains("\"Id\":\"season2539\"", json);
    }

    #endregion

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
            var opts = server.ApplyServeWorkDir(new DownloadRequest { Url = "https://www.bilibili.com/video/BV1xx411c7XD" });
            Assert.Equal(tmp, opts.WorkDir);
        }
        finally
        {
            await server.StopForTestAsync( );
        }
    }

    [Fact]
    public async Task Serve_Host_FallsBackToServerConfig( )
    {
        // P0-1 回归：host 由 serve 启动参数决定，请求体不含该字段，无法被客户端覆盖。
        // 验证服务端配置的 host 会被注入到每个任务；空值回落官方默认。
        var server = new BBDownApiServer( );
        server.SetUpServer(host: "https://biliplus.example.com", epHost: "https://biliplus.example.com", tvHost: "api.snm0516.aisee.tv");
        try
        {
            var opts = server.ApplyServeHost(new DownloadRequest { Url = "https://www.bilibili.com/video/BV1xx411c7XD" });
            Assert.Equal("https://biliplus.example.com", opts.Host);
            Assert.Equal("https://biliplus.example.com", opts.EpHost);
            Assert.Equal("api.snm0516.aisee.tv", opts.TvHost);
        }
        finally
        {
            await server.StopForTestAsync( );
        }
    }

    [Fact]
    public async Task Serve_Host_EmptyFallsBackToDefault( )
    {
        // §2.5：serve 启动参数 host 为空时回落官方默认，避免空 host 抛出 UriFormatException
        var server = new BBDownApiServer( );
        server.SetUpServer(host: "", epHost: null, tvHost: "  ");
        try
        {
            var opts = server.ApplyServeHost(new DownloadRequest { Url = "https://www.bilibili.com/video/BV1xx411c7XD" });
            Assert.Equal(BiliApi.MainHost, opts.Host);
            Assert.Equal(BiliApi.MainHost, opts.EpHost);
            Assert.Equal(BiliApi.TvHost, opts.TvHost);
        }
        finally
        {
            await server.StopForTestAsync( );
        }
    }

    #endregion

    #region IsPrivateAddress（§2.4 私网段补全）

    [Theory]
    [InlineData("::")]                       // IPv6 未指定地址
    [InlineData("100.64.0.1")]               // CGNAT
    [InlineData("100.127.255.254")]          // CGNAT 上界
    [InlineData("192.0.0.1")]                // 192.0.0.0/24
    [InlineData("198.18.0.1")]               // 198.18.0.0/15 benchmark
    [InlineData("198.19.255.255")]           // 198.18.0.0/15 上界
    [InlineData("224.0.0.1")]                // 多播
    [InlineData("239.255.255.255")]          // 多播上界
    [InlineData("127.0.0.1")]                // 回环
    [InlineData("10.0.0.1")]                 // RFC1918
    [InlineData("172.16.0.1")]               // RFC1918
    [InlineData("192.168.1.1")]              // RFC1918
    [InlineData("169.254.169.254")]          // 链路本地/云元数据
    [InlineData("::ffff:169.254.169.254")]   // IPv4-mapped 云元数据（绕过修复）
    [InlineData("::ffff:10.0.0.1")]          // IPv4-mapped RFC1918
    [InlineData("0.0.0.0")]                  // 未指定
    [InlineData("::1")]                      // IPv6 回环
    [InlineData("fd00::1")]                  // IPv6 ULA
    [InlineData("fc00::1")]                  // IPv6 ULA
    [InlineData("fe80::1")]                  // IPv6 链路本地
    [InlineData("ff02::1")]                  // IPv6 多播
    public void IsPrivateAddress_RejectsPrivate(string ip)
    {
        Assert.True(SsrfGuard.IsPrivateAddress(IPAddress.Parse(ip)));
    }

    [Theory]
    [InlineData("1.2.3.4")]
    [InlineData("8.8.8.8")]
    [InlineData("203.0.113.5")]              // TEST-NET-3 文档地址（非私网）
    [InlineData("198.20.0.1")]              // 198.18.0.0/15 之外
    [InlineData("100.63.255.255")]          // CGNAT 之外
    [InlineData("192.0.1.1")]               // 192.0.0.0/24 之外
    [InlineData("223.255.255.255")]         // 多播之外（公网最大单播）
    [InlineData("2001:db8::1")]             // 文档地址（公网段）
    public void IsPrivateAddress_AllowsPublic(string ip)
    {
        Assert.False(SsrfGuard.IsPrivateAddress(IPAddress.Parse(ip)));
    }

    #endregion

    #region 进度回吐（PipelineSink）

    // 下载链路不再持有 DownloadTask，只通过回调回吐；这里锁住三个回调的映射
    [Fact]
    public void SinkFor_RoutesCallbacksIntoTask( )
    {
        var task = new DownloadTask(new ResourceId.Av(114514), "BV1xx411c7XD", 0);
        var sink = BBDownApiServer.SinkFor(task);

        sink.Meta!(new VInfo { Title = "标题", Desc = "", Pic = "https://i0.hdslb.com/x.jpg", PubTime = 1700000000, PagesInfo = [] });
        sink.Saved!("D:/out/a.mp4");
        sink.Saved!("D:/out/b.mp4");
        sink.Sample!(0.5, 2048);

        Assert.Equal("标题", task.Title);
        Assert.Equal("https://i0.hdslb.com/x.jpg", task.Pic);
        Assert.Equal(1700000000, task.VideoPubTime);
        Assert.Equal(["D:/out/a.mp4", "D:/out/b.mp4"], task.SavePaths);
        Assert.Equal(0.5, task.Progress);
        Assert.Equal(2048, task.TotalDownloadedBytes);
    }

    // CLI 走 default(PipelineSink)：全部回调为 null，下层的 ?.Invoke 必须能安全跳过
    [Fact]
    public void DefaultSink_HasNoCallbacks( )
    {
        PipelineSink sink = default;

        Assert.Null(sink.Meta);
        Assert.Null(sink.Saved);
        Assert.Null(sink.Sample);
        Assert.Null(sink.Downloading);
    }

    #endregion

    #region 并发上限（--max-concurrent）

    [Fact]
    public void SetUpServer_WithoutMaxConcurrent_KeepsUnlimitedBehaviour( )
    {
        var server = new BBDownApiServer( );
        server.SetUpServer( );
        var task = server.CreateTask(new ResourceId.Av(114514), "BV1xx411c7XD");

        Assert.Equal(DownloadStatus.Running, task.Status);
    }

    [Fact]
    public void SetUpServer_WithMaxConcurrent_QueuesAndLeavesParallelismToDownloader( )
    {
        var server = new BBDownApiServer( );
        server.SetUpServer(maxConcurrent: 2);
        var task = server.CreateTask(new ResourceId.Av(114514), "BV1xx411c7XD");

        Assert.Equal(DownloadStatus.Queued, task.Status);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void SetUpServer_NonPositiveMaxConcurrent_MeansUnlimited(int n)
    {
        var server = new BBDownApiServer( );
        server.SetUpServer(maxConcurrent: n);
        Assert.Equal(DownloadStatus.Running, server.CreateTask(new ResourceId.Av(1), "u").Status);
    }

    [Fact]
    public async Task RunGatedAsync_NeverExceedsMaxConcurrent( )
    {
        const int cap = 2;
        const int total = 5;
        var server = new BBDownApiServer( );
        server.SetUpServer(maxConcurrent: cap);

        var running = 0;
        var peak = 0;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = Enumerable.Range(0, total).Select(i => server.CreateTask(new ResourceId.Av(i), "u")).ToList( );
        var runs = tasks.Select(t => server.RunGatedAsync(t, async ( ) =>
        {
            var now = Interlocked.Increment(ref running);
            int old;
            while ((old = Volatile.Read(ref peak)) < now && Interlocked.CompareExchange(ref peak, now, old) != old) { }

            await release.Task;
            Interlocked.Decrement(ref running);
        }, TestContext.Current.CancellationToken)).ToList( );

        var sw = System.Diagnostics.Stopwatch.StartNew( );
        while (Volatile.Read(ref running) < cap && sw.Elapsed < TimeSpan.FromSeconds(5))
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        await Task.Delay(200, TestContext.Current.CancellationToken);
        Assert.Equal(cap, Volatile.Read(ref running));
        Assert.Equal(cap, Volatile.Read(ref peak));
        Assert.Equal(total - cap, tasks.Count(t => t.Status == DownloadStatus.Queued));
        Assert.Equal(cap, tasks.Count(t => t.Status == DownloadStatus.Running));

        release.SetResult( );
        await Task.WhenAll(runs);
        Assert.Equal(cap, Volatile.Read(ref peak));
        Assert.All(tasks, t => Assert.Equal(DownloadStatus.Running, t.Status));
    }

    [Fact]
    public async Task RunGatedAsync_Unlimited_RunsAllConcurrently( )
    {
        var server = new BBDownApiServer( );
        server.SetUpServer( );
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var running = 0;
        var runs = Enumerable.Range(0, 4).Select(i => server.RunGatedAsync(server.CreateTask(new ResourceId.Av(i), "u"),
            async ( ) => { Interlocked.Increment(ref running); await release.Task; },
            TestContext.Current.CancellationToken)).ToList( );

        var sw = System.Diagnostics.Stopwatch.StartNew( );
        while (Volatile.Read(ref running) < 4 && sw.Elapsed < TimeSpan.FromSeconds(5))
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        Assert.Equal(4, Volatile.Read(ref running));
        release.SetResult( );
        await Task.WhenAll(runs);
    }

    [Fact]
    public async Task RunGatedAsync_CancelledWhileQueued_DoesNotRunDownload( )
    {
        var server = new BBDownApiServer( );
        server.SetUpServer(maxConcurrent: 1);
        using var cts = new CancellationTokenSource( );
        var block = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var holder = server.RunGatedAsync(server.CreateTask(new ResourceId.Av(1), "u"), ( ) => block.Task, CancellationToken.None);

        var queued = server.CreateTask(new ResourceId.Av(2), "u");
        var second = server.RunGatedAsync(queued, ( ) => Task.FromException(new InvalidOperationException("不应执行")), cts.Token);
        await cts.CancelAsync( );

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async ( ) => await second);
        Assert.Equal(DownloadStatus.Queued, queued.Status);
        block.SetResult( );
        await holder;
    }

    #endregion
}
