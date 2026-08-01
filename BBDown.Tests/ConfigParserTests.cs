using System.IO;
using System.Linq;

namespace BBDown.Tests;

public class ConfigParserTests
{
    private static string WriteConfig(params string[] lines)
    {
        var path = Path.Combine(Path.GetTempPath( ), Path.GetRandomFileName( ) + ".txt");
        File.WriteAllLines(path, lines);
        return path;
    }

    [Fact]
    public void TokenizeConfigLines_SkipsBlanksAndComments( )
    {
        var path = WriteConfig("# 这是注释", "", "   ", "--help");
        try
        {
            var tokens = BBDownConfigParser.TokenizeConfigLines(path);
            Assert.Equal(["--help"], tokens);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TokenizeConfigLines_SplitsOptionAndValue( )
    {
        var path = WriteConfig("--output /tmp/out");
        try
        {
            var tokens = BBDownConfigParser.TokenizeConfigLines(path);
            Assert.Equal(["--output", "/tmp/out"], tokens);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TokenizeConfigLines_StripsQuotesAroundValue( )
    {
        var path = WriteConfig("--output \"/tmp/my folder/out\"");
        try
        {
            var tokens = BBDownConfigParser.TokenizeConfigLines(path);
            Assert.Equal(["--output", "/tmp/my folder/out"], tokens);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TokenizeConfigLines_QuotedWholeLineBecomesToken( )
    {
        var path = WriteConfig("\"--app-only\"");
        try
        {
            var tokens = BBDownConfigParser.TokenizeConfigLines(path);
            Assert.Equal(["--app-only"], tokens);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TokenizeConfigLines_FlagWithoutSpaceIsSingleToken( )
    {
        var path = WriteConfig("-a");
        try
        {
            var tokens = BBDownConfigParser.TokenizeConfigLines(path);
            Assert.Equal(["-a"], tokens);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TokenizeConfigLines_MultipleLinesAccumulate( )
    {
        var path = WriteConfig("--app-only", "--output /tmp/out", "# x", "--single-thread");
        try
        {
            var tokens = BBDownConfigParser.TokenizeConfigLines(path);
            Assert.Equal(["--app-only", "--output", "/tmp/out", "--single-thread"], tokens);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
