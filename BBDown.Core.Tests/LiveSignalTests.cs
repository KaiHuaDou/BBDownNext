using System;
using System.Threading;

namespace BBDown.Core.Tests;

/// <summary>
/// <see cref="LiveSignal"/> 按会话标识持有进程级注册表。各用例使用互不相同的标识，
/// 残留注册影响不到其它用例，因此无需串行集合。
/// </summary>
public class LiveSignalTests
{
    // 非录制场景下 Ctrl+Break 必须回落到全局取消，否则用户按了没反应
    [Fact]
    public void TryRequestStop_WithoutRegistration_ReturnsFalse( )
    {
        Assert.False(LiveSignal.TryRequestStop("unregistered"));
    }

    [Fact]
    public void TryRequestStop_AfterRegister_CancelsToken( )
    {
        using var cts = new CancellationTokenSource( );
        using var scope = LiveSignal.Register("cancel", cts);

        Assert.True(LiveSignal.TryRequestStop("cancel"));
        Assert.True(cts.IsCancellationRequested);
    }

    // 二次 Ctrl+Break 要能穿透到全局取消（强制退出），所以第二次必须返回 false
    [Fact]
    public void TryRequestStop_Twice_SecondReturnsFalse( )
    {
        using var cts = new CancellationTokenSource( );
        using var scope = LiveSignal.Register("twice", cts);

        Assert.True(LiveSignal.TryRequestStop("twice"));
        Assert.False(LiveSignal.TryRequestStop("twice"));
    }

    [Fact]
    public void TryRequestStop_AfterScopeDisposed_ReturnsFalse( )
    {
        using var cts = new CancellationTokenSource( );
        LiveSignal.Register("disposed-scope", cts).Dispose( );

        Assert.False(LiveSignal.TryRequestStop("disposed-scope"));
        Assert.False(cts.IsCancellationRequested);
    }

    // 录制收尾时 cts 可能先于信号到达被释放，此时 Cancel 会抛 ObjectDisposedException
    [Fact]
    public void TryRequestStop_OnDisposedSource_ReturnsFalse( )
    {
        var cts = new CancellationTokenSource( );
        using var scope = LiveSignal.Register("disposed-source", cts);
        cts.Dispose( );

        Assert.False(LiveSignal.TryRequestStop("disposed-source"));
    }

    // 并发录制：不同会话标识互不影响，各自可单独停止
    [Fact]
    public void TryRequestStop_StopsOnlyMatchingSession( )
    {
        using var first = new CancellationTokenSource( );
        using var second = new CancellationTokenSource( );
        using var firstScope = LiveSignal.Register("session-a", first);
        using var secondScope = LiveSignal.Register("session-b", second);

        Assert.True(LiveSignal.TryRequestStop("session-a"));
        Assert.True(first.IsCancellationRequested);
        Assert.False(second.IsCancellationRequested);

        Assert.True(LiveSignal.TryRequestStop("session-b"));
        Assert.True(second.IsCancellationRequested);
    }

    // 先注册者后释放，不能把另一会话的挂载一并摘掉
    [Fact]
    public void DisposingScope_DoesNotDetachOtherSession( )
    {
        using var first = new CancellationTokenSource( );
        using var second = new CancellationTokenSource( );
        var firstScope = LiveSignal.Register("holder-a", first);
        using var secondScope = LiveSignal.Register("holder-b", second);

        firstScope.Dispose( );

        Assert.True(LiveSignal.TryRequestStop("holder-b"));
        Assert.True(second.IsCancellationRequested);
    }

    [Fact]
    public void Register_NullSource_Throws( )
    {
        Assert.Throws<ArgumentNullException>(( ) => LiveSignal.Register("null-source", null!));
    }
}
