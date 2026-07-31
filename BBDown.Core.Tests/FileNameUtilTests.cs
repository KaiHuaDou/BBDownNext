using BBDown.Core.Util;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;


namespace BBDown.Core.Tests;

public class FileNameUtilTests
{
    [Theory]
    [InlineData("normal_title", "normal_title")]
    [InlineData("a:b*c?d", "a_b_c_d")]
    [InlineData("a<b>c|d\"e", "a_b_c_d_e")]
    public void GetValidFileName_ReplacesInvalidChars(string input, string expected)
    {
        Assert.Equal(expected, FileNameUtil.GetValidFileName(input));
    }

    [Fact]
    public void GetValidFileName_CustomReplacement( )
    {
        Assert.Equal(".a.b", FileNameUtil.GetValidFileName("*a?b", "."));
    }

    [Fact]
    public void GetValidFileName_SlashAlwaysFiltered_BackslashOnlyWhenRequested( )
    {
        // 正斜杠/反斜杠都在 InvalidChars 内，默认即被替换
        Assert.Equal("a_b", FileNameUtil.GetValidFileName("a/b"));
        Assert.Equal("a_b", FileNameUtil.GetValidFileName("a\\b"));
        Assert.Equal("a_b_c", FileNameUtil.GetValidFileName("a/b\\c", "_", true));
    }

    [Fact]
    public void GetValidFileName_ControlCharsStripped( )
    {
        Assert.Equal("a_b", FileNameUtil.GetValidFileName("a\nb"));
    }
}
