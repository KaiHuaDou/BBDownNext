using System.Threading;

using BBDown.Live;

namespace BBDown.Tests;

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
        LiveSignal.Register(dummy).Dispose( );
    }

    // 非录制场景下 Ctrl+Break 必须回落到全局取消，否则用户按了没反应
    [Fact]
    public void TryRequestStop_WithoutRegistration_ReturnsFalse( )
    {
        Assert.False(LiveSignal.TryRequestStop( ));
    }

    [Fact]
    public void TryRequestStop_AfterRegister_CancelsToken( )
    {
        using var cts = new CancellationTokenSource( );
        using var scope = LiveSignal.Register(cts);

        Assert.True(LiveSignal.TryRequestStop( ));
        Assert.True(cts.IsCancellationRequested);
    }

    // 二次 Ctrl+Break 要能穿透到全局取消（强制退出），所以第二次必须返回 false
    [Fact]
    public void TryRequestStop_Twice_SecondReturnsFalse( )
    {
        using var cts = new CancellationTokenSource( );
        using var scope = LiveSignal.Register(cts);

        Assert.True(LiveSignal.TryRequestStop( ));
        Assert.False(LiveSignal.TryRequestStop( ));
    }

    [Fact]
    public void TryRequestStop_AfterScopeDisposed_ReturnsFalse( )
    {
        using var cts = new CancellationTokenSource( );
        LiveSignal.Register(cts).Dispose( );

        Assert.False(LiveSignal.TryRequestStop( ));
        Assert.False(cts.IsCancellationRequested);
    }

    // 录制收尾时 cts 可能先于信号到达被释放，此时 Cancel 会抛 ObjectDisposedException
    [Fact]
    public void TryRequestStop_OnDisposedSource_ReturnsFalse( )
    {
        var cts = new CancellationTokenSource( );
        using var scope = LiveSignal.Register(cts);
        cts.Dispose( );

        Assert.False(LiveSignal.TryRequestStop( ));
    }

    [Fact]
    public void Register_Second_OverridesFirst( )
    {
        using var first = new CancellationTokenSource( );
        using var second = new CancellationTokenSource( );
        using var firstScope = LiveSignal.Register(first);
        using var secondScope = LiveSignal.Register(second);

        Assert.True(LiveSignal.TryRequestStop( ));
        Assert.True(second.IsCancellationRequested);
        Assert.False(first.IsCancellationRequested);
    }

    // 先注册者后释放，不能把后注册者的挂载一并摘掉
    [Fact]
    public void DisposingStaleScope_DoesNotDetachCurrent( )
    {
        using var first = new CancellationTokenSource( );
        using var second = new CancellationTokenSource( );
        var firstScope = LiveSignal.Register(first);
        using var secondScope = LiveSignal.Register(second);

        firstScope.Dispose( );

        Assert.True(LiveSignal.TryRequestStop( ));
        Assert.True(second.IsCancellationRequested);
    }

    [Fact]
    public void Register_NullSource_Throws( )
    {
        Assert.Throws<System.ArgumentNullException>(( ) => LiveSignal.Register(null!));
    }
}
