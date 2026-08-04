using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using static BBDown.Core.Entity.Entity;
using static BBDown.Core.Logger;
using static BBDown.Core.Util.FileNameUtil;

namespace BBDown.Util;

internal static partial class SavePath
{
    public static string SinglePageDefaultSavePath { get; } = "<videoTitle>";
    public static string MultiPageDefaultSavePath { get; } = "<videoTitle>/[P<pageNumberWithZero>]<pageTitle>";

    // 1. 多 P; 2. 只有 1P, 但是是番剧，尚未完结时 按照多 P 处理
    internal static string Resolve(DownloadOptions myOption, int pagesCount, bool isBangumi, bool isBangumiEnd)
    {
        return pagesCount > 1 || (isBangumi && !isBangumiEnd)
            ? (string.IsNullOrEmpty(myOption.MultiFilePattern) ? MultiPageDefaultSavePath : myOption.MultiFilePattern)
            : (string.IsNullOrEmpty(myOption.FilePattern) ? SinglePageDefaultSavePath : myOption.FilePattern);
    }

    internal static string Build(WorkContext ctx, PageContext pageCtx, Video? videoTrack, Audio? audioTrack)
    {
        var relative = Format(ctx.SavePathFormat, pageCtx.Title, videoTrack, audioTrack, pageCtx.Page, pageCtx.PagesCount, ctx.ApiType, pageCtx.PubTime);
        if (pageCtx.IsPreview)
        {
            relative = ApplyPreviewPrefix(relative);
        }

        return Path.Combine(ctx.WorkDir, relative);
    }

    // 多 P 模板形如 <videoTitle>/[P01]<pageTitle>，前缀只能加到最后一段，否则会造出带前缀的目录
    internal static string ApplyPreviewPrefix(string relative)
    {
        var i = relative.LastIndexOfAny(['/', '\\']);
        return i < 0 ? "[试看]" + relative : relative[..(i + 1)] + "[试看]" + relative[(i + 1)..];
    }

    internal static string Format(string savePathFormat, string title, Video? videoTrack, Audio? audioTrack, Page p, int pagesCount, string apiType, long pubTime)
    {
        var result = savePathFormat.Replace('\\', '/');
        var regex = InfoRegex( );
        var matches = regex.Matches(result).Cast<Match>( ).ToList( );
        var replacements = new List<(int Index, int Length, string Value)>(matches.Count);
        foreach (var m in matches)
        {
            var key = m.Groups[1].Value;

            //解析自定义日期格式
            var defaultDateFormat = "yyyy-MM-dd_HH-mm-ss";
            string[] prefixes = ["publishDate:", "videoDate:"];
            foreach (var prefix in prefixes)
            {
                if (key.StartsWith(prefix))
                {
                    defaultDateFormat = key[(key.IndexOf(':') + 1)..];
                    key = prefix.Replace(":", "");
                    break;
                }
            }

            var v = key switch
            {
                "videoTitle" => GetValidFileName(title),
                "pageNumber" => p.index.ToString( ),
                "pageNumberWithZero" => p.index.ToString( ).PadLeft(pagesCount.ToString( ).Length, '0'),
                "pageTitle" => GetValidFileName(p.title),
                "bvid" => p.bvid,
                "aid" => p.aid,
                "cid" => p.cid,
                "ownerName" => p.ownerName == null ? "" : GetValidFileName(p.ownerName),
                "ownerMid" => p.ownerMid ?? "",
                "dfn" => videoTrack == null ? "" : videoTrack.dfn,
                "res" => videoTrack == null ? "" : videoTrack.res,
                "fps" => videoTrack == null ? "" : videoTrack.fps,
                "videoCodecs" => videoTrack == null ? "" : videoTrack.codecs,
                "videoBandwidth" => videoTrack == null ? "" : videoTrack.bandwidth.ToString( ),
                "audioCodecs" => audioTrack == null ? "" : audioTrack.codecs,
                "audioBandwidth" => audioTrack == null ? "" : audioTrack.bandwidth.ToString( ),
                "publishDate" => Utils.FormatTimeStamp(pubTime, defaultDateFormat),
                "videoDate" => Utils.FormatTimeStamp(p.pubTime, defaultDateFormat),
                "apiType" => apiType,
                _ => UnknownPlaceholder(key)
            };
            replacements.Add((m.Index, m.Length, v ?? ""));
        }

        for (var i = replacements.Count - 1; i >= 0; i--)
        {
            var (index, length, value) = replacements[i];
            result = result.Remove(index, length).Insert(index, value);
        }

        if (!result.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)) { result += ".mp4"; }

        return result;
    }

    private static string UnknownPlaceholder(string key)
    {
        LogWarn($"未知的文件名变量 <{key}>，已原样保留");
        return $"<{key}>";
    }

    [GeneratedRegex("<([\\w:\\-.]+?)>")]
    private static partial Regex InfoRegex( );
}
