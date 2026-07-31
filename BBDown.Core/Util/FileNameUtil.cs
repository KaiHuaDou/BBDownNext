using System.Linq;

namespace BBDown.Core.Util;

public static class FileNameUtil
{
    private static readonly char[] InvalidChars = "34,60,62,124,0,1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,17,18,19,20,21,22,23,24,25,26,27,28,29,30,31,58,42,63,92,47"
        .Split(',').Select(s => (char) byte.Parse(s)).ToArray( );

    public static string GetValidFileName(string input, string re = "_", bool filterSlash = false)
    {
        var title = input;

        foreach (var invalidChar in InvalidChars)
        {
            title = title.Replace(invalidChar.ToString( ), re);
        }

        if (filterSlash)
        {
            title = title.Replace("/", re);
            title = title.Replace("\\", re);
        }

        return title;
    }
}
