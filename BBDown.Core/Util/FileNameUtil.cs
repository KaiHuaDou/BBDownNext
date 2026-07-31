using System.Linq;

namespace BBDown.Core.Util;

public static class FileNameUtil
{
    // Windows 与 Unix 非法字符的并集；路径分隔符也在内，调用方传进来的一律是单段文件名
    private static readonly char[] InvalidChars =
        ['"', '<', '>', '|', ':', '*', '?', '\\', '/', .. Enumerable.Range(0, 32).Select(i => (char) i)];

    public static string GetValidFileName(string input, string re = "_")
    {
        var title = input;

        foreach (var invalidChar in InvalidChars)
        {
            title = title.Replace(invalidChar.ToString( ), re);
        }

        return title;
    }
}
