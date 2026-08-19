using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

using BBDown.Core.Logging;
using BBDown.Core.Workflow;

namespace BBDown.Core.Tests;

/// <summary>
/// 进度总线测试：阶段边界事件、阶段内样本快照、阶段外忽略、重入语义、作用域路由、事件序列化。
/// </summary>
public class ProgressBusTests
{
    [Fact]
    public void ProgressSampleEvent_JsonRoundTrip_PreservesFields( )
    {
        var evt = new ProgressSampleEvent("av1", 0.5, 1024, 12.5, "详情");
        var json = JsonSerializer.Serialize<WorkflowEvent>(evt, WorkflowJsonSerializerContext.Default.WorkflowEvent);

        var back = JsonSerializer.Deserialize<WorkflowEvent>(json, WorkflowJsonSerializerContext.Default.WorkflowEvent);
        var sample = Assert.IsType<ProgressSampleEvent>(back);
        Assert.Equal("av1", sample.Scope);
        Assert.Equal(0.5, sample.Ratio);
        Assert.Equal(1024, sample.TotalBytes);
        Assert.Equal(12.5, sample.Speed);
        Assert.Equal("详情", sample.Detail);
    }

    [Fact]
    public void BeginStage_Dispose_EmitsStartAndEnd( )
    {
        var scopeId = Guid.NewGuid( ).ToString( );
        var events = new List<WorkflowEvent>( );
        Action<WorkflowEvent> handler = events.Add;
        ProgressBus.Subscribe(handler);
        try
        {
            using (MessageBus.BeginScope(scopeId))
            {
                using (ProgressBus.BeginStage("下载"))
                {
                }
            }

            Assert.Equal(2, events.Count);
            var start = Assert.IsType<ProgressRangeStartEvent>(events[0]);
            Assert.Equal(scopeId, start.Scope);
            Assert.Equal("下载", start.StageName);
            Assert.IsType<ProgressRangeEndEvent>(events[1]);
        }
        finally
        {
            ProgressBus.Unsubscribe(handler);
        }
    }

    [Fact]
    public void Publish_WithinStage_UpdatesLatestAndClearsOnEnd( )
    {
        var scopeId = Guid.NewGuid( ).ToString( );
        using (MessageBus.BeginScope(scopeId))
        {
            using (var stage = ProgressBus.BeginStage("下载"))
            {
                ProgressBus.Publish(0.5, 1024, 12.5);
                ProgressBus.Publish(0.6, 512, 6.0);

                var state = ProgressBus.Latest(scopeId);
                Assert.Equal("下载", state!.StageName);
                Assert.Equal(0.6, state.Sample!.Ratio);
                // 增量语义：第二次上报后 TotalBytes 为两次增量之和（阶段内累计，多采样器并发互不覆盖）
                Assert.Equal(1536, state.Sample.TotalBytes);
                Assert.Equal(6.0, state.Sample.Speed);
            }

            // 阶段结束后状态清空
            Assert.Null(ProgressBus.Latest(scopeId));
        }
    }

    [Fact]
    public void Publish_OutsideStage_Ignored( )
    {
        var scopeId = Guid.NewGuid( ).ToString( );
        using (MessageBus.BeginScope(scopeId))
        {
            ProgressBus.Publish(0.5, 1024, 12.5);
        }

        Assert.Null(ProgressBus.Latest(scopeId));
    }

    [Fact]
    public void BeginStage_Reentry_EndsOldStageFirst( )
    {
        var scopeId = Guid.NewGuid( ).ToString( );
        var events = new List<WorkflowEvent>( );
        Action<WorkflowEvent> handler = events.Add;
        ProgressBus.Subscribe(handler);
        try
        {
            using (MessageBus.BeginScope(scopeId))
            {
                using (ProgressBus.BeginStage("下载"))
                {
                    using (ProgressBus.BeginStage("重下"))
                    {
                    }
                }
            }

            Assert.Equal(4, events.Count);
            Assert.IsType<ProgressRangeStartEvent>(events[0]);
            Assert.IsType<ProgressRangeEndEvent>(events[1]);
            Assert.IsType<ProgressRangeStartEvent>(events[2]);
            Assert.IsType<ProgressRangeEndEvent>(events[3]);
        }
        finally
        {
            ProgressBus.Unsubscribe(handler);
        }
    }

    [Fact]
    public async Task Publish_Concurrent_DoesNotThrow( )
    {
        var scopeId = Guid.NewGuid( ).ToString( );
        using (MessageBus.BeginScope(scopeId))
        {
            using (ProgressBus.BeginStage("下载"))
            {
                var jobs = Enumerable.Range(0, 4).Select(i => Task.Run(( ) =>
                {
                    for (var n = 0; n < 100; n++)
                    {
                        ProgressBus.Publish(n / 100.0, n, n);
                    }
                })).ToArray( );

                await Task.WhenAll(jobs);

                // 快照为最后一次样本，数值落在合法区间即可（并发交错下的最终值不确定）
                var sample = ProgressBus.Latest(scopeId)!.Sample;
                Assert.True(sample!.Ratio is >= 0 and <= 1);
            }
        }
    }
}
