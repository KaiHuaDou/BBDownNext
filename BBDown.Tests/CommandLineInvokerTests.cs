using System;
using System.Threading.Tasks;

namespace BBDown.Tests;

public class CommandLineInvokerTests
{
    private static readonly string SampleUrl = TestVideos.PickRandom( );

    private static async Task<DownloadOptions> ParseAsync(params string[] args)
    {
        DownloadOptions? captured = null;
        var root = CommandLineInvoker.GetRootCommand(o =>
        {
            captured = o;
            return Task.FromResult(0);
        });
        var parseResult = root.Parse(args);
        await parseResult.InvokeAsync( );
        Assert.NotNull(captured);
        return captured!;
    }

    // P0-1: --no-metadata 曾未注册到 RootCommand，被静默丢弃。
    [Fact]
    public async Task NoMetadata_Flag_EnablesOption( )
    {
        var opt = await ParseAsync(SampleUrl, "--no-metadata");
        Assert.True(opt.NoMetadata);
    }

    [Fact]
    public async Task NoMetadata_DefaultsToFalse( )
    {
        var opt = await ParseAsync(SampleUrl);
        Assert.False(opt.NoMetadata);
    }

    // P0-2: 默认开启的开关曾被无条件覆盖成 false。不加 -st 即视为多线程（默认 SingleThread 为 false）。
    [Fact]
    public async Task SingleThread_DefaultsToFalse( )
    {
        var opt = await ParseAsync(SampleUrl);
        Assert.False(opt.SingleThread);
    }

    [Fact]
    public async Task SingleThread_SetsSingleThread( )
    {
        var opt = await ParseAsync(SampleUrl, "--single-thread");
        Assert.True(opt.SingleThread);
    }

    [Fact]
    public async Task NoForceHttp_DefaultsToFalse( )
    {
        var opt = await ParseAsync(SampleUrl);
        Assert.False(opt.NoForceHttp);
    }

    // P0-3: --allow-ai 反转语义——默认不下载 AI 字幕，加选项才下载。
    [Fact]
    public async Task AllowAi_DefaultsToFalse( )
    {
        var opt = await ParseAsync(SampleUrl);
        Assert.False(opt.AllowAi);
    }

    [Fact]
    public async Task AllowAi_Flag_Enables( )
    {
        var opt = await ParseAsync(SampleUrl, "--allow-ai");
        Assert.True(opt.AllowAi);
    }

    // 默认拦截充电专属试看片段，加选项才放行
    [Fact]
    public async Task AllowPreview_DefaultsToFalse( )
    {
        var opt = await ParseAsync(SampleUrl);
        Assert.False(opt.AllowPreview);
    }

    [Fact]
    public async Task AllowPreview_Flag_Enables( )
    {
        var opt = await ParseAsync(SampleUrl, "--allow-preview");
        Assert.True(opt.AllowPreview);
    }

    // --no-force-host 反转语义——默认强制替换 host，加选项才不替换。
    [Fact]
    public async Task NoForceHost_DefaultsToFalse( )
    {
        var opt = await ParseAsync(SampleUrl);
        Assert.False(opt.NoForceHost);
    }

    [Fact]
    public async Task NoForceHost_Flag_Enables( )
    {
        var opt = await ParseAsync(SampleUrl, "--no-force-host");
        Assert.True(opt.NoForceHost);
    }

    [Fact]
    public async Task DelayPerPage_DefaultsToZeroString( )
    {
        var opt = await ParseAsync(SampleUrl);
        Assert.Equal("0", opt.DelayPerPage);
    }

    [Fact]
    public async Task Host_DefaultsToApiBilibili( )
    {
        var opt = await ParseAsync(SampleUrl);
        Assert.Equal("api.bilibili.com", opt.Host);
    }

    // P0-3 的合并策略前提：同名选项重复出现时，后出现者胜出。
    // 配置文件参数拼在命令行参数之前，命令行因此天然覆盖配置文件。
    // System.CommandLine 2.0.10 对重复出现的单值选项不做「后者胜出」，而是在取值时抛异常。
    // 这决定了配置文件只能「补齐」命令行未指定的选项，不能简单拼接。见 ConfigParser.MergeWithConfig。
    [Fact]
    public void DuplicatedOption_ThrowsOnGetValue( )
    {
        var root = CommandLineInvoker.GetRootCommand(_ => Task.FromResult(0));
        var parseResult = root.Parse([SampleUrl, "--host", "from-config", "--host", "from-cli"]);
        Assert.Throws<InvalidOperationException>(( ) => parseResult.GetValue<string>("--host"));
    }

    [Fact]
    public async Task EncodingFirst_TrueWhenEncodingPriorityWrittenFirst( )
    {
        var opt = await ParseAsync(SampleUrl, "-e", "hevc,avc", "-q", "1080P 高码率");
        Assert.True(opt.EncodingFirst);
    }

    [Fact]
    public async Task EncodingFirst_FalseWhenDfnPriorityWrittenFirst( )
    {
        var opt = await ParseAsync(SampleUrl, "-q", "1080P 高码率", "-e", "hevc,avc");
        Assert.False(opt.EncodingFirst);
    }

    [Fact]
    public async Task EncodingFirst_FalseWhenOnlyOneSpecified( )
    {
        var opt = await ParseAsync(SampleUrl, "-e", "hevc,avc");
        Assert.False(opt.EncodingFirst);
    }
}
