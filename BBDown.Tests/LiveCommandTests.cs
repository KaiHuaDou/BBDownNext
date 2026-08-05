using System.Threading.Tasks;

using BBDown.Core.Live;

namespace BBDown.Tests;

public class LiveCommandTests
{
    private const string LiveUrl = "https://live.bilibili.com/22632424";

    private static async Task<DownloadOptions> ParseAsync(params string[] args)
    {
        DownloadOptions? captured = null;
        var root = CommandLineInvoker.GetRootCommand(o =>
        {
            captured = o;
            return Task.FromResult(0);
        });
        await root.Parse(args).InvokeAsync( );
        Assert.NotNull(captured);
        return captured!;
    }

    [Fact]
    public async Task LiveQuality_DefaultsToOriginal( )
    {
        var opt = await ParseAsync(LiveUrl);
        Assert.Equal(LiveQuality.Original, opt.LiveQuality);
    }

    [Fact]
    public async Task LiveQuality_ParsesExplicitValue( )
    {
        Assert.Equal(250, (await ParseAsync(LiveUrl, "--live-quality", "250")).LiveQuality);
        Assert.Equal(400, (await ParseAsync(LiveUrl, "-lq", "400")).LiveQuality);
    }

    // 漏加进 RootCommand 集合会被 System.CommandLine 静默丢弃
    [Fact]
    public void LiveQualityOption_RegisteredToRootCommand( )
    {
        var root = CommandLineInvoker.GetRootCommand(_ => Task.FromResult(0));
        var parseResult = root.Parse([LiveUrl, "-lq", "250"]);
        Assert.Empty(parseResult.Errors);
        Assert.Equal(250, parseResult.GetValue<int>("--live-quality"));
    }

    [Fact]
    public void LiveQuality_RequiresIntegerValue( )
    {
        var root = CommandLineInvoker.GetRootCommand(_ => Task.FromResult(0));
        Assert.NotEmpty(root.Parse([LiveUrl, "--live-quality"]).Errors);
        Assert.NotEmpty(root.Parse([LiveUrl, "--live-quality", "原画"]).Errors);
    }

    // 根命令直接吃直播地址：解析出的 Url 必须能被旁路识别，否则会掉进普通视频流程
    [Fact]
    public async Task RootCommand_LiveUrl_IsRecognizedByResolver( )
    {
        var opt = await ParseAsync(LiveUrl, "--work-dir", "/tmp/x");
        Assert.True(LiveInputResolver.TryParse(opt.Url, out var target));
        Assert.Equal("22632424", target.RoomId);
        Assert.Equal("/tmp/x", opt.WorkDir);
    }

    [Fact]
    public async Task RootCommand_LivePrefix_IsRecognizedByResolver( )
    {
        var opt = await ParseAsync("live:22632424");
        Assert.True(LiveInputResolver.TryParse(opt.Url, out var target));
        Assert.Equal("22632424", target.RoomId);
    }

    // 普通视频地址不能被直播旁路截胡
    [Fact]
    public async Task RootCommand_VideoUrl_IsNotRecognizedAsLive( )
    {
        var opt = await ParseAsync(TestVideos.PickRandom( ));
        Assert.False(LiveInputResolver.TryParse(opt.Url, out _));
    }

    [Fact]
    public async Task LiveUrl_CoexistsWithCommonOptions( )
    {
        var opt = await ParseAsync(LiveUrl, "-lq", "250", "--cookie", "SESSDATA=abc", "-ua", "MyUA", "--debug");
        Assert.Equal(250, opt.LiveQuality);
        Assert.Equal("SESSDATA=abc", opt.Cookie);
        Assert.Equal("MyUA", opt.UserAgent);
        Assert.True(opt.Debug);
    }
}
