using System.Threading.Tasks;

using BBDown.Core.Download;

namespace BBDown.Core.Tests;

public class PostProcessClientTests
{
    // 未配置后处理（空路径）直接返回 false，不启动任何进程、不落请求文件
    [Fact]
    public async Task TryProcessAsync_EmptyExe_ReturnsFalse( )
    {
        var result = await PostProcessClient.TryProcessAsync(
            "", "aid", "cid", "video", "track.mp4", "track.out.mp4", "ffmpeg",
            TestContext.Current.CancellationToken);
        Assert.False(result);
    }
}
