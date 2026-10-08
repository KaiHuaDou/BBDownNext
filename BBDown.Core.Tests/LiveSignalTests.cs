using System;
using System.Threading;

namespace BBDown.Core.Tests;

/// <summary>
/// <see cref="LiveSignal"/> 按直播间持有进程级注册表。各用例使用互不相同的标识
/// 残留注册影响不到其它用例，因此无需串行集合
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

    // 同房间并发录制必须被拒：覆盖注册会让先注册者的摘除比较失败，槽位留成第二次的悬空挂载
    [Fact]
    public void Register_DuplicateSession_Throws( )
    {
        using var first = new CancellationTokenSource( );
        using var second = new CancellationTokenSource( );
        using var scope = LiveSignal.Register("same-room", first);

        Assert.Throws<InvalidOperationException>(( ) => LiveSignal.Register("same-room", second));

        // 被拒的第二次不占槽位，先注册者仍能正常停录
        Assert.True(LiveSignal.TryRequestStop("same-room"));
        Assert.True(first.IsCancellationRequested);
        Assert.False(second.IsCancellationRequested);
    }

    // 拒绝路径不写入槽位：第二次注册失败后，原会话释放即可让同房间重新可录
    [Fact]
    public void Register_DuplicateThenRelease_AllowsReregistration( )
    {
        using var first = new CancellationTokenSource( );
        using var second = new CancellationTokenSource( );
        var scope = LiveSignal.Register("recycled-room", first);
        Assert.Throws<InvalidOperationException>(( ) => LiveSignal.Register("recycled-room", second));
        scope.Dispose( );

        using var nextScope = LiveSignal.Register("recycled-room", second);
        Assert.True(LiveSignal.TryRequestStop("recycled-room"));
        Assert.True(second.IsCancellationRequested);
    }

    [Fact]
    public void Register_NullSource_Throws( )
    {
        Assert.Throws<ArgumentNullException>(( ) => LiveSignal.Register("null-source", null!));
    }
}
