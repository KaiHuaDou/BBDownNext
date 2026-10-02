using System;

namespace BBDown.Core.Fetcher;

/// <summary>
/// 输入的 EP/SS 不是一个可解析的番剧 (或海外番剧) 时由 BangumiInfoFetcher /
/// IntlBangumiInfoFetcher 抛出，供 FetcherRegistry 据此回退到课程 (cheese) 查找。
/// </summary>
public sealed class BangumiNotFoundException : Exception
{
    public BangumiNotFoundException( ) { }

    public BangumiNotFoundException(string message) : base(message) { }

    public BangumiNotFoundException(string message, Exception innerException) : base(message, innerException) { }
}
