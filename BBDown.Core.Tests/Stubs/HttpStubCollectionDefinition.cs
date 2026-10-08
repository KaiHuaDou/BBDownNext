namespace BBDown.Core.Tests;

/// <summary>
/// 替换进程级静态 <c>HTTPUtil.AppHttpClient</c> 的测试必须挂此集合
/// 并行的桩会在各自的 finally 里把对方装上的客户端还原掉
/// 只替换其它静态（如 <c>DownloaderAdapter.HttpClientFactory</c>）的测试用各自独立的集合
/// </summary>
[CollectionDefinition]
public sealed class HttpStubCollectionDefinition;
