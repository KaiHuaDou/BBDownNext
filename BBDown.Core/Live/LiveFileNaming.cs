using System;
using System.Globalization;
using System.IO;

using BBDown.Core.Util;

namespace BBDown.Core.Live;

/// <summary>
/// 直播录制的文件命名。直播不走 <see cref="Pipeline.SavePath"/>/<c>-F</c>：那套模板依赖分 P、清晰度、
/// 编码等录制开始时还不存在的信息，且会硬加 .mp4 后缀。
/// </summary>
public static class LiveFileNaming
{
    private const int UnameMaxBytes = 40;
    private const int TitleMaxBytes = 80;

    /// <summary>
    /// 生成不含扩展名的基名。主播名与标题先各自按字节截断再拼接——
    /// 直接拼完再整串截断会把用于区分场次的时间戳切掉。
    /// </summary>
    public static string BuildBaseName(string uname, string title, DateTime startTime)
    {
        var stamp = startTime.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        var left = FileNameUtil.TruncateToBytes(uname.Trim( ), UnameMaxBytes).Trim( );
        var middle = FileNameUtil.TruncateToBytes(title.Trim( ), TitleMaxBytes).Trim( );

        var name = (left.Length, middle.Length) switch
        {
            (0, 0) => stamp,
            (0, _) => $"{middle}-{stamp}",
            (_, 0) => $"{left}-{stamp}",
            _ => $"{left}-{middle}-{stamp}"
        };

        return FileNameUtil.GetValidFileName(name);
    }

    /// <summary>
    /// 分段文件路径。<c>.bbdown.part</c> 后缀已被 .gitignore 覆盖，且与既有下载分片语义一致。
    /// </summary>
    public static string BuildSegmentPath(string destPathWithoutExtension, int index)
    {
        return $"{destPathWithoutExtension}.{index.ToString("D3", CultureInfo.InvariantCulture)}.bbdown.part";
    }

    /// <summary>
    /// 同一场直播重复录制时避免覆盖已有成品。<paramref name="exists"/> 便于测试注入。
    /// </summary>
    public static string EnsureUnique(string path, Func<string, bool>? exists = null)
    {
        exists ??= File.Exists;
        if (!exists(path))
        {
            return path;
        }

        var dir = Path.GetDirectoryName(path) ?? "";
        var stem = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (var i = 2; i < 1000; i++)
        {
            var candidate = Path.Combine(dir, $"{stem}-{i.ToString(CultureInfo.InvariantCulture)}{ext}");
            if (!exists(candidate))
            {
                return candidate;
            }
        }

        return path;
    }
}
