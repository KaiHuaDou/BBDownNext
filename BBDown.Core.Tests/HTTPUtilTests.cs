using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace BBDown.Core.Tests;

// 替换进程级静态 AppHttpClient，必须与其它 AppHttpClient 桩测试同集合串行
[Collection<HttpStubCollectionDefinition>]
public class HTTPUtilTests
{
    [Theory]
    [InlineData("", "")]
    [InlineData("短响应", "短响应")]
    public void TruncateForLog_ShortText_KeptAsIs(string text, string expected)
    {
        Assert.Equal(expected, HTTPUtil.TruncateForLog(text));
    }

    [Fact]
    public void TruncateForLog_ExactlyAtLimit_KeptAsIs( )
    {
        Assert.Equal(8192, HTTPUtil.TruncateForLog(new string('a', 8192)).Length);
    }

    // 截断必须自述原文长度：只看到前 8KB 时无法判断丢了多少
    [Fact]
    public void TruncateForLog_LongText_KeepsHeadAndReportsTotalLength( )
    {
        var logged = HTTPUtil.TruncateForLog(new string('a', 82256));

        Assert.StartsWith(new string('a', 8192), logged);
        Assert.EndsWith("…（已截断，共 82256 字符）", logged);
    }

    // 默认只接受无错误的证书：本集合已串行，此处临时改环境变量不会影响其它桩测试的 TLS 判定
    [Theory]
    [InlineData(System.Net.Security.SslPolicyErrors.None, true)]
    [InlineData(System.Net.Security.SslPolicyErrors.RemoteCertificateNameMismatch, false)]
    [InlineData(System.Net.Security.SslPolicyErrors.RemoteCertificateChainErrors, false)]
    public void IsTlsAcceptable_WithoutInsecureEnv_AcceptsOnlyCleanCertificates(
        System.Net.Security.SslPolicyErrors errors, bool expected)
    {
        var restored = Environment.GetEnvironmentVariable("BBDOWN_INSECURE_TLS");
        Environment.SetEnvironmentVariable("BBDOWN_INSECURE_TLS", null);
        try
        {
            Assert.Equal(expected, HTTPUtil.IsTlsAcceptable(errors));
        }
        finally
        {
            Environment.SetEnvironmentVariable("BBDOWN_INSECURE_TLS", restored);
        }
    }

    // BBDOWN_INSECURE_TLS=1 放行自签 / 中间人（抓包调试用），四类出站 client 共用此判定
    [Theory]
    [InlineData(System.Net.Security.SslPolicyErrors.None)]
    [InlineData(System.Net.Security.SslPolicyErrors.RemoteCertificateChainErrors)]
    public void IsTlsAcceptable_WithInsecureEnv_AcceptsAnything(
        System.Net.Security.SslPolicyErrors errors)
    {
        var restored = Environment.GetEnvironmentVariable("BBDOWN_INSECURE_TLS");
        Environment.SetEnvironmentVariable("BBDOWN_INSECURE_TLS", "1");
        try
        {
            Assert.True(HTTPUtil.IsTlsAcceptable(errors));
        }
        finally
        {
            Environment.SetEnvironmentVariable("BBDOWN_INSECURE_TLS", restored);
        }
    }

    // AppHttpClient 已关闭自动重定向（凭据门须逐跳过门），探测方法必须自己走完链路
    [Fact]
    public async Task GetWebLocationAsync_FollowsRedirectChainManually( )
    {
        var next = new Dictionary<string, string>
        {
            ["https://b23.tv/short"] = "https://b23.tv/mid",
            ["https://b23.tv/mid"] = "https://b23.tv/long",
        };
        var requested = new List<string>( );

        var final = await HttpStub.WithResponder(request =>
        {
            var uri = request.RequestUri!.AbsoluteUri;
            requested.Add(uri);
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            if (next.TryGetValue(uri, out var location))
            {
                response.StatusCode = HttpStatusCode.Found;
                response.Headers.Location = new Uri(location);
            }

            return response;
        }, ( ) => HTTPUtil.GetWebLocationAsync("https://b23.tv/short", TestContext.Current.CancellationToken));

        Assert.Equal("https://b23.tv/long", final);
        Assert.Equal(["https://b23.tv/short", "https://b23.tv/mid", "https://b23.tv/long"], [.. requested]);
    }

    [Fact]
    public async Task GetWebLocationAsync_EndlessRedirects_ThrowsAtHopLimit( )
    {
        var count = 0;

        var error = await Assert.ThrowsAsync<InvalidOperationException>(( ) => HttpStub.WithResponder(_ =>
        {
            count++;
            return new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = new Uri("https://b23.tv/loop") } };
        }, ( ) => HTTPUtil.GetWebLocationAsync("https://b23.tv/loop", TestContext.Current.CancellationToken)));

        Assert.Contains("重定向", error.Message);
        Assert.Equal(HttpTransfer.MaxRedirectHops + 1, count);
    }
}
