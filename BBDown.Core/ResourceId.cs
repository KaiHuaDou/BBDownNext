using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json.Serialization;

namespace BBDown.Core;

/// <summary>
/// 各类资源 id 的统一类型，替代字符串形态的内部 id（如 "ep:ss2539"、favId:123:456）。
/// 值相等性天然支持去重键；子类型即形态，消费点按类型分发而非解析字符串。
/// serve API 边界经 <see cref="ResourceIdJsonConverter"/> 序列化为规范字符串
/// （如 "season2539"，与 <see cref="TryParse"/> 的路径参数编码一致），内部仍保持类型。
/// </summary>
[JsonConverter(typeof(ResourceIdJsonConverter))]
public abstract record ResourceId
{
    /// <summary>普通视频 av 号</summary>
    public sealed record Av(long Aid) : ResourceId;

    /// <summary>单集番剧 ep_id</summary>
    public sealed record Ep(long EpId) : ResourceId;

    /// <summary>整季番剧 season_id（原 "ep:ss{id}" 打标形态）</summary>
    public sealed record Season(long SeasonId) : ResourceId;

    /// <summary>课程单集 ep_id</summary>
    public sealed record CheeseEp(long EpId) : ResourceId;

    /// <summary>整季课程 season_id（原 "cheese:ss{id}" 打标形态）</summary>
    public sealed record CheeseSeason(long SeasonId) : ResourceId;

    /// <summary>收藏夹（Fid 为 0 表示取默认收藏夹）</summary>
    public sealed record Fav(long Fid, long Mid) : ResourceId;

    /// <summary>合集 biz_id</summary>
    public sealed record MediaList(long BizId) : ResourceId;

    /// <summary>系列 biz_id</summary>
    public sealed record Series(long BizId) : ResourceId;

    /// <summary>UP 主空间 mid</summary>
    public sealed record Space(long Mid) : ResourceId;

    /// <summary>稍后再看列表</summary>
    public sealed record WatchLater : ResourceId;

    /// <summary>直播间（房间号，可为短号，短号在 live_init 时换真实房间号）</summary>
    public sealed record LiveRoom(long RoomId) : ResourceId;

    /// <summary>专栏（opus 动态 id 与 cv id 是同一文章的两个 id，至少一个非 0）</summary>
    public sealed record OpusArticle(long OpusId, long CvId) : ResourceId;

    /// <summary>文集（专栏合集）rlid</summary>
    public sealed record ReadList(long RlId) : ResourceId;

    /// <summary>UP 主空间全部图文 / 专栏投稿（动态流过滤，仅 MAJOR_TYPE_OPUS）</summary>
    public sealed record SpaceOpus(long Mid) : ResourceId;

    /// <summary>UP 主空间全部音频投稿（AU 号列表）</summary>
    public sealed record SpaceAudio(long Mid) : ResourceId;

    /// <summary>UP 主空间动态流（图文 / 视频 / 转发混合分发）</summary>
    public sealed record SpaceDynamic(long Mid) : ResourceId;

    /// <summary>单条音频投稿 au 号</summary>
    public sealed record Audio(long AuId) : ResourceId;

