using System;
using System.Globalization;

namespace BBDown.GUI.Tests;

public class StatusConvertersTests
{
    [Theory]
    [InlineData(TaskStatus.Running, null, true)]
    [InlineData(TaskStatus.Waiting, null, false)]
    [InlineData(TaskStatus.Waiting, "invert", true)]
    [InlineData(TaskStatus.Running, "invert", false)]
    [InlineData(TaskStatus.Failed, "retry", true)]
    [InlineData(TaskStatus.Cancelled, "retry", true)]
    [InlineData(TaskStatus.Success, "retry", false)]
    public void StatusToVisibility_ByParameter_ReturnsExpected(TaskStatus status, string? parameter, bool expected)
    {
        var converter = new StatusToVisibilityConverter( );

        Assert.Equal(expected, (bool) converter.Convert(status, typeof(bool), parameter, CultureInfo.InvariantCulture)!);
    }

    [Fact]
    public void StatusToBrush_AllStatuses_ReturnsNonNull( )
    {
        foreach (var status in Enum.GetValues<TaskStatus>( ))
        {
            Assert.NotNull(StatusToBrushConverter.StatusColor(status));
        }
    }

    [Fact]
    public void LiveStopVisibility_LiveRunning_Visible( )
    {
        var state = MakeState(TaskKind.Live, TaskStatus.Running);

        Assert.True((bool) new LiveStopVisibilityConverter( ).Convert(state, typeof(bool), null, CultureInfo.InvariantCulture)!);
    }

    [Fact]
    public void LiveStopVisibility_NonLive_NotVisible( )
    {
        var state = MakeState(TaskKind.Video, TaskStatus.Running);

        Assert.False((bool) new LiveStopVisibilityConverter( ).Convert(state, typeof(bool), null, CultureInfo.InvariantCulture)!);
    }

    private static TaskState MakeState(TaskKind kind, TaskStatus status)
    {
        return new TaskState { Params = new TaskParams( ), Url = "BV1xx", Kind = kind, Index = 1, Status = status };
    }
}
