namespace BBDown.Core.Tests;

/// <summary>
/// <see cref="DownloadUtil.ReadState"/> 把成品与 aria2c 控制文件的组合收成一个判据：
/// 只有成品可跳过，成品与控制文件并存时按空壳处理
/// </summary>
public class DownloadUtilTests
{
    [Fact]
    public void ReadState_NoFiles_ReturnsMissing( )
    {
        Assert.Equal(DownloadUtil.TrackState.Missing, DownloadUtil.ReadState(false, false));
    }

    [Fact]
    public void ReadState_ControlOnly_ReturnsAriaPartial( )
    {
        Assert.Equal(DownloadUtil.TrackState.AriaPartial, DownloadUtil.ReadState(false, true));
    }

    [Fact]
    public void ReadState_FinalOnly_ReturnsComplete( )
    {
        Assert.Equal(DownloadUtil.TrackState.Complete, DownloadUtil.ReadState(true, false));
    }

    [Fact]
    public void ReadState_FinalWithControl_ReturnsAriaStale( )
    {
        Assert.Equal(DownloadUtil.TrackState.AriaStale, DownloadUtil.ReadState(true, true));
    }
}
