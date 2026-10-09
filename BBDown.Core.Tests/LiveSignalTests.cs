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

    // 短号与长号是同一房间的两种写法，只按输入串占位会被别名绕过互斥
    [Fact]
    public void Register_AliasOccupied_Throws( )
    {
        using var first = new CancellationTokenSource( );
        using var second = new CancellationTokenSource( );
        using var scope = LiveSignal.Register("live123", first, "live456");

        Assert.Throws<InvalidOperationException>(( ) => LiveSignal.Register("live456", second));
        Assert.True(LiveSignal.TryRequestStop("live123"));
        Assert.True(first.IsCancellationRequested);
    }

    // 别名占位失败时本次已占的键一并回滚，否则该别名会被永久占住
    [Fact]
    public void Register_AliasConflict_RollsBackPrimary( )
    {
        using var holder = new CancellationTokenSource( );
        using var blocked = new CancellationTokenSource( );
        using var held = LiveSignal.Register("live-blocked", holder);

        Assert.Throws<InvalidOperationException>(( ) => LiveSignal.Register("live-fresh", blocked, "live-blocked"));

        // 回滚生效：主键槽位已被摘除，本次的停止源不该收到取消
        Assert.False(LiveSignal.TryRequestStop("live-fresh"));
        Assert.False(blocked.IsCancellationRequested);
    }

    // 别名与主键相同时不重复占位，否则第二次注册会与自己冲突
    [Fact]
    public void Register_AliasSameAsPrimary_Succeeds( )
    {
        using var cts = new CancellationTokenSource( );
        using var scope = LiveSignal.Register("live-same", cts, "live-same");

        Assert.True(LiveSignal.TryRequestStop("live-same"));
        Assert.True(cts.IsCancellationRequested);
    }

    [Fact]
    public void Register_NullSource_Throws( )
    {
        Assert.Throws<ArgumentNullException>(( ) => LiveSignal.Register("null-source", null!));
    }
}
