namespace BBDown.Core.Tests;

// ResolveExt 为纯函数（字符串切片，无 IO），不触网可直测
public class AudioDownloadTests
{
    public static TheoryData<string, string> ExtCases => new( )
    {
        // CDN 实际形态：扩展名后带签名 query
        { "https://upos-sz-mirrorcos.bilivideo.com/xxx/m4a-192k.m4a?e=ig8&deadline=1725013547", ".m4a" },
        { "https://example.com/song.mp3", ".mp3" },
        { "https://example.com/song.flac?token=1", ".flac" },
        // 兜底：无扩展 / 非音频扩展一律 .m4a（B 站音频流当前恒为 m4a 容器）
        { "https://example.com/song", ".m4a" },
        { "https://example.com/song.txt?a=b", ".m4a" },
        { "https://example.com/a.b/song", ".m4a" },
    };

    [Theory]
    [MemberData(nameof(ExtCases))]
    public void ResolveExt_UrlVariants_InfersAudioExt(string url, string expected)
    {
        Assert.Equal(expected, AudioDownload.ResolveExt(url));
    }
}
