using System.Threading.Tasks;

namespace BBDown.Tests;

public class OpusCommandTests
{
    private static async Task<DownloadRequest> ParseOpusAsync(params string[] args)
    {
        DownloadRequest? captured = null;
        var opus = CommandLineInvoker.GetOpusCommand(o =>
        {
            captured = o;
            return Task.FromResult(0);
        });

        // 子命令实际是挂在根命令下被解析的，测试里也照样组装，避免只测了脱离上下文的分支
        var root = CommandLineInvoker.GetRootCommand(_ => Task.FromResult(0));
        root.Subcommands.Add(opus);

        await root.Parse(args).InvokeAsync( );
        Assert.NotNull(captured);
        return captured!;
    }

    [Fact]
    public async Task OpusCommand_PassesInputAsUrl( )
    {
        var opt = await ParseOpusAsync("opus", "1230485246732926996");
        Assert.Equal("1230485246732926996", opt.Url);
    }

    [Fact]
    public async Task OpusCommand_AcceptsCvIdAndUrl( )
    {
        Assert.Equal("cv51908655", (await ParseOpusAsync("opus", "cv51908655")).Url);
        Assert.Equal(
            "https://www.bilibili.com/opus/1230485246732926996",
            (await ParseOpusAsync("opus", "https://www.bilibili.com/opus/1230485246732926996")).Url);
    }

    [Fact]
    public async Task OpusCommand_NoImages_DefaultsToFalse( )
    {
        Assert.False((await ParseOpusAsync("opus", "cv1")).NoImages);
        Assert.True((await ParseOpusAsync("opus", "cv1", "--no-images")).NoImages);
    }

    // opus 侧的 --no-metadata 指的是 YAML front matter，语义与视频侧不同但落在同一字段上
    [Fact]
    public async Task OpusCommand_NoMetadata_MapsToSameField( )
    {
        Assert.False((await ParseOpusAsync("opus", "cv1")).NoMetadata);
        Assert.True((await ParseOpusAsync("opus", "cv1", "--no-metadata")).NoMetadata);
    }

    [Fact]
    public async Task OpusCommand_PassesThroughWorkDirCookieAndUserAgent( )
    {
        var opt = await ParseOpusAsync("opus", "cv1", "--work-dir", "/tmp/x", "--cookie", "SESSDATA=abc", "-ua", "MyUA", "--debug");
        Assert.Equal("/tmp/x", opt.WorkDir);
        Assert.Equal("SESSDATA=abc", opt.Cookie);
        Assert.Equal("MyUA", opt.UserAgent);
        Assert.True(opt.Debug);
    }

    // 根命令也要认 --no-images，用户可以直接 `bbdown <专栏地址> --no-images` 而不写子命令
    [Fact]
    public async Task RootCommand_AlsoAcceptsNoImages( )
    {
        DownloadRequest? captured = null;
        var root = CommandLineInvoker.GetRootCommand(o =>
        {
            captured = o;
            return Task.FromResult(0);
        });

        await root.Parse(["https://www.bilibili.com/opus/1230485246732926996", "--no-images"])
            .InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(captured);
        Assert.True(captured!.NoImages);
    }
}