    /// <summary>
    /// 解析 serve API 路径参数的规范 id（"&lt;type&gt;&lt;值&gt;" 无冒号形态，如 "season2539"；
    /// fav 双值为 "fav&lt;fid&gt;_&lt;mid&gt;"，watchLater 无值）。仅接受规范形态，不接受用户输入简写。
    /// </summary>
    public static bool TryParse(string input, [NotNullWhen(true)] out ResourceId? id)
    {
        id = null;
        if (string.IsNullOrEmpty(input))
        {
            return false;
        }

        if (input == "watchLater")
        {
            id = new WatchLater( );
            return true;
        }

        // 前缀按长度降序匹配，长前缀（cheeseSeason/cheeseEp）不被短前缀误吞
        foreach (var prefix in TypePrefixes)
        {
            if (input.StartsWith(prefix, StringComparison.Ordinal) && TryBuild(prefix, input[prefix.Length..], out id))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryBuild(string prefix, string rest, [NotNullWhen(true)] out ResourceId? id)
    {
        id = null;
        switch (prefix)
        {
            case "av":
                if (TryLong(rest, out var aid))
                {
                    id = new Av(aid);
                    return true;
                }

                break;
            case "ep":
                if (TryLong(rest, out var epId))
                {
                    id = new Ep(epId);
                    return true;
                }

                break;
            case "season":
                if (TryLong(rest, out var seasonId))
                {
                    id = new Season(seasonId);
                    return true;
                }

                break;
            case "cheeseEp":
                if (TryLong(rest, out var cheeseEpId))
                {
                    id = new CheeseEp(cheeseEpId);
                    return true;
                }

                break;
            case "cheeseSeason":
                if (TryLong(rest, out var cheeseSeasonId))
                {
                    id = new CheeseSeason(cheeseSeasonId);
                    return true;
                }

                break;
            case "mediaList":
                if (TryLong(rest, out var listBizId))
                {
                    id = new MediaList(listBizId);
                    return true;
                }

                break;
            case "series":
                if (TryLong(rest, out var seriesBizId))
                {
                    id = new Series(seriesBizId);
                    return true;
                }

                break;
            case "space":
                if (TryLong(rest, out var mid))
                {
                    id = new Space(mid);
                    return true;
                }

                break;
            case "live":
                if (TryLong(rest, out var roomId))
                {
                    id = new LiveRoom(roomId);
                    return true;
                }

                break;
            case "opus":
                if (TryLong(rest, out var opusId))
                {
                    id = new OpusArticle(opusId, 0);
                    return true;
                }

                break;
            case "readlist":
            case "rl":
                if (TryLong(rest, out var rlId))
                {
                    id = new ReadList(rlId);
                    return true;
                }

                break;
            case "spaceOpus":
                if (TryLong(rest, out var spaceOpusMid))
                {
                    id = new SpaceOpus(spaceOpusMid);
                    return true;
                }

                break;
            case "spaceAudio":
                if (TryLong(rest, out var spaceAudioMid))
                {
                    id = new SpaceAudio(spaceAudioMid);
                    return true;
                }

                break;
            case "spaceDynamic":
                if (TryLong(rest, out var spaceDynamicMid))
                {
                    id = new SpaceDynamic(spaceDynamicMid);
                    return true;
                }

                break;

            case "au":
                if (TryLong(rest, out var auId))
                {
                    id = new Audio(auId);
                    return true;
                }

                break;
            case "cv":
                if (TryLong(rest, out var cvId))
                {
                    id = new OpusArticle(0, cvId);
                    return true;
                }

                break;
            case "fav":
                var sep = rest.IndexOf('_');
                if (sep > 0 && TryLong(rest[..sep], out var fid) && TryLong(rest[(sep + 1)..], out var favMid))
                {
                    id = new Fav(fid, favMid);
                    return true;
                }

                break;
        }

        return false;
    }

    // 前缀按长度降序（spaceDynamic 12 / cheeseSeason 12 > spaceAudio 10 > mediaList 9 / spaceOpus 9 > cheeseEp 8 / readlist 8
    // > season 6 > series/space 5 > opus/live 4 > fav 3 > ep/cv/av/au/rl 2），
    // 未来若出现包含关系（如新增 "cheese" 前缀），长前缀仍优先匹配
    private static readonly string[] TypePrefixes =
        ["spaceDynamic", "cheeseSeason", "spaceAudio", "mediaList", "spaceOpus", "cheeseEp", "readlist", "season", "series", "space", "opus", "live", "fav", "ep", "cv", "au", "av", "rl"];

    // 仅接受纯数字（无符号/空白/千分位），保证规范形态与非法输入严格区分
    private static bool TryLong(string value, out long result)
    {
        return long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out result);
    }
}
