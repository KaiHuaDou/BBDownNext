using System;

namespace BBDown.Core.Opus;

/// <summary>
/// 图片 URL 归一化：BBDown.Core（渲染器）与 BBDown（下载器）都用它生成同一把字典键，
/// 保证「下载后的本地相对路径」能正确回填进 Markdown。
/// 规则：协议相对 // 补全 https；http 升 https；剥掉文件名段里的 @ 格式化后缀以拿到原图（加 @ 会得到重编码图）。
/// </summary>
public static class OpusImageUtil
{
    public static string Normalize(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return "";
        }

        url = url.Trim( );
        if (url.StartsWith("//", StringComparison.Ordinal))
        {
            url = "https:" + url;
        }
        else if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            url = "https://" + url[7..];
        }

        var at = url.LastIndexOf('@');
        var lastSlash = url.LastIndexOf('/');
        // 只剥文件名段里的 @（@ 在最后一个 / 之后），避免误伤 URL 路径其它位置
        if (at > lastSlash)
        {
            url = url[..at];
        }

        return url;
    }
}
