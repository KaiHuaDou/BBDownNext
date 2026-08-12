using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using static BBDown.Core.Logger;
using static BBDown.Core.Util.HTTPUtil;
using static BBDown.Core.Util.JsonUtil;

namespace BBDown.Core.Live;

public static class LiveFetcher
{
    private const string HttpStream = "http_stream";
    private const string Flv = "flv";

    /// <summary>
    /// 取直播间信息。<paramref name="target"/> 里的号码可能是短号，room_init 负责换算成真实房间号，
    /// 之后所有接口都必须用真实房间号。
    /// </summary>
    public static async Task<LiveRoomInfo> FetchRoomAsync(LiveTarget target, AppConfig cfg, CancellationToken ct = default)
    {
        using var initDoc = JsonDocument.Parse(await GetWebSourceAsync($"{BiliApi.LiveRoomInit}?id={target.RoomId}", cfg, null, ct));
        var init = GetApiData(initDoc.RootElement, "直播间信息");

        var roomId = ReadNumberAsString(init, "room_id");
        if (string.IsNullOrEmpty(roomId))
        {
            throw new InvalidOperationException($"直播间 {target.RoomId} 不存在");
        }

        var baseUrl = $"{BiliApi.LiveRoomBaseInfo}?room_ids={roomId}&req_biz=video";
        using var baseDoc = JsonDocument.Parse(await GetWebSourceAsync(baseUrl, cfg, null, ct));
        var baseInfo = ReadRoomBaseInfo(GetApiData(baseDoc.RootElement, "直播间详情"), roomId);

        return new LiveRoomInfo(
            RoomId: roomId,
            ShortId: ReadNumberAsString(init, "short_id"),
            Uid: ReadNumberAsString(init, "uid"),
            Uname: ReadString(baseInfo, "uname"),
            Title: ReadString(baseInfo, "title"),
            LiveStatus: ReadInt(init, "live_status") ?? 0,
            Encrypted: ReadBool(init, "encrypted"),
            PwdVerified: ReadBool(init, "pwd_verified"),
            Cover: ReadString(baseInfo, "cover"));
    }

    /// <summary>
    /// 取直播流地址。返回 <c>null</c> 表示当前拿不到流（未开播 / 已下播），调用方据此决定重试还是收尾。
    /// </summary>
    public static async Task<LivePlayInfo?> FetchPlayInfoAsync(string roomId, int qn, AppConfig cfg, CancellationToken ct = default)
    {
        var url = $"{BiliApi.LiveRoomPlayInfo}?room_id={roomId}&protocol=0&format=0&codec=0,1"
                  + $"&qn={qn.ToString(CultureInfo.InvariantCulture)}&platform=web&ptype=8&dolby=5&panorama=1";
        using var doc = JsonDocument.Parse(await GetWebSourceAsync(url, cfg, null, ct));
        var data = GetApiData(doc.RootElement, "直播流地址");
        return ParsePlayInfo(data, qn);
    }

