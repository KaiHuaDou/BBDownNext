using System;
using System.Linq;

using BBDown.Core.Entity;

namespace BBDown.Tests;

public class SavePathTests
{
    // 钉住 SavePath.Variables 与 Format 内 switch 的同步：未知键会被原样保留，出现即说明表与求值漂移
    [Fact]
    public void Format_EveryListedToken_ReplacedInsteadOfPreserved( )
    {
        var page = new Page
        {
            Index = 1,
            Aid = "1",
            Cid = "1",
            EpId = "",
            Title = "P",
            Dur = 0,
            Res = "",
            PubTime = 0,
        };
        foreach (var variable in SavePath.Variables)
        {
            var output = SavePath.Format(variable.Token, "标题", null, null, page, 1, ApiType.Web, 0);
            Assert.DoesNotContain(variable.Token, output, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Variables_Tokens_Unique( )
    {
        Assert.Equal(SavePath.Variables.Length, SavePath.Variables.Select(v => v.Token).Distinct( ).Count( ));
    }

    [Fact]
    public void Variables_Descriptions_NonEmpty( )
    {
        Assert.All(SavePath.Variables, v => Assert.False(string.IsNullOrWhiteSpace(v.Description)));
    }
}
