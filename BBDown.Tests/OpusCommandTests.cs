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
    public async Task OpusCommand_ImageDefaultOnAndCanBeRemoved( )
    {
        Assert.True((await ParseOpusAsync("opus", "cv1")).Content.Has(DownloadContent.OpusImage));
        Assert.False((await ParseOpusAsync("opus", "cv1", "-W", "i")).Content.Has(DownloadContent.OpusImage));
    }

    // opus 默认内容集 avmsCiM 含 M：front matter 默认输出，-W M 可关闭
    [Fact]
    public async Task OpusCommand_FrontMatterOnByDefaultAndCanBeRemoved( )
    {
        Assert.True((await ParseOpusAsync("opus", "cv1")).Content.Has(DownloadContent.FrontMatter));
        Assert.False((await ParseOpusAsync("opus", "cv1", "-W", "M")).Content.Has(DownloadContent.FrontMatter));
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

    // 根命令也要认 -W i，用户可以直接 `bbdown <专栏地址> -W i` 而不写子命令
    [Fact]
    public async Task RootCommand_AlsoAcceptsWithoutContent( )
    {
        DownloadRequest? captured = null;
        var root = CommandLineInvoker.GetRootCommand(o =>
        {
            captured = o;
            return Task.FromResult(0);
        });

        await root.Parse(["https://www.bilibili.com/opus/1230485246732926996", "-W", "i"])
            .InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(captured);
        Assert.False(captured!.Content.Has(DownloadContent.OpusImage));
    }
}
