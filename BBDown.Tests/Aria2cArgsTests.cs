using System.IO;

namespace BBDown.Tests;

public class Aria2cArgsTests
{
    [Theory]
    [InlineData("", new string[0])]
    [InlineData("   ", new string[0])]
    [InlineData("--max-tries=5", new[] { "--max-tries=5" })]
    [InlineData("--max-tries=5   --timeout=30", new[] { "--max-tries=5", "--timeout=30" })]
    [InlineData("--out=\"my file.mp4\"", new[] { "--out=my file.mp4" })]
    [InlineData("--out='my file.mp4'", new[] { "--out=my file.mp4" })]
    [InlineData("--empty=\"\"", new[] { "--empty=" })]
    [InlineData("\t--a\n--b ", new[] { "--a", "--b" })]
    public void SplitArgs_TokenizesRespectingQuotes(string input, string[] expected)
    {
        Assert.Equal(expected, BBDownAria2c.SplitArgs(input));
    }

    [Fact]
    public void BuildArgs_PutsHeadersAndTargetInSeparateTokens( )
    {
        var path = Path.Combine(Path.GetTempPath( ), "bbdown", "video.m4s");

        var args = BBDownAria2c.BuildArgs("https://cdn.example.com/v.m4s", path, "", "SESSDATA=abc");

        Assert.Contains("--header=Referer: https://www.bilibili.com", args);
        Assert.Contains("--header=User-Agent: Mozilla/5.0", args);
        Assert.Contains("--header=Cookie: SESSDATA=abc", args);
        Assert.Contains("https://cdn.example.com/v.m4s", args);
        Assert.Equal(Path.GetDirectoryName(path), args[args.IndexOf("-d") + 1]);
        Assert.Equal("video.m4s", args[args.IndexOf("-o") + 1]);
    }

    [Theory]
    [InlineData("https://cdn.example.com/v.m4s?platform=android")]
    [InlineData("https://cdn.example.com/v.m4s?platform=android_tv_yst")]
    public void BuildArgs_SkipsRefererForAppEndpoints(string url)
    {
        var args = BBDownAria2c.BuildArgs(url, "/tmp/v.m4s", "", "");

        Assert.DoesNotContain(args, a => a.StartsWith("--header=Referer", System.StringComparison.Ordinal));
    }

    [Fact]
    public void BuildArgs_KeepsQuotedExtraArgumentAsOneToken( )
    {
        var args = BBDownAria2c.BuildArgs("https://cdn.example.com/v.m4s", "/tmp/v.m4s",
            "--user-agent=\"Mozilla 5.0 fake\" --max-tries=3", "");

        Assert.Contains("--user-agent=Mozilla 5.0 fake", args);
        Assert.Contains("--max-tries=3", args);
    }

    [Fact]
    public void BuildArgs_DoesNotLetCookieForgeExtraOptions( )
    {
        var args = BBDownAria2c.BuildArgs("https://cdn.example.com/v.m4s", "/tmp/v.m4s", "",
            "SESSDATA=abc\" --on-download-complete=/bin/sh \"");

        Assert.Contains("--header=Cookie: SESSDATA=abc\" --on-download-complete=/bin/sh \"", args);
        Assert.DoesNotContain(args, a => a.StartsWith("--on-download-complete", System.StringComparison.Ordinal));
    }
}
