using System.Text.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;


namespace BBDown.Core.Tests;

public class ParserPureFunctionTests
{
    [Fact]
    public void BuildUrlList_MergesBaseAndBackupUrls( )
    {
        using var doc = JsonDocument.Parse("""{"base_url":"http://main","backup_url":["http://b1","http://b2"]}""");
        var list = Parser.BuildUrlList(doc.RootElement);
        Assert.Equal(["http://main", "http://b1", "http://b2"], list);
    }

    [Fact]
    public void BuildUrlList_NoBackupUrl( )
    {
        using var doc = JsonDocument.Parse("""{"base_url":"http://main"}""");
        Assert.Equal(["http://main"], Parser.BuildUrlList(doc.RootElement));
    }

    [Fact]
    public void BuildUrlList_NullBackupUrl( )
    {
        using var doc = JsonDocument.Parse("""{"base_url":"http://main","backup_url":null}""");
        Assert.Equal(["http://main"], Parser.BuildUrlList(doc.RootElement));
    }

    [Fact]
    public void PickBaseUrl_PrefersUrlWithoutPort( )
    {
        // 带端口的 url（P2P/mcdn）被跳过，选第一个不带端口的
        var list = new List<string> { "https://xy1.mcdn.bilivideo.cn:8082/v.m4s", "https://upos-sz.bilivideo.com/v.m4s" };
        Assert.Equal("https://upos-sz.bilivideo.com/v.m4s", Parser.PickBaseUrl(list));
    }

    [Fact]
    public void PickBaseUrl_AllHavePorts_FallsBackToFirst( )
    {
        var list = new List<string> { "https://a:8082/v.m4s", "https://b:4483/v.m4s" };
        Assert.Equal("https://a:8082/v.m4s", Parser.PickBaseUrl(list));
    }
}
