using System;
using System.Threading.Tasks;

namespace BBDown.Core.Tests;

// 简写输入解析的畸形输入保护：前缀后跟非数字应落入统一的「输入有误」
// ArgumentException（或 BV 号的可读 InvalidOperationException）
// 这些用例都在触网前的纯解析阶段失败，可离线断言
public class InputResolverShorthandTests
{
    [Theory]
    [InlineData("avabc")]
    [InlineData("av")]
    [InlineData("epabc")]
    [InlineData("ep")]
    [InlineData("bv")]
    [InlineData("BV2abc")]
    [InlineData("space")]
    public async Task ResolveIdAsync_MalformedPrefix_ThrowsReadableError(string input)
    {
        await Assert.ThrowsAsync<ArgumentException>(async ( ) =>
            await InputResolver.ResolveIdAsync(input, AppConfig.Empty, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ResolveIdAsync_ShortBvBody_ThrowsReadableError( )
    {
        // "bv" + 不足 9 位：BV 转换器给出可读的长度错误，而非越界/空引用
        await Assert.ThrowsAsync<InvalidOperationException>(async ( ) =>
            await InputResolver.ResolveIdAsync("bv123", AppConfig.Empty, TestContext.Current.CancellationToken));
    }
}
