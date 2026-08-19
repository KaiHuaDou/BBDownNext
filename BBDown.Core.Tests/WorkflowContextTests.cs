using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core.Logging;
using BBDown.Core.Workflow;

namespace BBDown.Core.Tests;

public class WorkflowContextTests
{
    [Theory]
    [InlineData("hello")]
    [InlineData("")]
    public void MessageEvent_JsonRoundTrip_PreservesTypeAndText(string text)
    {
        var json = JsonSerializer.Serialize(new MessageEvent(text, DateTimeOffset.UnixEpoch), WorkflowJsonSerializerContext.Default.WorkflowEvent);
        Assert.Contains("\"type\":\"message\"", json);

        var back = JsonSerializer.Deserialize<WorkflowEvent>(json, WorkflowJsonSerializerContext.Default.WorkflowEvent);
        var message = Assert.IsType<MessageEvent>(back);
        Assert.Equal(text, message.Text);
        Assert.Equal(DateTimeOffset.UnixEpoch, message.Time);
    }

    [Fact]
    public void OptionRequestEvent_JsonRoundTrip_PreservesRequestIdAndOptions( )
    {
        var evt = new OptionRequestEvent(Guid.Parse("11111111-2222-3333-4444-555555555555"), "提示", ["a", "b"], DateTimeOffset.UnixEpoch);
        var json = JsonSerializer.Serialize<WorkflowEvent>(evt, WorkflowJsonSerializerContext.Default.WorkflowEvent);

        var back = JsonSerializer.Deserialize<WorkflowEvent>(json, WorkflowJsonSerializerContext.Default.WorkflowEvent);
        var option = Assert.IsType<OptionRequestEvent>(back);
        Assert.Equal(evt.RequestId, option.RequestId);
        Assert.Equal(evt.Options, option.Options);
        Assert.Equal(evt.Deadline, option.Deadline);
    }

    [Fact]
    public async Task ReadAllAsync_DeliversMessagesInOrder( )
    {
        var ctx = new ChannelWorkflowContext( );
        ctx.EnqueueMessage("a", DateTimeOffset.UnixEpoch);
        ctx.EnqueueMessage("b", DateTimeOffset.UnixEpoch);
        ctx.EnqueueMessage("c", DateTimeOffset.UnixEpoch);

        var texts = new List<string>( );
        await foreach (var evt in ctx.ReadAllAsync(TestContext.Current.CancellationToken))
        {
            if (evt is MessageEvent message)
            {
                texts.Add(message.Text);
            }

            if (texts.Count == 3)
            {
                break;
            }
        }

        Assert.Equal(["a", "b", "c"], texts);
    }

    [Fact]
    public async Task AskOptionAsync_SubmitValidChoice_ReturnsChoice( )
    {
        var ctx = new ChannelWorkflowContext( );
        var task = ctx.AskOptionAsync("选择", ["继续", "退出"], TestContext.Current.CancellationToken);
        var request = await ReadSingleOptionAsync(ctx, TestContext.Current.CancellationToken);

        Assert.True(ctx.SubmitChoice(request.RequestId, "继续"));
        Assert.Equal("继续", await task);
    }

    [Fact]
    public async Task AskOptionAsync_InvalidChoice_StaysPending( )
    {
        var ctx = new ChannelWorkflowContext( );
        var task = ctx.AskOptionAsync("选择", ["继续"], TestContext.Current.CancellationToken);
        var request = await ReadSingleOptionAsync(ctx, TestContext.Current.CancellationToken);

        Assert.False(ctx.SubmitChoice(request.RequestId, "不存在的选项"));
        Assert.False(task.IsCompleted);
    }

    [Fact]
    public void SubmitChoice_UnknownRequestId_ReturnsFalse( )
    {
        var ctx = new ChannelWorkflowContext( );
        Assert.False(ctx.SubmitChoice(Guid.NewGuid( ), "任意"));
    }

    [Fact]
    public async Task SubmitChoice_SecondAnswer_ReturnsFalse( )
    {
        var ctx = new ChannelWorkflowContext( );
        var task = ctx.AskOptionAsync("选择", ["继续"], TestContext.Current.CancellationToken);
        var request = await ReadSingleOptionAsync(ctx, TestContext.Current.CancellationToken);

        Assert.True(ctx.SubmitChoice(request.RequestId, "继续"));
        Assert.False(ctx.SubmitChoice(request.RequestId, "继续"));
        Assert.Equal("继续", await task);
    }

    [Fact]
    public async Task AskOptionAsync_Cancelled_ThrowsOperationCanceled( )
    {
        var ctx = new ChannelWorkflowContext( );
        using var tokenSource = new CancellationTokenSource( );
        var task = ctx.AskOptionAsync("选择", ["继续"], tokenSource.Token);
        await tokenSource.CancelAsync( );

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async ( ) => await task);
    }

    [Fact]
    public async Task AskOptionAsync_Timeout_ThrowsTimeoutException( )
    {
        var ctx = new ChannelWorkflowContext(TimeSpan.FromMilliseconds(50));
        var task = ctx.AskOptionAsync("选择", ["继续"], TestContext.Current.CancellationToken);

        await Assert.ThrowsAnyAsync<TimeoutException>(async ( ) => await task);
    }

    [Fact]
    public async Task CancelPendingChoices_UnblocksAllPending( )
    {
        var ctx = new ChannelWorkflowContext( );
        var first = ctx.AskOptionAsync("选择", ["继续"], TestContext.Current.CancellationToken);
        var second = ctx.AskOptionAsync("选择", ["继续"], TestContext.Current.CancellationToken);

        ctx.CancelPendingChoices( );

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async ( ) => await first);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async ( ) => await second);
    }

    [Fact]
    public async Task AskOptionAsync_SurvivesProgressReport( )
    {
        // 挂起期间进度继续上报（经 ProgressBus）：不干扰选项等待
        var ctx = new ChannelWorkflowContext( );
        var task = ctx.AskOptionAsync("选择", ["继续"], TestContext.Current.CancellationToken);
        using (MessageBus.BeginScope("test-option"))
        {
            using (ProgressBus.BeginStage("下载"))
            {
                ProgressBus.Publish(0.3, 5, 1);
            }
        }

        var request = await ReadSingleOptionAsync(ctx, TestContext.Current.CancellationToken);

        Assert.True(ctx.SubmitChoice(request.RequestId, "继续"));
        Assert.Equal("继续", await task);
    }

    private static async Task<OptionRequestEvent> ReadSingleOptionAsync(ChannelWorkflowContext ctx, CancellationToken token)
    {
        await foreach (var evt in ctx.ReadAllAsync(token))
        {
            if (evt is OptionRequestEvent option)
            {
                return option;
            }
        }

        throw new InvalidOperationException("未收到选项请求事件");
    }
}
