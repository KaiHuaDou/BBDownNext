using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core.Download;
using BBDown.Core.Entity;
using BBDown.Core.Pipeline;
using BBDown.Core.Workflow;

namespace BBDown.Core.Tests;

public class DownloadPipelineTests
{
    [Fact]
    public void ComposeSink_WorkflowNull_ReturnsOriginal( )
    {
        var sink = new PipelineSink(null, null);

        var composed = DownloadPipeline.ComposeSink(sink, null);

        Assert.Equal(sink, composed);
    }

    [Fact]
    public async Task ComposeSink_WithWorkflow_MirrorsMetaAndSaved( )
    {
        var ctx = new ChannelWorkflowContext( );
        var savedPaths = new List<string>( );
        var composed = DownloadPipeline.ComposeSink(new PipelineSink(null, savedPaths.Add), ctx);

        composed.Meta!(new VInfo { Title = "标题", Desc = "", Pic = "", PubTime = 0, PagesInfo = [] });
        composed.Saved!("D:/out/a.mp4");

        var events = await ReadEventsAsync(ctx, 2, TestContext.Current.CancellationToken);
        Assert.Equal("任务信息：标题", Assert.IsType<MessageEvent>(events[0]).Text);
        Assert.Equal("已保存：D:/out/a.mp4", Assert.IsType<MessageEvent>(events[1]).Text);
        Assert.Equal(["D:/out/a.mp4"], savedPaths);
    }

    private static async Task<List<WorkflowEvent>> ReadEventsAsync(ChannelWorkflowContext ctx, int count, CancellationToken token)
    {
        var events = new List<WorkflowEvent>( );
        await foreach (var evt in ctx.ReadAllAsync(token))
        {
            events.Add(evt);
            if (events.Count == count)
            {
                break;
            }
        }

        return events;
    }
}
