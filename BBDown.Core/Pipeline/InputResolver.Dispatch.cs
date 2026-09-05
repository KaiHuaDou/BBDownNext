using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;

using BBDown.Core.Live;
using BBDown.Core.Opus;

using static BBDown.Core.ResourceId;

namespace BBDown.Core.Pipeline;

/// <summary>
/// InputResolver 的形态分发部分：识别需走独立链路（非视频管道）的输入形态。
/// 纯字符串逻辑不触网，视频形态返回 false 由调用方按视频管道处理；
/// ss / md / 裸数字等需要网络换算的形态仍在主文件的 ResolveIdAsync 链路内完成。
/// </summary>
public static partial class InputResolver
{
    /// <summary>
    /// 识别需走独立链路（非视频管道）的输入形态：直播、专栏（opus / cv）、文集、空间图文 / 音频 / 动态、单音频（au）。
    /// CLI（RunApp）在视频管道之前调用它做早分流；ResolveUrlAsync / ResolveShorthandAsync 亦复用。
    /// </summary>
    public static bool TryDispatch(string input, [NotNullWhen(true)] out ResourceId? id)
    {
        id = null;
        if (LiveInputResolver.TryParse(input, out var live))
        {
            id = new LiveRoom(long.Parse(live.RoomId));
            return true;
        }

        if (OpusInputResolver.TryParse(input, out var opus))
        {
            id = new OpusArticle(long.TryParse(opus.OpusId, out var opusId) ? opusId : 0, long.TryParse(opus.CvId, out var cvId) ? cvId : 0);
            return true;
        }

        if (TryParseAudio(input, out var auId))
        {
            id = new Audio(auId);
            return true;
        }

        return TryParseCollection(input, out id);
    }

    // 单音频形态：URL（https://www.bilibili.com/audio/au12345，忽略 query / fragment）与简写（au12345）。
    // space.bilibili.com/{mid}/audio 是空间音频列表（TryParseCollection 域），路径无 au 尾段不会被此处误吞
    private static bool TryParseAudio(string input, out long auId)
    {
        auId = 0;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var path = input.Trim( ).Split('?', '#')[0];
        var idx = path.IndexOf("/audio/au", StringComparison.OrdinalIgnoreCase);
        string rest;
        if (idx >= 0)
        {
            rest = path[(idx + "/audio/au".Length)..];
        }
        else if (path.Length > IdPrefix.Au.Length
            && path.StartsWith(IdPrefix.Au, StringComparison.OrdinalIgnoreCase)
            && char.IsAsciiDigit(path[IdPrefix.Au.Length]))
        {
            rest = path[IdPrefix.Au.Length..];
        }
        else
        {
            return false;
        }

        // 取前导数字段（容忍 URL 尾随斜杠等杂字符），与 OpusInputResolver 的 TryTakeDigits 同语义
        var end = 0;
        while (end < rest.Length && char.IsAsciiDigit(rest[end]))
        {
            end++;
        }

        return end > 0 && long.TryParse(rest[..end], NumberStyles.None, CultureInfo.InvariantCulture, out auId);
    }

    // 集合形态（文集 / 空间图文 / 空间音频 / 空间动态）的纯字符串识别：URL 与简写共用
    private static bool TryParseCollection(string input, [NotNullWhen(true)] out ResourceId? id)
    {
        id = null;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var s = input.Trim( );

        // 文集 URL：https://www.bilibili.com/read/readlist/rl75249（路径尾段即 rlid，忽略 query / fragment）
        if (s.Contains("/read/readlist/", StringComparison.OrdinalIgnoreCase))
        {
            var path = s.Split('?', '#')[0];
            var last = path[(path.LastIndexOf('/') + 1)..];
            if (last.StartsWith(IdPrefix.Rl, StringComparison.OrdinalIgnoreCase)
                && last.Length > IdPrefix.Rl.Length
                && long.TryParse(last[IdPrefix.Rl.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var rlId))
            {
                id = new ReadList(rlId);
                return true;
            }

            return false;
        }

        // 空间子页 URL：图文 / 音频 / 动态（https://space.bilibili.com/{mid}/upload/opus 等）。
        // 必须在 Space 兜底（全部投稿视频）之前识别，否则 /upload/* 与 /dynamic 会被吞进视频列表
        if (s.Contains("/space.bilibili.com/", StringComparison.OrdinalIgnoreCase)
            && UidRegex( ).Match(s) is { Success: true } spaceMatch)
        {
            var mid = long.Parse(spaceMatch.Groups[1].Value);
            if (s.Contains("/upload/opus", StringComparison.OrdinalIgnoreCase))
            {
                id = new SpaceOpus(mid);
                return true;
            }

            // 旧版音频页 space.bilibili.com/{mid}/audio 与新版 /upload/audio 同义（/audio 判定两者通吃）
            if (s.Contains("/audio", StringComparison.OrdinalIgnoreCase))
            {
                id = new SpaceAudio(mid);
                return true;
            }

            if (s.Contains("/dynamic", StringComparison.OrdinalIgnoreCase))
            {
                id = new SpaceDynamic(mid);
                return true;
            }

            return false;
        }

        // 简写：rl75249 / readlist75249 / spaceOpus213741 / spaceAudio213741 / spaceDynamic213741
        if (TryPrefixDigits(s, IdPrefix.ReadList, out var readListId))
        {
            id = new ReadList(readListId);
            return true;
        }

        if (TryPrefixDigits(s, IdPrefix.Rl, out var rlShortId))
        {
            id = new ReadList(rlShortId);
            return true;
        }

        if (TryPrefixDigits(s, IdPrefix.SpaceOpus, out var spaceOpusMid))
        {
            id = new SpaceOpus(spaceOpusMid);
            return true;
        }

        if (TryPrefixDigits(s, IdPrefix.SpaceAudio, out var spaceAudioMid))
        {
            id = new SpaceAudio(spaceAudioMid);
            return true;
        }

        if (TryPrefixDigits(s, IdPrefix.SpaceDynamic, out var spaceDynamicMid))
        {
            id = new SpaceDynamic(spaceDynamicMid);
            return true;
        }

        return false;
    }

    // 简写前缀 + 纯数字匹配（大小写不敏感），前缀后必须紧跟数字
    private static bool TryPrefixDigits(string s, string prefix, out long value)
    {
        value = 0;
        return s.Length > prefix.Length
            && s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && s[prefix.Length..].All(char.IsDigit)
            && long.TryParse(s[prefix.Length..], out value);
    }
}
