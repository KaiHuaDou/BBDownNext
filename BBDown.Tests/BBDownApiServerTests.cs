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
using BBDown.Serve.Tasks;

namespace BBDown.Tests;

/// <summary>
/// P0-9：为 <see cref="BBDownServer"/> 补回归测试。
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
            "url": "https://www.bilibili.com/video/BV1xx411c7XD",
            "ffmpegPath": "/evil/ffmpeg",
            "mp4boxPath": "/evil/mp4box",
            "aria2cPath": "/evil/aria2c",
            "aria2cArgs": "--on-download-complete /evil.sh",
            "workDir": "/tmp/escape",
            "filePattern": "../../../etc/cron.d/pwn",
            "multiFilePattern": "../../../root/.bashrc",
            "debug": true,
            "userAgent": "Mozilla/5.0 (attacker)",
            "configFile": "/etc/passwd",
            "host": "https://evil.example.com",
            "epHost": "https://evil.example.com",
            "tvHost": "https://evil.example.com"
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
        // 请求体用字符串表达内容集与 API 通道，与 CLI 输入一致；转换后落入枚举字段（契约 camelCase）
        const string json = """{"url":"https://www.bilibili.com/video/BV1xx411c7XD","content":"av","api":"tv"}""";
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

        // id 经 ResourceIdJsonConverter 输出规范字符串，属性名随契约 camelCase
        Assert.Contains("\"id\":\"season2539\"", json);
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

    #region 任务回吐（PipelineSink）

    // 下载链路不再持有 DownloadTask，只通过回调回吐；这里锁住元数据与产物回调的映射
    [Fact]
    public void SinkFor_RoutesCallbacksIntoTask( )
    {
        var task = new DownloadTask(new ResourceId.Av(114514), "BV1xx411c7XD", 0);
        var sink = TaskWorker.SinkFor(task);

        sink.Meta!(new VInfo { Title = "标题", Desc = "", Pic = "https://i0.hdslb.com/x.jpg", PubTime = 1700000000, PagesInfo = [] });
        sink.Saved!("D:/out/a.mp4");
        sink.Saved!("D:/out/b.mp4");

        Assert.Equal("标题", task.Title);
        Assert.Equal("https://i0.hdslb.com/x.jpg", task.Pic);
        Assert.Equal(1700000000, task.VideoPubTime);
        Assert.Equal(["D:/out/a.mp4", "D:/out/b.mp4"], task.SavePaths);
    }

    // CLI 走 default(PipelineSink)：全部回调为 null，下层的 ?.Invoke 必须能安全跳过
    [Fact]
    public void DefaultSink_HasNoCallbacks( )
    {
        PipelineSink sink = default;

        Assert.Null(sink.Meta);
        Assert.Null(sink.Saved);
    }

    #endregion
}
