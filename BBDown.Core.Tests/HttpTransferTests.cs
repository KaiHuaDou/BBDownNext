using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BBDown.Core.Tests;

/// <summary>
/// 响应体读取上限与重定向逐跳跟随的测试。真实网络请求一律不测（见 AGENTS.md「测试范围约定」）
/// </summary>
public class HttpTransferTests
{
    [Fact]
    public async Task ReadBodyAsync_DecodesUtf8AndStripsBom( )
    {
        using var content = new ByteArrayContent([.. Encoding.UTF8.Preamble, .. Encoding.UTF8.GetBytes("中文")]);
        Assert.Equal("中文", await HttpTransfer.ReadBodyAsync(content, TestContext.Current.CancellationToken));
    }

    // 声明长度不可信（自动解压后常被移除），但明显超限时可先拒，省去读取
    [Fact]
    public async Task ReadBodyBytesAsync_DeclaredLengthOverLimit_Throws( )
    {
        using var content = new ByteArrayContent([]);
        content.Headers.ContentLength = 128L * 1024 * 1024;

        await Assert.ThrowsAsync<InvalidDataException>(( ) => HttpTransfer.ReadBodyBytesAsync(content, TestContext.Current.CancellationToken));
    }

    // 分块慢发不声明长度，只能靠逐块累计兜住
    [Fact]
    public async Task ReadBodyBytesAsync_EndlessStream_ThrowsOnceLimitExceeded( )
    {
        using var content = new ChunkedContent(null);

        await Assert.ThrowsAsync<InvalidDataException>(( ) => HttpTransfer.ReadBodyBytesAsync(content, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(HttpStatusCode.Moved, true)]
    [InlineData(HttpStatusCode.Found, true)]
    [InlineData(HttpStatusCode.SeeOther, true)]
    [InlineData(HttpStatusCode.TemporaryRedirect, true)]
    [InlineData(HttpStatusCode.PermanentRedirect, true)]
    [InlineData(HttpStatusCode.OK, false)]
    [InlineData(HttpStatusCode.NoContent, false)]
    [InlineData(HttpStatusCode.MultipleChoices, false)]
    [InlineData(HttpStatusCode.NotModified, false)]
    [InlineData(HttpStatusCode.BadRequest, false)]
    [InlineData(HttpStatusCode.NotFound, false)]
    public void IsRedirect_AcceptsOnlyServerRequestedContinuation(HttpStatusCode status, bool expected)
    {
        Assert.Equal(expected, HttpTransfer.IsRedirect(status));
    }

    [Fact]
    public async Task Send_NonRedirect_ReturnsResponseWithoutSecondRequest( )
    {
        var stub = new RedirectStub([(HttpStatusCode.OK, null)]);

        var response = await Send(stub.Respond( ));

        Assert.Same(stub.Returned[0], response);
        Assert.Equal(["https://api.bilibili.com/start"], stub.RequestedUris);
    }

    // 凭据门的保障在于每一跳：自动重定向会把 Cookie 原样带到目标，此处重定向目标必须再次过门
    [Fact]
    public async Task Send_RedirectToUntrustedHost_RejectedBeforeRequest( )
    {
        var stub = new RedirectStub(
            [(HttpStatusCode.Found, "https://evil.example.com/steal"), (HttpStatusCode.OK, null)]);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(( ) => Send(stub.Respond( )));

        Assert.Contains("evil.example.com", error.Message);
        // 第二跳在发出前就被凭据门拒绝，桩只收到首跳请求
        Assert.Equal(["https://api.bilibili.com/start"], stub.RequestedUris);
    }

    [Fact]
    public async Task Send_RedirectToTrustedHost_FollowsAndReturnsFinalResponse( )
    {
        var stub = new RedirectStub(
            [(HttpStatusCode.Found, "https://www.bilibili.com/landed"), (HttpStatusCode.OK, null)]);

        var response = await Send(stub.Respond( ));

        Assert.Same(stub.Returned[1], response);
        Assert.Equal(["https://api.bilibili.com/start", "https://www.bilibili.com/landed"], stub.RequestedUris);
    }

    [Fact]
    public async Task Send_RelativeLocation_ResolvedAgainstCurrentHop( )
    {
        var stub = new RedirectStub(
            [(HttpStatusCode.Found, "/x/v2/next"), (HttpStatusCode.OK, null)]);

        await Send(stub.Respond( ));

        Assert.Equal("https://api.bilibili.com/x/v2/next", stub.RequestedUris[1]);
    }

    // 与浏览器一致：303 表示资源已改用 GET 获取，方法必须切换，否则表单内容会被重放
    [Fact]
    public async Task Send_SeeOther_SwitchesMethodToGet( )
    {
        var stub = new RedirectStub(
            [(HttpStatusCode.SeeOther, "https://passport.bilibili.com/done"), (HttpStatusCode.OK, null)]);

        await Send(stub.Respond( ), HttpMethod.Post);

        Assert.Equal(HttpMethod.Post, stub.RequestedMethods[0]);
        Assert.Equal(HttpMethod.Get, stub.RequestedMethods[1]);
    }

    [Fact]
    public async Task Send_ExceedsMaxRedirectHops_Throws( )
    {
        var stub = new RedirectStub([.. Enumerable.Repeat((HttpStatusCode.Found, (string?)"https://api.bilibili.com/loop"), HttpTransfer.MaxRedirectHops + 2)]);

        await Assert.ThrowsAsync<InvalidOperationException>(( ) => Send(stub.Respond( )));

        Assert.Equal(HttpTransfer.MaxRedirectHops + 1, stub.RequestedUris.Count);
    }

    private static Task<HttpResponseMessage> Send(Func<HttpRequestMessage, HttpResponseMessage> responder, HttpMethod? method = null)
    {
        return HttpTransfer.SendTrustGatedAsync(
            "https://api.bilibili.com/start",
            method ?? HttpMethod.Get,
            (url, m) => new HttpRequestMessage(m, url),
            (request, _) => Task.FromResult(responder(request)),
            AppConfig.Empty,
            TestContext.Current.CancellationToken);
    }

    // 队列式桩：按请求到达顺序逐个消费预设响应，同时记录实际到达的 URI 与方法供断言
    private sealed class RedirectStub((HttpStatusCode Status, string? Location)[] hops)
    {
        private int index;

        public List<string> RequestedUris { get; } = [];
        public List<HttpMethod> RequestedMethods { get; } = [];
        public List<HttpResponseMessage> Returned { get; } = [];

        public Func<HttpRequestMessage, HttpResponseMessage> Respond( )
        {
            return request =>
            {
                RequestedUris.Add(request.RequestUri!.AbsoluteUri);
                RequestedMethods.Add(request.Method);
                var (status, location) = index < hops.Length ? hops[index] : (HttpStatusCode.OK, null);
                index++;
                var response = new HttpResponseMessage(status);
                if (location != null)
                {
                    response.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);
                }

                Returned.Add(response);
                return response;
            };
        }
    }
}
