using System;
using System.IO;
using System.Threading.Tasks;

namespace BBDown.Tests;

// P0-3: 配置文件此前完全失效（选项名反向拼装出 "----debug" 之类的无效 token）。
public sealed class ConfigParserMergeTests : IDisposable
{
    private const string SampleUrl = "https://www.bilibili.com/video/BV133411X769/";

    private readonly string configPath = Path.Combine(Path.GetTempPath( ), $"BBDown.{Guid.NewGuid( ):N}.config");

    public void Dispose( )
    {
        if (File.Exists(configPath))
        {
            File.Delete(configPath);
        }
    }

    private DownloadRequest Merge(string configContent, params string[] cliArgs)
    {
        File.WriteAllText(configPath, configContent);
        string[] args = [.. cliArgs, "--config", configPath];

        DownloadRequest? captured = null;
        var root = CommandLineInvoker.GetRootCommand(o =>
        {
            captured = o;
            return Task.FromResult(0);
        });

        var merged = ConfigParser.MergeWithConfig(args, root.Parse(args), root);
        var result = root.Parse(merged);
        Assert.Empty(result.Errors);
        result.Invoke( );

        Assert.NotNull(captured);
        return captured!;
    }

    [Fact]
    public void Config_SuppliesOptionMissingFromCommandLine( )
    {
        var opt = Merge("-g avd\n--work-dir \"D:/videos\"\n", SampleUrl);
        Assert.True(opt.Content.Has(DownloadContent.Danmaku));
        Assert.Equal("D:/videos", opt.WorkDir);
    }

    [Fact]
    public void CommandLine_WinsOverConfig( )
    {
        var opt = Merge("--work-dir \"D:/from-config\"\n", SampleUrl, "--work-dir", "D:/from-cli");
        Assert.Equal("D:/from-cli", opt.WorkDir);
    }

    // string[] 选项：命令行显式给过 --get 则配置里的 --get 被跳过，不会重复累积
    [Fact]
    public void Config_GetSkippedWhenCommandLineExplicit( )
    {
        var opt = Merge("-g d\n", SampleUrl, "-g", "av");
        Assert.Equal(DownloadContent.Audio | DownloadContent.Video, opt.Content);
    }

    [Fact]
    public void Config_CanAddAiSubtitle( )
    {
        var opt = Merge("-w S\n", SampleUrl);
        Assert.True(opt.Content.Has(DownloadContent.AiSubtitle));
    }

    [Fact]
    public void Config_IgnoresCommentsAndBlankLines( )
    {
        var opt = Merge("# 这是注释\n\n-g avd\n", SampleUrl);
        Assert.True(opt.Content.Has(DownloadContent.Danmaku));
    }

    [Fact]
    public void Config_SuppliesUrlWhenCommandLineHasNone( )
    {
        var opt = Merge($"{SampleUrl}\n-g avd\n");
        Assert.Equal(SampleUrl, opt.Url);
        Assert.True(opt.Content.Has(DownloadContent.Danmaku));
    }

    [Fact]
    public void CommandLineUrl_WinsOverConfigUrl( )
    {
        var opt = Merge("https://www.bilibili.com/video/BV1from2config\n", SampleUrl);
        Assert.Equal(SampleUrl, opt.Url);
    }

    [Fact]
    public void MissingConfigFile_ReturnsOriginalArgs( )
    {
        string[] args = [SampleUrl, "--config", Path.Combine(Path.GetTempPath( ), "definitely-not-here.config")];
        var root = CommandLineInvoker.GetRootCommand(_ => Task.FromResult(0));
        var merged = ConfigParser.MergeWithConfig(args, root.Parse(args), root);
        Assert.Same(args, merged);
    }

    [Fact]
    public void EmptyConfigFile_ReturnsOriginalArgs( )
    {
        File.WriteAllText(configPath, "# 只有注释\n\n");
        string[] args = [SampleUrl, "--config", configPath];
        var root = CommandLineInvoker.GetRootCommand(_ => Task.FromResult(0));
        var merged = ConfigParser.MergeWithConfig(args, root.Parse(args), root);
        Assert.Same(args, merged);
    }
}
