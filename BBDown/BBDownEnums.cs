using System;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;


namespace BBDown;

public enum BBDownDanmakuFormat
{
    Xml,
    Ass,
}

public static class BBDownDanmakuFormatInfo
{
    // 默认
    public static readonly BBDownDanmakuFormat[] DefaultFormats = [BBDownDanmakuFormat.Xml, BBDownDanmakuFormat.Ass];
    public static readonly string[] DefaultFormatsNames = DefaultFormats.Select(f => f.ToString( ).ToLower( )).ToArray( );
    // 可选项
    public static readonly string[] AllFormatNames = Enum.GetNames<BBDownDanmakuFormat>( ).Select(f => f.ToLower( )).ToArray( );

    public static BBDownDanmakuFormat FromFormatName(string formatName)
    {
        return formatName switch
        {
            "xml" => BBDownDanmakuFormat.Xml,
            "ass" => BBDownDanmakuFormat.Ass,
            _ => BBDownDanmakuFormat.Xml,
        };
    }
}
