using System;
using System.Linq;

namespace BBDown.Core.Download;

public enum DanmakuFormat
{
    Xml,
    Ass,
}

public static class DanmakuFormatInfo
{
    // 默认
    public static readonly DanmakuFormat[] DefaultFormats = [DanmakuFormat.Xml, DanmakuFormat.Ass];
    public static readonly string[] DefaultFormatsNames =
        [.. DefaultFormats.Select(f => f.ToString( ).ToLower( ))];
    // 可选项
    public static readonly string[] AllFormatNames =
        [.. Enum.GetNames<DanmakuFormat>( ).Select(f => f.ToLower( ))];

    public static DanmakuFormat FromFormatName(string formatName)
    {
        return formatName switch
        {
            "xml" => DanmakuFormat.Xml,
            "ass" => DanmakuFormat.Ass,
            _ => DanmakuFormat.Xml,
        };
    }
}
