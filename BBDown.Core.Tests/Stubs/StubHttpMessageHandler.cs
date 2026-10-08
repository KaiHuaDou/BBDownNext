using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace BBDown.Core.Tests;

/// <summary>
/// 按请求返回响应的 <see cref="HttpMessageHandler"/>
/// 需要记录已发请求的用例在 responder 闭包里自行收集，无须在此留状态
/// </summary>
internal sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return Task.FromResult(responder(request));
    }
}
