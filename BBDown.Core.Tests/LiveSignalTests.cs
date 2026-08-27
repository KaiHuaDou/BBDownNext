using System.Threading;

namespace BBDown.Core.Tests;

[CollectionDefinition]
public sealed class LiveSignalCollectionDefinition;

/// <summary>
/// <see cref="LiveSignal"/> 持有进程级静态状态，测试必须串行；
/// 每个用例开始前强制摘除残留注册，避免上一个失败用例污染下一个。
/// </summary>
[Collection<LiveSignalCollectionDefinition>]
public class LiveSignalTests
{
    public LiveSignalTests( )
    {
        using var dummy = new CancellationTokenSource( );
        LiveSignal.Register("__dummy__", dummy).Dispose( );
    }

    // 非录制场景下 Ctrl+Break 必须回落到全局取消，否则用户按了没反应
    [Fact]
    public void TryRequestStop_WithoutRegistration_ReturnsFalse( )
    {
        Assert.False(LiveSignal.TryRequestStop("nope"));
    }

    [Fact]
    public void TryRequestStop_AfterRegister_CancelsToken( )
    {
        using var cts = new CancellationTokenSource( );
        using var scope = LiveSignal.Register("a", cts);

        Assert.True(LiveSignal.TryRequestStop("a"));
        Assert.True(cts.IsCancellationRequested);
    }

    // 二次 Ctrl+Break 要能穿透到全局取消（强制退出），所以第二次必须返回 false
    [Fact]
    public void TryRequestStop_Twice_SecondReturnsFalse( )
    {
        using var cts = new CancellationTokenSource( );
        using var scope = LiveSignal.Register("a", cts);

        Assert.True(LiveSignal.TryRequestStop("a"));
        Assert.False(LiveSignal.TryRequestStop("a"));
    }

    [Fact]
    public void TryRequestStop_AfterScopeDisposed_ReturnsFalse( )
    {
        using var cts = new CancellationTokenSource( );
        LiveSignal.Register("a", cts).Dispose( );

        Assert.False(LiveSignal.TryRequestStop("a"));
        Assert.False(cts.IsCancellationRequested);
    }

    // 录制收尾时 cts 可能先于信号到达被释放，此时 Cancel 会抛 ObjectDisposedException
    [Fact]
    public void TryRequestStop_OnDisposedSource_ReturnsFalse( )
    {
        var cts = new CancellationTokenSource( );
        using var scope = LiveSignal.Register("a", cts);
        cts.Dispose( );

        Assert.False(LiveSignal.TryRequestStop("a"));
    }

    // 并发录制：不同会话标识互不影响，各自可单独停止
    [Fact]
    public void TryRequestStop_StopsOnlyMatchingSession( )
    {
        using var first = new CancellationTokenSource( );
        using var second = new CancellationTokenSource( );
        using var firstScope = LiveSignal.Register("a", first);
        using var secondScope = LiveSignal.Register("b", second);

        Assert.True(LiveSignal.TryRequestStop("a"));
        Assert.True(first.IsCancellationRequested);
        Assert.False(second.IsCancellationRequested);

        Assert.True(LiveSignal.TryRequestStop("b"));
        Assert.True(second.IsCancellationRequested);
    }

    // 先注册者后释放，不能把另一会话的挂载一并摘掉
    [Fact]
    public void DisposingScope_DoesNotDetachOtherSession( )
    {
        using var first = new CancellationTokenSource( );
        using var second = new CancellationTokenSource( );
        var firstScope = LiveSignal.Register("a", first);
        using var secondScope = LiveSignal.Register("b", second);

        firstScope.Dispose( );

        Assert.True(LiveSignal.TryRequestStop("b"));
        Assert.True(second.IsCancellationRequested);
    }

    [Fact]
    public void Register_NullSource_Throws( )
    {
        Assert.Throws<System.ArgumentNullException>(( ) => LiveSignal.Register("a", null!));
    }
}
