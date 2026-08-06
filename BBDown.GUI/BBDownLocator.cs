using System;
using System.IO;

namespace BBDown.GUI;

/// <summary>按 GUI exe 同目录 → PATH 逐目录的顺序查找 BBDown.exe；未命中返回 null（由界面提示手动选择）。</summary>
public static class BBDownLocator
{
    private const string ExeName = "BBDown.exe";

    public static string? Find( )
    {
        var exeDir = Path.GetDirectoryName(Environment.ProcessPath);
        if (exeDir is not null && File.Exists(Path.Combine(exeDir, ExeName)))
        {
            return Path.Combine(exeDir, ExeName);
        }

        var pathValue = Environment.GetEnvironmentVariable("PATH");
        if (pathValue is null)
        {
            return null;
        }

        foreach (var dir in pathValue.Split(Path.PathSeparator))
        {
            var trimmed = dir.Trim( );
            if (trimmed.Length == 0)
            {
                continue;
            }

            try
            {
                var candidate = Path.Combine(trimmed, ExeName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch (ArgumentException)
            {
                // PATH 中可能存在带非法字符的目录项，跳过
            }
        }

        return null;
    }
}
