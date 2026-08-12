using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using BBDown.Core;

using static BBDown.Core.Logger;
using BBDown.Core.Util;
using static BBDown.Core.Util.FileNameUtil;
using BBDown.Core.Entity;
using BBDown.Core.Download;

namespace BBDown.Core.Download;
public static partial class SavePath
{
    public static string SinglePageDefaultSavePath { get; } = "<videoTitle>";
    public static string MultiPageDefaultSavePath { get; } = "<videoTitle>/[P<pageNumberWithZero>]<pageTitle>";

    // 1. 多 P; 2. 只有 1P, 但是是番剧，尚未完结时 按照多 P 处理
    internal static string Resolve(DownloadRequest myOption, int pagesCount, bool isBangumi, bool isBangumiEnd)
    {
        return pagesCount > 1 || (isBangumi && !isBangumiEnd)
            ? (string.IsNullOrEmpty(myOption.MultiFilePattern) ? MultiPageDefaultSavePath : myOption.MultiFilePattern)
            : (string.IsNullOrEmpty(myOption.FilePattern) ? SinglePageDefaultSavePath : myOption.FilePattern);
    }

    internal static string Build(WorkContext ctx, PageContext pageCtx, Video? videoTrack, Audio? audioTrack)
    {
        var relative = Format(ctx.SavePathFormat, pageCtx.Title, videoTrack, audioTrack, pageCtx.Page, pageCtx.PagesCount, ctx.Fetch.ApiType, pageCtx.PubTime);
        if (pageCtx.IsPreview)
        {
            relative = ApplyPreviewPrefix(relative);
        }

        return Path.Combine(ctx.Run.WorkDir, relative);
    }

    // 多 P 模板形如 <videoTitle>/[P01]<pageTitle>，前缀只能加到最后一段，否则会造出带前缀的目录
    internal static string ApplyPreviewPrefix(string relative)
    {
        var i = relative.LastIndexOfAny(['/', '\\']);
        return i < 0 ? "[试看]" + relative : relative[..(i + 1)] + "[试看]" + relative[(i + 1)..];
    }

    internal static string Format(string savePathFormat, string title, Video? videoTrack, Audio? audioTrack, Page p, int pagesCount, ApiType apiType, long pubTime)
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
                "pageNumber" => p.Index.ToString( ),
                "pageNumberWithZero" => p.Index.ToString( ).PadLeft(pagesCount.ToString( ).Length, '0'),
                "pageTitle" => GetValidFileName(p.Title),
                "bvid" => p.Bvid,
                "aid" => p.Aid,
                "cid" => p.Cid,
                "ownerName" => p.OwnerName == null ? "" : GetValidFileName(p.OwnerName),
                "ownerMid" => p.OwnerMid ?? "",
                "dfn" => videoTrack == null ? "" : videoTrack.Dfn,
                "res" => videoTrack == null ? "" : videoTrack.Res,
                "fps" => videoTrack == null ? "" : videoTrack.Fps,
                "videoCodecs" => videoTrack == null ? "" : videoTrack.Codecs,
                "videoBandwidth" => videoTrack == null ? "" : videoTrack.Bandwidth.ToString( ),
                "audioCodecs" => audioTrack == null ? "" : audioTrack.Codecs,
                "audioBandwidth" => audioTrack == null ? "" : audioTrack.Bandwidth.ToString( ),
                "publishDate" => Utils.FormatTimeStamp(pubTime, defaultDateFormat),
                "videoDate" => Utils.FormatTimeStamp(p.PubTime, defaultDateFormat),
                "apiType" => apiType.ToString( ).ToUpperInvariant( ),
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
