using System;
using System.Linq;

namespace BBDown.Core.Download;

public enum CommentFormat
{
    Json,
    Txt,
}

public static class CommentFormatInfo
{
    // 默认
    public static readonly CommentFormat[] DefaultFormats = [CommentFormat.Json, CommentFormat.Txt];
    // 可选项
    public static readonly string[] AllFormatNames =
        [.. Enum.GetNames<CommentFormat>( ).Select(f => f.ToLower( ))];

    public static CommentFormat FromFormatName(string formatName)
    {
        return formatName switch
        {
            "json" => CommentFormat.Json,
            "txt" => CommentFormat.Txt,
            _ => CommentFormat.Json,
        };
    }
}
