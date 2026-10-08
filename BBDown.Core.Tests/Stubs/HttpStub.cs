using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace BBDown.Core.Tests;

/// <summary>
/// 替换进程级静态 <c>HTTPUtil.AppHttpClient</c> 的唯一入口
/// 每个方法都在 <see cref="Install{T}"/> 内完成「保存原值 → 装桩 → 还原」
/// 用例不直接改这个静态
/// </summary>
/// <remarks>
/// 调用方必须挂 <see cref="HttpStubCollectionDefinition"/>：静态是进程级的
/// 并行的桩会互相还原对方的客户端
/// </remarks>
internal static class HttpStub
{
    /// <summary>构造 200 + <c>application/json</c> 的响应，供 responder 里直接取用。</summary>
    public static HttpResponseMessage Json(string body)
    {
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    public static Task<T> WithJsonResponse<T>(string body, Func<Task<T>> act)
    {
        return WithResponder(_ => Json(body), act);
    }

    public static Task<T> WithResponder<T>(Func<HttpRequestMessage, HttpResponseMessage> responder, Func<Task<T>> act)
    {
        return Install(new StubHttpMessageHandler(responder), act);
    }

    /// <summary>按请求的绝对地址挑响应体，用于同一桩要服务多个接口的情形。</summary>
    public static Task<T> WithRoute<T>(Func<string, string> bodyForUrl, Func<Task<T>> act)
    {
        return WithResponder(request => Json(bodyForUrl(request.RequestUri!.AbsoluteUri)), act);
    }

    /// <summary>按调用顺序逐个消费预设响应体，用于翻页；末项之后重复末项。</summary>
    public static Task<T> WithJsonResponses<T>(IReadOnlyList<string> bodies, Func<Task<T>> act)
    {
        var script = new Script<string>([.. bodies]);
        return WithResponder(_ => Json(script.Next( )), act);
    }

    /// <summary>
    /// 恒回 200 并把请求原样回填到响应上：<c>FixAvidAsync</c> 之类的探测据此读取最终地址
    /// 不重定向即等于原地址
    /// </summary>
    public static Task<T> WithOkEcho<T>(Func<Task<T>> act)
    {
        return WithResponder(request => new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request }, act);
    }

    private static async Task<T> Install<T>(HttpMessageHandler handler, Func<Task<T>> act)
    {
        var original = HTTPUtil.AppHttpClient;
        // handler 的所有权归本方法：HttpClient 以 disposeHandler: false 构造，不会替调用链释放
        using (handler)
        {
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
}
