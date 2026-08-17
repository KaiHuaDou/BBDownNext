using System;

namespace BBDown.Core.Fetcher;

/// <summary>
/// 输入的 EP/SS 不是一个可解析的番剧 (或海外番剧) 时由 BangumiInfoFetcher /
/// IntlBangumiInfoFetcher 抛出，供 FetcherRegistry 据此回退到课程 (cheese) 查找。
/// 用语义化异常取代原先"catch KeyNotFoundException 并比对 .NET 内部资源串"的脆弱写法。
/// </summary>
public sealed class BangumiNotFoundException : Exception
{
    public BangumiNotFoundException( ) { }

    public BangumiNotFoundException(string message) : base(message) { }

    public BangumiNotFoundException(string message, Exception innerException) : base(message, innerException) { }
}
