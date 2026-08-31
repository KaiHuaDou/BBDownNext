using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using static BBDown.Core.Util.HTTPUtil;
using static BBDown.Core.Util.JsonUtil;

namespace BBDown.Core.Music;

/// <summary>
/// 音频投稿（AU）的抓取：song/info（元信息）、web/url（播放流，web 端恒 192K）、song/lyric（歌词文本）。
/// 付费 / 大会员曲目未登录时 web/url 返回试听片段（type=-1），由调用方提示。
/// 命名空间用 Music（对齐 music-service）而非 Audio：后者与 <see cref="Entity.Audio"/>（音轨实体）同名冲突。
/// </summary>
public static class AudioFetcher
{
    public static async Task<AudioInfo> FetchInfoAsync(long auId, AppConfig cfg, CancellationToken ct = default)
    {
        var api = $"{BiliApi.AudioSongInfo}?sid={auId}";
        using var doc = JsonDocument.Parse(await GetWebSourceAsync(api, cfg, null, ct));
        var data = GetData(doc.RootElement, "音频信息", auId);
        return new AudioInfo(
            auId,
            ReadStr(data, "title"),
            ReadStr(data, "uname"),
            ReadStr(data, "cover"),
            ReadLong(data, "duration"),
            ReadLong(data, "passtime"));
    }

    public static async Task<AudioPlayUrl> FetchPlayUrlAsync(long auId, AppConfig cfg, CancellationToken ct = default)
    {
        // privilege=2 请求完整版；无权限时服务端降级为试听片段（type=-1）
        var api = $"{BiliApi.AudioSongUrl}?sid={auId}&privilege=2&quality=2";
        using var doc = JsonDocument.Parse(await GetWebSourceAsync(api, cfg, null, ct));
        var data = GetData(doc.RootElement, "音频播放流", auId);
        var type = (int) ReadLong(data, "type");
        if (!TryGetArray(data, "cdns", out var cdns))
        {
            throw new InvalidOperationException($"获取音频播放流失败：响应缺少 cdns 字段（au{auId}）");
        }

        foreach (var cdn in cdns.EnumerateArray( ))
        {
            if (cdn.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(cdn.GetString( )))
            {
                return new AudioPlayUrl(cdn.GetString( )!, type);
            }
        }

        throw new InvalidOperationException($"获取音频播放流失败：响应中无可用地址（au{auId}）");
    }

    /// <summary>歌词文本（lrc 格式）。无歌词返回空串；歌词为附加产物，失败由调用方降级告警。</summary>
    public static async Task<string> FetchLyricAsync(long auId, AppConfig cfg, CancellationToken ct = default)
    {
        var api = $"{BiliApi.AudioLyric}?sid={auId}";
        using var doc = JsonDocument.Parse(await GetWebSourceAsync(api, cfg, null, ct));
        // data 为字符串（lrc 文本）；无歌词时 data 为 null / 缺失
        return doc.RootElement.ValueKind == JsonValueKind.Object
            && doc.RootElement.TryGetProperty("data", out var lyric)
            && lyric.ValueKind == JsonValueKind.String
            ? lyric.GetString( ) ?? ""
            : "";
    }

    // 外层 code 非零时按音频域错误码转可读信息（GetApiData 的通用文案不含音频语义）
    private static JsonElement GetData(JsonElement root, string label, long auId)
    {
        if (TryGetObject(root, "data", out var data))
        {
            return data;
        }

        var (code, message) = ReadApiError(root);
        throw new InvalidOperationException(code switch
        {
            7201006 => $"获取{label}失败：该音频不存在或已下架（au{auId}）",
            72010027 => $"获取{label}失败：版权音乐已重定向，暂不支持（au{auId}）",
            4511006 => $"获取{label}失败：该音频无法播放（au{auId}）",
            _ => $"获取{label}失败(code={code})：{message}（au{auId}）",
        });
    }

    private static string ReadStr(JsonElement obj, string name)
    {
        return obj.ValueKind == JsonValueKind.Object
            && obj.TryGetProperty(name, out var v)
            && v.ValueKind == JsonValueKind.String
            ? v.GetString( ) ?? ""
            : "";
    }

    private static long ReadLong(JsonElement obj, string name)
    {
        return obj.ValueKind == JsonValueKind.Object
            && obj.TryGetProperty(name, out var v)
            && v.ValueKind == JsonValueKind.Number
            && v.TryGetInt64(out var value)
            ? value
            : 0;
    }
}
