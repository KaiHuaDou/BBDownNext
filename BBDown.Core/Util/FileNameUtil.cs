using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace BBDown.Core.Util;

public static class FileNameUtil
{
    // Windows 与 Unix 非法字符的并集；路径分隔符也在内，调用方传进来的一律是单段文件名
    private static readonly HashSet<char> InvalidChars =
        ['"', '<', '>', '|', ':', '*', '?', '\\', '/', .. Enumerable.Range(0, 32).Select(i => (char) i)];

    // Windows 上这些设备名连带任意扩展名一起被拒绝，CON.mp4 同样无法创建
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    // ext4/APFS 单段上限 255 字节，留出分片前缀、字幕语言后缀与扩展名的余量
    private const int MaxBytes = 200;

    public static string GetValidFileName(string input)
    {
        var builder = new StringBuilder(input.Length);
        foreach (var c in input)
        {
            builder.Append(InvalidChars.Contains(c) ? '_' : c);
        }

        // Windows 会静默吃掉结尾的点与空格，索性自己去掉；开头的点在 Unix 上是隐藏文件
        var name = builder.ToString( ).Trim( ).TrimEnd('.').Trim( );
        if (name.StartsWith('.'))
        {
            name = "_" + name;
        }

        var stem = name.Contains('.') ? name[..name.IndexOf('.')] : name;
        if (ReservedNames.Contains(stem))
        {
            name = "_" + name;
        }

        return TruncateToBytes(name, MaxBytes);
    }

    /// <summary>
    /// 按 UTF-8 字节数截断。拼接文件名时须先各自截断再拼，否则整串截断会把尾部的时间戳之类切掉。
    /// </summary>
    public static string TruncateToBytes(string input, int maxBytes)
    {
        if (Encoding.UTF8.GetByteCount(input) <= maxBytes)
        {
            return input;
        }

        var used = 0;
        for (var i = 0; i < input.Length; i++)
        {
            // 代理对必须整体保留，否则会切出无效字符
            var runeLength = char.IsHighSurrogate(input[i]) && i + 1 < input.Length ? 2 : 1;
            var bytes = Encoding.UTF8.GetByteCount(input.AsSpan(i, runeLength));
            if (used + bytes > maxBytes)
            {
                return input[..i].TrimEnd( );
            }

            used += bytes;
            i += runeLength - 1;
        }

        return input;
    }
}
