using System;
using System.Linq;
using System.Threading.Tasks;

namespace BBDown.GUI.Tests;

public class QueueRunnerTests
{
    [Theory]
    [InlineData("BV1xx", TaskKind.Video)]
    [InlineData("https://live.bilibili.com/123456", TaskKind.Live)]
    [InlineData("cv1234567", TaskKind.Opus)]
    [InlineData("https://www.bilibili.com/opus/123456", TaskKind.Opus)]
    public void Enqueue_DetectsKind(string url, TaskKind expected)
    {
        var queue = new QueueRunner(a => a( ));
        queue.Enqueue(new TaskParams( ), url);

        Assert.Equal(expected, queue.All.Single( ).Kind);
    }

    [Fact]
    public void Remove_WaitingTask_RemovesIt( )
    {
        var queue = new QueueRunner(a => a( ));
        queue.Enqueue(new TaskParams( ), "BV1xx");
        var task = queue.All.Single( );

        Assert.True(queue.Remove(task));
        Assert.Empty(queue.All);
    }

    [Fact]
    public void ClearFinished_EmptyQueue_NoOp( )
    {
        var queue = new QueueRunner(a => a( ));

        queue.ClearFinished( );

        Assert.Empty(queue.All);
    }

    [Fact]
    public async Task RunNow_Success_MarksSuccess( )
    {
        var gate = new object( );
        var queue = new QueueRunner(a => { lock (gate) a( ); });
        queue.Executor = static (_, _) => Task.FromResult(0);
        queue.RunNow(new TaskParams( ), "BV1xx");

        var task = await WaitUntilFinishedAsync(queue, gate);

        Assert.Equal(TaskStatus.Success, task.Status);
    }

    [Fact]
    public async Task RunNow_NonZeroExit_MarksFailed( )
    {
        var gate = new object( );
        var queue = new QueueRunner(a => { lock (gate) a( ); });
        queue.Executor = static (_, _) => Task.FromResult(1);
        queue.RunNow(new TaskParams( ), "BV1xx");

        var task = await WaitUntilFinishedAsync(queue, gate);

        Assert.Equal(TaskStatus.Failed, task.Status);
    }

    private static async Task<TaskState> WaitUntilFinishedAsync(QueueRunner queue, object gate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            TaskState? finished;
            lock (gate)
            {
                finished = queue.All.FirstOrDefault(t => t.Status is TaskStatus.Success or TaskStatus.Failed or TaskStatus.Cancelled);
            }

            if (finished is not null)
            {
                return finished;
            }

            await Task.Delay(20);
        }

        throw new TimeoutException("任务未在超时前完成");
    }
}