    /// <summary>
    /// 解析 getRoomPlayInfo 的 data 节点。只取 http_stream + flv：BBDown 的混流链路按连续字节流设计，
    /// hls/fmp4 的分片语义完全不同。
    /// </summary>
    internal static LivePlayInfo? ParsePlayInfo(JsonElement data, int requestedQn)
    {
        if (ReadInt(data, "live_status") != 1)
        {
            return null;
        }

        if (!data.TryGetProperty("playurl_info", out var playUrlInfo) || playUrlInfo.ValueKind != JsonValueKind.Object
            || !playUrlInfo.TryGetProperty("playurl", out var playUrl) || playUrl.ValueKind != JsonValueKind.Object
            || !playUrl.TryGetProperty("stream", out var streams) || streams.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var candidates = new List<LiveStreamCandidate>( );
        var acceptQn = new List<int>( );
        var actualQn = 0;

        // avc 兼容性优于 hevc，同清晰度下优先；只有 hevc 时仍然接受
        foreach (var preferred in (ReadOnlySpan<string>) ["avc", "hevc"])
        {
            foreach (var stream in streams.EnumerateArray( ))
            {
                if (ReadString(stream, "protocol_name") != HttpStream
                    || !stream.TryGetProperty("format", out var formats) || formats.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var format in formats.EnumerateArray( ))
                {
                    if (ReadString(format, "format_name") != Flv
                        || !format.TryGetProperty("codec", out var codecs) || codecs.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    foreach (var codec in codecs.EnumerateArray( ))
                    {
                        var codecName = ReadString(codec, "codec_name");
                        if (codecName != preferred)
                        {
                            continue;
                        }

                        if (ReadBool(codec, "drm"))
                        {
                            LogWarn($"直播流 {codecName} 轨道受保护，已跳过");
                            continue;
                        }

                        var currentQn = ReadInt(codec, "current_qn") ?? 0;
                        if (candidates.Count == 0)
                        {
                            actualQn = currentQn;
                            acceptQn.AddRange(ReadIntArray(codec, "accept_qn"));
                        }

                        candidates.AddRange(BuildCandidates(codec, codecName, currentQn));
                    }
                }
            }
        }

        return candidates.Count == 0 ? null : new LivePlayInfo(requestedQn, actualQn, acceptQn, candidates);
    }

    private static IEnumerable<LiveStreamCandidate> BuildCandidates(JsonElement codec, string codecName, int currentQn)
    {
        var baseUrl = ReadString(codec, "base_url");
        if (baseUrl.Length == 0 || !codec.TryGetProperty("url_info", out var urlInfos) || urlInfos.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var info in urlInfos.EnumerateArray( ))
        {
            var host = ReadString(info, "host");
            if (host.Length == 0)
            {
                continue;
            }

            yield return new LiveStreamCandidate(
                Url: BuildStreamUrl(host, baseUrl, ReadString(info, "extra")),
                Host: host,
                ProtocolName: HttpStream,
                FormatName: Flv,
                CodecName: codecName,
                CurrentQn: currentQn);
        }
    }

    /// <summary>
    /// 拼接播放地址。B 站的 <c>base_url</c> 自带尾部 <c>?</c>，直接三段相连即可；
    /// 这里仍按实际结尾补分隔符，防止接口哪天改掉这个约定就静默拼出 404 地址。
    /// </summary>
    internal static string BuildStreamUrl(string host, string baseUrl, string extra)
    {
        var url = host.TrimEnd('/') + baseUrl;
        if (extra.Length == 0)
        {
            return url;
        }

        if (url.EndsWith('?') || url.EndsWith('&'))
        {
            return url + extra;
        }

        return url + (url.Contains('?', StringComparison.Ordinal) ? '&' : '?') + extra;
    }

    // getRoomBaseInfo 按 by_room_ids 字典返回，键是房间号字符串
    private static JsonElement ReadRoomBaseInfo(JsonElement data, string roomId)
    {
        if (data.TryGetProperty("by_room_ids", out var byRoomIds) && byRoomIds.ValueKind == JsonValueKind.Object
            && byRoomIds.TryGetProperty(roomId, out var room))
        {
            return room;
        }

        return default;
    }

    private static string ReadString(JsonElement element, string name)
    {
        return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString( ) ?? ""
            : "";
    }

    private static int? ReadInt(JsonElement element, string name)
    {
        return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32( )
            : null;
    }

    private static bool ReadBool(JsonElement element, string name)
    {
        return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
    }

    private static string ReadNumberAsString(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value))
        {
            return "";
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number => value.GetRawText( ),
            JsonValueKind.String => value.GetString( ) ?? "",
            _ => ""
        };
    }

    private static IEnumerable<int> ReadIntArray(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var array) || array.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var item in array.EnumerateArray( ))
        {
            if (item.ValueKind == JsonValueKind.Number)
            {
                yield return item.GetInt32( );
            }
        }
    }
}
