using System;
using System.Threading.Tasks;

using BBDown.Core;

namespace BBDown.Tests;

public class CommandLineInvokerTests
{
    private const string SampleUrl = "https://www.bilibili.com/video/BV133411X769/";

    private static async Task<DownloadRequest> ParseAsync(params string[] args)
    {
        DownloadRequest? captured = null;
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

    // ---- 内容集（--get / --with / --without）----

    [Fact]
    public async Task Get_DefaultsToAvmsCiM( )
    {
        var opt = await ParseAsync(SampleUrl);
        Assert.Equal(ContentSelector.DefaultFlags, opt.Content);
    }

    [Fact]
    public async Task Get_SetsContent( )
    {
        var opt = await ParseAsync(SampleUrl, "-g", "av");
        Assert.Equal(DownloadContent.Audio | DownloadContent.Video, opt.Content);
    }

    [Fact]
    public async Task Get_MultipleValuesMerge( )
    {
        var opt = await ParseAsync(SampleUrl, "-g", "av", "--get", "s");
        Assert.True(opt.Content.Has(DownloadContent.Audio));
        Assert.True(opt.Content.Has(DownloadContent.Video));
        Assert.True(opt.Content.Has(DownloadContent.Subtitle));
    }

    [Fact]
    public async Task With_AddsOnTopOfDefault( )
    {
        var opt = await ParseAsync(SampleUrl, "-w", "d");
        Assert.True(opt.Content.Has(DownloadContent.Danmaku));
        Assert.True(opt.Content.Has(DownloadContent.Audio));
    }

    [Fact]
    public async Task Without_RemovesFromDefault( )
    {
        var opt = await ParseAsync(SampleUrl, "-W", "s");
        Assert.False(opt.Content.Has(DownloadContent.Subtitle));
        Assert.True(opt.Content.Has(DownloadContent.Audio));
    }

    [Fact]
    public async Task Combined_GetWithWithout( )
    {
        var opt = await ParseAsync(SampleUrl, "-g", "av", "-w", "c", "-W", "v");
        Assert.Equal(DownloadContent.Audio | DownloadContent.Cover, opt.Content);
    }

    // ---- API 通道（--api）----

    [Fact]
    public async Task Api_DefaultsToWeb( )
    {
        var opt = await ParseAsync(SampleUrl);
        Assert.Equal(ApiType.Web, opt.Api);
    }

    [Fact]
    public async Task Api_ParsesValuesIgnoringCase( )
    {
        Assert.Equal(ApiType.Tv, (await ParseAsync(SampleUrl, "--api", "TV")).Api);
        Assert.Equal(ApiType.App, (await ParseAsync(SampleUrl, "-a", "app")).Api);
        Assert.Equal(ApiType.Intl, (await ParseAsync(SampleUrl, "--api", "intl")).Api);
    }

    [Fact]
    public void Api_InvalidValue_ReportsError( )
    {
        var root = CommandLineInvoker.GetRootCommand(_ => Task.FromResult(0));
        Assert.NotEmpty(root.Parse([SampleUrl, "--api", "bogus"]).Errors);
    }

    [Fact]
    public async Task InfoOnly_AliasI( )
    {
        var opt = await ParseAsync(SampleUrl, "-i");
        Assert.True(opt.OnlyShowInfo);
    }

    // ---- 其余既有开关 ----

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

    // ---- 评论下载（--comments-*）----

    [Fact]
    public async Task CommentsCount_DefaultsToZero( )
    {
        var opt = await ParseAsync(SampleUrl);
        Assert.Equal(0, opt.CommentCount);
    }

    [Fact]
    public async Task CommentsCount_ParsesAfterUrl( )
    {
        var opt = await ParseAsync(SampleUrl, "--comments-count", "100");
        Assert.Equal(100, opt.CommentCount);
    }

    [Fact]
    public async Task CommentsCount_ParsesBeforeUrl( )
    {
        // 不放 ArgumentArity：位置参数 url 始终能落到自己头上，不会被 --comments-count 吞掉
        var opt = await ParseAsync("--comments-count", "100", SampleUrl);
        Assert.Equal(100, opt.CommentCount);
    }

    [Fact]
    public async Task CommentsSort_AliasCs( )
    {
        var opt = await ParseAsync(SampleUrl, "-cs", "time");
        Assert.Equal("time", opt.CommentSort);
    }

    [Fact]
    public async Task CommentsFormats_AliasCf( )
    {
        var opt = await ParseAsync(SampleUrl, "-cf", "txt");
        Assert.Equal("txt", opt.CommentFormats);
    }

    [Fact]
    public void Comments_RequiresIntegerValue( )
    {
        var root = CommandLineInvoker.GetRootCommand(_ => Task.FromResult(0));
        // 缺值：--comments-count 之后没有整数
        Assert.NotEmpty(root.Parse([SampleUrl, "--comments-count"]).Errors);
        // 非整数：把 URL 当值消费会转换失败
        Assert.NotEmpty(root.Parse([SampleUrl, "--comments-count", SampleUrl]).Errors);
    }

    [Fact]
    public void CommentsOptions_RegisteredToRootCommand( )
    {
        // 漏加进 RootCommand 集合会被 System.CommandLine 静默丢弃：用真实解析证明选项（含别名）都已注册
        var root = CommandLineInvoker.GetRootCommand(_ => Task.FromResult(0));
        var parseResult = root.Parse([SampleUrl, "-cn", "7", "-cs", "time", "-cf", "txt"]);
        Assert.Empty(parseResult.Errors); // 主选项与三个别名都被识别，无「未知选项」
        Assert.Equal(7, parseResult.GetValue<int>("--comments-count"));
        Assert.Equal("time", parseResult.GetValue<string>("--comments-sort"));
        Assert.Equal("txt", parseResult.GetValue<string>("--comments-formats"));
    }

    // ---- 专栏输入（无子命令，根命令直接识别）----

    [Fact]
    public async Task OpusInput_OpusUrl_PassesThrough( )
    {
        var opt = await ParseAsync("https://www.bilibili.com/opus/1230485246732926996");
        Assert.Equal("https://www.bilibili.com/opus/1230485246732926996", opt.Url);
    }

    [Fact]
    public async Task OpusInput_OpusPrefix_PassesThrough( )
    {
        var opt = await ParseAsync("opus1230485246732926996");
        Assert.Equal("opus1230485246732926996", opt.Url);
    }

    [Fact]
    public async Task OpusInput_CvId_PassesThrough( )
    {
        var opt = await ParseAsync("cv51908655");
        Assert.Equal("cv51908655", opt.Url);
    }

    // 根命令下 -W i 对专栏输入同样生效：默认含图片内容，可移除
    [Fact]
    public async Task OpusInput_WithoutImage_RemovesImageFlag( )
    {
        var opt = await ParseAsync("https://www.bilibili.com/opus/1230485246732926996", "-W", "i");
        Assert.False(opt.Content.Has(DownloadContent.OpusImage));
    }
}
