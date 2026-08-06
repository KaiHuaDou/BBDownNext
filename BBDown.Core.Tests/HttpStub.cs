using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core.Util;

namespace BBDown.Core.Tests;

// HTTP 桩测试必须串行：它们会替换进程级静态 AppHttpClient，并行会互相踩踏
[CollectionDefinition]
public sealed class HttpStubCollectionDefinition;

// 替换进程级静态 AppHttpClient 的 HTTP 桩，供各 Fetcher 测试复用
public static class HttpStub
{
    public static Task<T> WithJsonResponse<T>(string body, Func<Task<T>> act)
    {
        return WithResponder(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        }, act);
    }

    public static async Task<T> WithResponder<T>(Func<HttpRequestMessage, HttpResponseMessage> responder, Func<Task<T>> act)
    {
        var original = HTTPUtil.AppHttpClient;
        using var handler = new StubHttpMessageHandler(responder);
        using var client = new HttpClient(handler, disposeHandler: false);
        HTTPUtil.AppHttpClient = client;
        try
        {
            return await act( );
        }
        finally
        {
            HTTPUtil.AppHttpClient = original;
        }
    }
}

// 按请求返回预设响应的 HttpMessageHandler
internal sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> responder = responder;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return Task.FromResult(responder(request));
    }
}
