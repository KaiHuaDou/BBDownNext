using System;
using System.CommandLine;
using System.IO;
using System.Threading.Tasks;
using BBDown;
using Xunit;

namespace BBDown.Tests;

// P0-3: 配置文件此前完全失效（选项名反向拼装出 "----debug" 之类的无效 token）。
public sealed class BBDownConfigParserTests : IDisposable
{
    private static readonly string SampleUrl = TestVideos.PickRandom();

    private readonly string configPath = Path.Combine(Path.GetTempPath(), $"BBDown.{Guid.NewGuid():N}.config");

    public void Dispose()
    {
        if (File.Exists(configPath)) File.Delete(configPath);
    }

    private DownloadOptions Merge(string configContent, params string[] cliArgs)
    {
        File.WriteAllText(configPath, configContent);
        string[] args = [.. cliArgs, "--config", configPath];

        DownloadOptions? captured = null;
        var root = CommandLineInvoker.GetRootCommand(o =>
        {
            captured = o;
            return Task.FromResult(0);
        });

        var merged = BBDownConfigParser.MergeWithConfig(args, root.Parse(args), root);
        var result = root.Parse(merged);
        Assert.Empty(result.Errors);
        result.Invoke();

        Assert.NotNull(captured);
        return captured!;
    }

    [Fact]
    public void Config_SuppliesOptionMissingFromCommandLine()
    {
        var opt = Merge("--danmaku\n--work-dir \"D:/videos\"\n", SampleUrl);
        Assert.True(opt.DownloadDanmaku);
        Assert.Equal("D:/videos", opt.WorkDir);
    }

    [Fact]
    public void CommandLine_WinsOverConfig()
    {
        var opt = Merge("--work-dir \"D:/from-config\"\n", SampleUrl, "--work-dir", "D:/from-cli");
        Assert.Equal("D:/from-cli", opt.WorkDir);
    }

    // 默认值为 true 的开关，配置文件必须能关掉。
    [Fact]
    public void Config_CanEnableAllowAi()
    {
        var opt = Merge("--allow-ai\n", SampleUrl);
        Assert.True(opt.AllowAi);
    }

    [Fact]
    public void Config_IgnoresCommentsAndBlankLines()
    {
        var opt = Merge("# 这是注释\n\n--danmaku\n", SampleUrl);
        Assert.True(opt.DownloadDanmaku);
    }

    [Fact]
    public void Config_SuppliesUrlWhenCommandLineHasNone()
    {
        var opt = Merge($"{SampleUrl}\n--danmaku\n");
        Assert.Equal(SampleUrl, opt.Url);
        Assert.True(opt.DownloadDanmaku);
    }

    [Fact]
    public void CommandLineUrl_WinsOverConfigUrl()
    {
        var opt = Merge("https://www.bilibili.com/video/BV1from2config\n", SampleUrl);
        Assert.Equal(SampleUrl, opt.Url);
    }

    [Fact]
    public void MissingConfigFile_ReturnsOriginalArgs()
    {
        string[] args = [SampleUrl, "--config", Path.Combine(Path.GetTempPath(), "definitely-not-here.config")];
        var root = CommandLineInvoker.GetRootCommand(_ => Task.FromResult(0));
        var merged = BBDownConfigParser.MergeWithConfig(args, root.Parse(args), root);
        Assert.Same(args, merged);
    }

    [Fact]
    public void EmptyConfigFile_ReturnsOriginalArgs()
    {
        File.WriteAllText(configPath, "# 只有注释\n\n");
        string[] args = [SampleUrl, "--config", configPath];
        var root = CommandLineInvoker.GetRootCommand(_ => Task.FromResult(0));
        var merged = BBDownConfigParser.MergeWithConfig(args, root.Parse(args), root);
        Assert.Same(args, merged);
    }
}
