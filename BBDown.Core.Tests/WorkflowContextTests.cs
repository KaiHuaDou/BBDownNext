using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

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
    public void OptionRequestEvent_JsonRoundTrip_PreservesScopeAndStructuredOptions( )
    {
        var options = new[] { new AskOption("y", "y"), new AskOption("n", "n") };
        var evt = new OptionRequestEvent(Guid.Parse("11111111-2222-3333-4444-555555555555"), "scope-1", "提示", options, DateTimeOffset.UnixEpoch, "n");
        var json = JsonSerializer.Serialize<WorkflowEvent>(evt, WorkflowJsonSerializerContext.Default.WorkflowEvent);

        var back = JsonSerializer.Deserialize<WorkflowEvent>(json, WorkflowJsonSerializerContext.Default.WorkflowEvent);
        var option = Assert.IsType<OptionRequestEvent>(back);
        Assert.Equal(evt.RequestId, option.RequestId);
        Assert.Equal("scope-1", option.Scope);
        Assert.Equal(options, option.Options);
        Assert.Equal("n", option.DefaultOptionId);
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
}
