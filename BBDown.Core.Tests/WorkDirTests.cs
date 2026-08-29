using System;
using System.IO;

namespace BBDown.Core.Tests;

public class WorkDirTests
{
    // 空 / 纯空白回落为 null，调用方据此使用进程当前目录
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void NormalizeWorkDir_EmptyOrWhitespaceReturnsNull(string? raw)
    {
        Assert.Null(WorkSetup.NormalizeWorkDir(raw));
    }

    // 首尾空白被去除后再规范化，避免生成带尾空格的目录
    [Fact]
    public void NormalizeWorkDir_TrimsSurroundingWhitespace( )
    {
        var dir = Path.Combine(Path.GetTempPath( ), "bbdown-wd-trim-test");
        try
        {
            Assert.Equal(Path.GetFullPath(dir), WorkSetup.NormalizeWorkDir("  " + dir + "  "));
        }
        finally
        {
            if (Directory.Exists(dir)) { Directory.Delete(dir); }
        }
    }

    // 环境变量被展开（Windows %VAR% / Unix $VAR）
    [Fact]
    public void NormalizeWorkDir_ExpandsEnvironmentVariables( )
    {
        var marker = "bbdown_wd_env_" + Guid.NewGuid( ).ToString("N");
        var baseDir = Path.Combine(Path.GetTempPath( ), marker);
        try
        {
            Environment.SetEnvironmentVariable("BBDown_WD_TEST", baseDir);
            var input = OperatingSystem.IsWindows( )
                ? "%BBDown_WD_TEST%\\sub"
                : "$BBDown_WD_TEST/sub";
            Assert.Equal(Path.GetFullPath(Path.Combine(baseDir, "sub")), WorkSetup.NormalizeWorkDir(input));
        }
        finally
        {
            Environment.SetEnvironmentVariable("BBDown_WD_TEST", null);
            if (Directory.Exists(baseDir)) { Directory.Delete(baseDir, true); }
        }
    }

    // 开头的 ~ 展开为用户主目录（Unix 习惯写法）；Windows 下同样适用
    [Fact]
    public void NormalizeWorkDir_ExpandsTildeToUserProfile( )
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.Equal(Path.Combine(home, "Downloads"), WorkSetup.NormalizeWorkDir("~/Downloads"));
        Assert.Equal(home, WorkSetup.NormalizeWorkDir("~"));
    }

    // 相对路径按进程当前目录解析为绝对路径
    [Fact]
    public void NormalizeWorkDir_RelativePathResolvesAgainstCurrentDirectory( )
    {
        Assert.Equal(Path.GetFullPath("some/relative/dir"), WorkSetup.NormalizeWorkDir("some/relative/dir"));
    }

    // 已是绝对路径则原样规范化（去多余分隔符）
    [Fact]
    public void NormalizeWorkDir_AbsolutePathIsNormalized( )
    {
        var abs = Path.Combine(Path.GetTempPath( ), "bbdown-wd-abs-test");
        Assert.Equal(Path.GetFullPath(abs), WorkSetup.NormalizeWorkDir(abs));
    }
}
