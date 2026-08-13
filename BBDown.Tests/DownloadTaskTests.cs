namespace BBDown.Tests;

public class DownloadTaskTests
{
    [Fact]
    public void ApplySample_KeepsLastSpeedWhenNothingArrived( )
    {
        var task = new DownloadTask(new ResourceId.Av(1), "https://example.com", 0);

        task.ApplySample(0.3, 2048);
        task.ApplySample(0.5, 0);

        Assert.Equal(0.5, task.Progress);
        Assert.Equal(2048, task.DownloadSpeed);
        Assert.Equal(2048, task.TotalDownloadedBytes);
    }

    [Fact]
    public void ApplySample_AccumulatesTotalBytes( )
    {
        var task = new DownloadTask(new ResourceId.Av(1), "https://example.com", 0);

        task.ApplySample(0.3, 2048);
        task.ApplySample(0.6, 1024);

        Assert.Equal(1024, task.DownloadSpeed);
        Assert.Equal(3072, task.TotalDownloadedBytes);
    }
}
