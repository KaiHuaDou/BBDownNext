using System;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.Threading.Tasks;
using BBDown;
using Xunit;

namespace BBDown.Tests;

public class CommandLineInvokerTests
{
    private static readonly string SampleUrl = TestVideos.PickRandom();

    private static async Task<MyOption> ParseAsync(params string[] args)
    {
        MyOption? captured = null;
        var root = CommandLineInvoker.GetRootCommand(o =>
        {
            captured = o;
            return Task.CompletedTask;
        });
        var parseResult = root.Parse(args);
        await parseResult.InvokeAsync();
        Assert.NotNull(captured);
        return captured!;
    }

    // P0-1: --simply-mux 之前未注册到 RootCommand，被静默丢弃。
    [Fact]
    public async Task SimplyMux_Flag_EnablesOption()
    {
        var opt = await ParseAsync(SampleUrl, "--simply-mux");
        Assert.True(opt.SimplyMux);
    }

    [Fact]
    public async Task SimplyMux_DefaultsToFalse()
    {
        var opt = await ParseAsync(SampleUrl);
        Assert.False(opt.SimplyMux);
    }

    // P0-2: 默认开启的开关曾被无条件覆盖成 false。
    [Fact]
    public async Task MultiThread_DefaultsToTrue()
    {
        var opt = await ParseAsync(SampleUrl);
        Assert.True(opt.MultiThread);
    }

    [Fact]
    public async Task MultiThread_Flag_Enables()
    {
        var opt = await ParseAsync(SampleUrl, "--multi-thread");
        Assert.True(opt.MultiThread);
    }

    [Fact]
    public async Task SingleThread_DisablesMultiThread()
    {
        var opt = await ParseAsync(SampleUrl, "--single-thread");
        Assert.True(opt.SingleThread);
        Assert.False(opt.MultiThread);
    }

    [Fact]
    public async Task ForceHttp_DefaultsToTrue()
    {
        var opt = await ParseAsync(SampleUrl);
        Assert.True(opt.ForceHttp);
    }

    [Fact]
    public async Task SkipAi_DefaultsToTrue()
    {
        var opt = await ParseAsync(SampleUrl);
        Assert.True(opt.SkipAi);
    }

    [Fact]
    public async Task ForceReplaceHost_DefaultsToTrue()
    {
        var opt = await ParseAsync(SampleUrl);
        Assert.True(opt.ForceReplaceHost);
    }

    [Fact]
    public async Task DelayPerPage_DefaultsToZeroString()
    {
        var opt = await ParseAsync(SampleUrl);
        Assert.Equal("0", opt.DelayPerPage);
    }

    [Fact]
    public async Task Host_DefaultsToApiBilibili()
    {
        var opt = await ParseAsync(SampleUrl);
        Assert.Equal("api.bilibili.com", opt.Host);
    }

    // P0-3 的合并策略前提：同名选项重复出现时，后出现者胜出。
    // 配置文件参数拼在命令行参数之前，命令行因此天然覆盖配置文件。
    // System.CommandLine 2.0.10 对重复出现的单值选项不做「后者胜出」，而是在取值时抛异常。
    // 这决定了配置文件只能「补齐」命令行未指定的选项，不能简单拼接。见 BBDownConfigParser.MergeWithConfig。
    [Fact]
    public void DuplicatedOption_ThrowsOnGetValue()
    {
        var root = CommandLineInvoker.GetRootCommand(_ => Task.CompletedTask);
        var parseResult = root.Parse([SampleUrl, "--host", "from-config", "--host", "from-cli"]);
        Assert.Throws<InvalidOperationException>(() => parseResult.GetValue<string>("--host"));
    }

    [Fact]
    public async Task EncodingFirst_TrueWhenEncodingPriorityWrittenFirst()
    {
        var opt = await ParseAsync(SampleUrl, "-e", "hevc,avc", "-q", "1080P 高码率");
        Assert.True(opt.EncodingFirst);
    }

    [Fact]
    public async Task EncodingFirst_FalseWhenDfnPriorityWrittenFirst()
    {
        var opt = await ParseAsync(SampleUrl, "-q", "1080P 高码率", "-e", "hevc,avc");
        Assert.False(opt.EncodingFirst);
    }

    [Fact]
    public async Task EncodingFirst_FalseWhenOnlyOneSpecified()
    {
        var opt = await ParseAsync(SampleUrl, "-e", "hevc,avc");
        Assert.False(opt.EncodingFirst);
    }
}
