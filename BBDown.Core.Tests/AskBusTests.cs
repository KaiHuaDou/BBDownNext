using System;
using System.Threading.Tasks;

using BBDown.Core.Logging;
using BBDown.Core.Workflow;

namespace BBDown.Core.Tests;

/// <summary>
/// 交互总线测试：无订阅者回落、结构化应答校验、二次应答、作用域取消。
/// </summary>
public class AskBusTests
{
    [Fact]
    public async Task Ask_NoSubscriber_ReturnsNullImmediately( )
    {
        var answer = await AskBus.Ask("选择", [new AskOption("a", "a")], token: TestContext.Current.CancellationToken);
        Assert.Null(answer);
    }

    [Fact]
    public async Task Ask_AnswerValidOption_ReturnsStructuredAnswer( )
    {
        OptionRequestEvent? request = null;
        void OnAsk(OptionRequestEvent evt) => request = evt;
        AskBus.Subscribe(OnAsk);
        try
        {
            var task = AskBus.Ask("选择", [new AskOption("继续", "继续"), new AskOption("退出", "退出")], token: TestContext.Current.CancellationToken);
            var evt = await WaitForAsync(( ) => request);

            Assert.True(AskBus.Answer(evt!.RequestId, new AskAnswer("继续", "ji xu")));

            var answer = await task;
            Assert.NotNull(answer);
            Assert.Equal("继续", answer!.OptionId);
            Assert.Equal("ji xu", answer.RawInput);
        }
        finally
        {
            AskBus.Unsubscribe(OnAsk);
        }
    }

    [Fact]
    public async Task Ask_AnswerInvalidOption_RejectedAndStaysPending( )
    {
        OptionRequestEvent? request = null;
        void OnAsk(OptionRequestEvent evt) => request = evt;
        AskBus.Subscribe(OnAsk);
        try
        {
            var task = AskBus.Ask("选择", [new AskOption("继续", "继续")], token: TestContext.Current.CancellationToken);
            var evt = await WaitForAsync(( ) => request);

            Assert.False(AskBus.Answer(evt!.RequestId, new AskAnswer("不存在的选项")));
            Assert.False(task.IsCompleted);

            // 清理：取消挂起提问，避免残留
            AskBus.CancelPending(evt.Scope);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async ( ) => await task);
        }
        finally
        {
            AskBus.Unsubscribe(OnAsk);
        }
    }

    [Fact]
    public void Answer_UnknownRequestId_ReturnsFalse( )
    {
        Assert.False(AskBus.Answer(Guid.NewGuid( ), new AskAnswer("a")));
    }

    [Fact]
    public async Task Answer_SecondAnswer_ReturnsFalse( )
    {
        OptionRequestEvent? request = null;
        void OnAsk(OptionRequestEvent evt) => request = evt;
        AskBus.Subscribe(OnAsk);
        try
        {
            var task = AskBus.Ask("选择", [new AskOption("继续", "继续")], token: TestContext.Current.CancellationToken);
            var evt = await WaitForAsync(( ) => request);

            Assert.True(AskBus.Answer(evt!.RequestId, new AskAnswer("继续")));
            Assert.False(AskBus.Answer(evt.RequestId, new AskAnswer("继续")));
            Assert.Equal("继续", (await task)!.OptionId);
        }
        finally
        {
            AskBus.Unsubscribe(OnAsk);
        }
    }

    [Fact]
    public async Task CancelPending_MatchingScope_Cancels( )
    {
        OptionRequestEvent? request = null;
        void OnAsk(OptionRequestEvent evt) => request = evt;
        AskBus.Subscribe(OnAsk);
        try
        {
            using (MessageBus.BeginScope("scope-x"))
            {
                var task = AskBus.Ask("选择", [new AskOption("a", "a")], token: TestContext.Current.CancellationToken);
                var evt = await WaitForAsync(( ) => request);
                Assert.Equal("scope-x", evt!.Scope);

                AskBus.CancelPending("scope-x");
                await Assert.ThrowsAnyAsync<OperationCanceledException>(async ( ) => await task);
            }
        }
        finally
        {
            AskBus.Unsubscribe(OnAsk);
        }
    }

    [Fact]
    public async Task CancelPending_OtherScope_Unaffected( )
    {
        OptionRequestEvent? request = null;
        void OnAsk(OptionRequestEvent evt) => request = evt;
        AskBus.Subscribe(OnAsk);
        try
        {
            using (MessageBus.BeginScope("scope-x"))
            {
                var task = AskBus.Ask("选择", [new AskOption("a", "a")], token: TestContext.Current.CancellationToken);
                var evt = await WaitForAsync(( ) => request);

                AskBus.CancelPending("scope-y");
                Assert.False(task.IsCompleted);

                AskBus.CancelPending("scope-x");
                await Assert.ThrowsAnyAsync<OperationCanceledException>(async ( ) => await task);
            }
        }
        finally
        {
            AskBus.Unsubscribe(OnAsk);
        }
    }

    private static async Task<T?> WaitForAsync<T>(Func<T?> get) where T : class
    {
        for (var i = 0; i < 100; i++)
        {
            if (get( ) is { } value)
            {
                return value;
            }

            await Task.Delay(10);
        }

        throw new InvalidOperationException("等待超时");
    }
}
