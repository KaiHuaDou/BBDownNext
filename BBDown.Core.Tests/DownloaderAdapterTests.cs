using Downloader;

namespace BBDown.Core.Tests;

// IsDownloadSuccess 把 downloader 库「目标已存在即跳过」以 Failed 状态送达的隐式契约显式化，
// 此处锁定该契约：Completed 即成功；Failed 且文件仍在即视为成功（跳过已存在）；
// 其余（Failed 无文件 / 其它状态）一律失败。库升级若改变该行为，此测试会率先报错。
public class DownloaderAdapterTests
{
    [Fact]
    public void IsDownloadSuccess_Completed_ReturnsTrue( )
        => Assert.True(DownloaderAdapter.IsDownloadSuccess(DownloadStatus.Completed, false));

    [Fact]
    public void IsDownloadSuccess_FailedWithExistingFile_ReturnsTrue( )
        => Assert.True(DownloaderAdapter.IsDownloadSuccess(DownloadStatus.Failed, true));

    [Fact]
    public void IsDownloadSuccess_FailedWithoutFile_ReturnsFalse( )
        => Assert.False(DownloaderAdapter.IsDownloadSuccess(DownloadStatus.Failed, false));

    [Fact]
    public void IsDownloadSuccess_Running_ReturnsFalse( )
        => Assert.False(DownloaderAdapter.IsDownloadSuccess(DownloadStatus.Running, true));
}
