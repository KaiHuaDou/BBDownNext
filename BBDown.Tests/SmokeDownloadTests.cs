using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BBDown.Tests;

// 真实下载冒烟测试：从候选池随机抽一个 BV，跑通「解析 → 选流 → HTTP 下载」端到端链路，
// 断言临时目录里产出了非空媒体文件。
//
// 默认跳过：设环境变量 BBDOWN_RUN_SMOKE=1 才真正执行，避免在正常 CI / 无网络环境误跑，
// 也避免频繁拉取真实视频占用带宽。需要可访问 bilibili 的网络；用 --skip-mux 跳过混流，
// 因此不依赖 ffmpeg/mp4box，下载产物直接落在临时目录便于断言。
public class SmokeDownloadTests
{
    [Fact]
    public async Task Download_RandomVideo_ProducesMediaFile( )
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BBDOWN_RUN_SMOKE")))
        {
            return;
        }

        var url = TestVideos.PickRandom( );
        var workDir = Path.Combine(Path.GetTempPath( ), "bbdown-smoke-" + Guid.NewGuid( ).ToString("N"));
        Directory.CreateDirectory(workDir);

        var option = new DownloadRequest
        {
            Url = url,
            WorkDir = workDir,
            SkipMux = true,
            NoCover = true,
            SingleThread = false,
            NoForceHttp = false,
            DfnPriority = "360P 流畅,480P 清晰,720P 高清",
        };

        try
        {
            var runConfig = WorkSetup.Build(option);
            var (effectiveReq, fetch) = await VideoInfo.FetchAsync(option, runConfig, CancellationToken.None);
            Assert.NotNull(fetch.VInfo);
            Assert.NotEmpty(fetch.VInfo.PagesInfo);

            await PageQueue.RunAsync(effectiveReq, runConfig, fetch, ct: CancellationToken.None);

            var mediaExtensions = new[] { ".mp4", ".m4a", ".m4s", ".flv", ".aac", ".mp3" };
            var downloaded = Directory.EnumerateFiles(workDir, "*", SearchOption.AllDirectories)
                .Where(f => mediaExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase)
                    && new FileInfo(f).Length > 0)
                .ToList( );

            Assert.NotEmpty(downloaded);
        }
        finally
        {
            TryDelete(workDir);
        }
    }

    private static void TryDelete(string dir)
    {
        try { Directory.Delete(dir, recursive: true); }
        catch { /* 失败时保留目录便于人工排查 */ }
    }
}
