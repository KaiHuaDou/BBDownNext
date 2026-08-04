using System.Collections.Generic;

using BBDown.Core;

namespace BBDown.Tests;

/// <summary>
/// P0-7：<c>--allow-pcdn</c> 单独使用必须能让 PCDN 域名保留，
/// 不能因为默认强制替换 host 而被覆盖（那样就等同于死选项）。
/// </summary>
public class CdnHostTests
{
    private const string PcdnUrl = "https://p.pcdn.bilibili.com:8080/upgcxcode/11/22/333.flv";
    private const string BackupHost = "upos-sz-mirrorcoso1.bilivideo.com";

    [Fact]
    public void AllowPcdn_KeepsPcdnUrl_WithoutNoForceHost( )
    {
        var opt = new DownloadOptions { AllowPcdn = true, NoForceHost = false };
        var clips = new List<string> { PcdnUrl };

        CdnHost.Apply(opt, clips, AppConfig.Empty);

        // 单独 --allow-pcdn 即应保留原 PCDN 域名
        Assert.Equal(PcdnUrl, clips[0]);
    }

    [Fact]
    public void Default_ReplacesPcdnUrl( )
    {
        var opt = new DownloadOptions { AllowPcdn = false, NoForceHost = false };
        var clips = new List<string> { PcdnUrl };

        CdnHost.Apply(opt, clips, AppConfig.Empty);

        Assert.Contains(BackupHost, clips[0]);
        Assert.DoesNotContain("pcdn", clips[0]);
    }

    [Fact]
    public void AllowPcdn_StillForcesNormalHost( )
    {
        // 普通 upos host 即便在 --allow-pcdn 下也应被强制替换为备用 host
        var opt = new DownloadOptions { AllowPcdn = true, NoForceHost = false };
        var clips = new List<string> { "https://upos-sz-upcdnbda2.bilivideo.com/upgcxcode/x.flv" };

        CdnHost.Apply(opt, clips, AppConfig.Empty);

        Assert.Contains(BackupHost, clips[0]);
    }

    [Fact]
    public void NoForceHost_KeepsPcdnAndNormalHost( )
    {
        var opt = new DownloadOptions { AllowPcdn = true, NoForceHost = true };
        var clips = new List<string> { PcdnUrl };

        CdnHost.Apply(opt, clips, AppConfig.Empty);

        Assert.Equal(PcdnUrl, clips[0]);
    }
}
