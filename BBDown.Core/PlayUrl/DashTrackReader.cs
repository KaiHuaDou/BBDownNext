using System.Linq;
using System.Text.Json;

using BBDown.Core.Entity;

using static BBDown.Core.PlayUrl.TrackFactory;
using static BBDown.Core.Util.JsonUtil;

namespace BBDown.Core.PlayUrl;

/// <summary>
/// DASH 响应（JSON）到轨道实体的解析。纯函数：输入已解析好的 <see cref="JsonElement"/>，不发任何网络请求——
/// 轨道收集基于编排层单次 MaxQn 请求的响应，见 <see cref="Parser.ExtractTracksAsync"/>。
/// </summary>
internal static class DashTrackReader
{
    // 单份 MaxQn 响应已含全部可用档位：视频轨按 Id 去重收集，音轨（含 dolby/flac）一并收集。
    internal static void Collect(ParsedResult result, JsonElement root, bool tvApi)
    {
        var pDur = ReadDuration(root);
        result.Duration = pDur;
        CollectVideoTracks(result, root, pDur, tvApi);
        CollectAudioTracks(result, root, pDur, tvApi);
    }

    internal static int ReadDuration(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return 0;
        }

        if (root.TryGetProperty("timelength", out var timelength) && timelength.TryGetInt32(out var ms))
        {
            return ms / 1000;
        }

        if (root.TryGetProperty("dash", out var dash) && dash.ValueKind == JsonValueKind.Object
            && dash.TryGetProperty("duration", out var duration) && duration.TryGetInt32(out var seconds))
        {
            return seconds;
        }

        return 0;
    }

    // support_formats 声明了某档位、dash 里却没有对应轨道 => 账号权限不够（need_vip / need_login）。
    // 纯判定，不做任何 IO；打印交给编排层（Parser）
    internal static bool DeclaredButMissing(JsonElement root, ParsedResult result, string qn)
    {
        if (result.VideoTracks.Any(v => v.Id == qn))
        {
            return false;
        }

        var formats = ArrayAtPath(root, "support_formats");
        return formats?.Any(f => f.TryGetProperty("quality", out var q) && q.ToString( ) == qn) == true;
    }

    private static void CollectVideoTracks(ParsedResult result, JsonElement root, int pDur, bool tvApi)
    {
        var video = ArrayAtPath(root, "dash", "video");
        if (video == null)
        {
            return;
        }

        foreach (var node in video)
        {
            var v = BuildVideo(node, pDur);
            if (!tvApi)
            {
                v.Res = node.GetProperty("width").ToString( ) + "x" + node.GetProperty("height").ToString( );
                v.Fps = node.GetProperty("frame_rate").ToString( );
            }

            if (!result.VideoTracks.Contains(v))
            {
                result.VideoTracks.Add(v);
            }
        }
    }

    private static void CollectAudioTracks(ParsedResult result, JsonElement root, int pDur, bool tvApi)
    {
        // 即使 dash.Audio 为 null（杜比/Hi-Res-only 片源），也要从 root 收集 dolby/flac 音轨（§2.7）；
        // 旧实现在此提前 return，会连带丢掉杜比/FLAC
        var audio = ArrayAtPath(root, "dash", "audio") ?? [];
        foreach (var node in audio)
        {
            var codecs = NormalizeAudioCodec(node.GetProperty("codecs").ToString( ));
            var id = node.GetProperty("id").ToString( );
            result.AudioTracks.Add(BuildAudio(node, pDur, codecs, Config.GetAudioQualityName(id)));
        }

        AppendDolbyAndHiRes(result, root, pDur, tvApi);
    }

    private static void AppendDolbyAndHiRes(ParsedResult result, JsonElement root, int pDur, bool tvApi)
    {
        if (tvApi || root.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (!root.TryGetProperty("dash", out var dash) || dash.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        // 处理杜比音频：type 区分普通杜比音效(1)与全景杜比音效(2)，id 恒为 30250
        if (dash.TryGetProperty("dolby", out var dolby) && dolby.ValueKind == JsonValueKind.Object
            && dolby.TryGetProperty("audio", out var dolbyAudio) && dolbyAudio.ValueKind == JsonValueKind.Array)
        {
            var type = dolby.TryGetProperty("type", out var t) && t.TryGetInt32(out var ti) ? ti : 0;
            foreach (var node in dolbyAudio.EnumerateArray( ))
            {
                var codecs = NormalizeAudioCodec(node.GetProperty("codecs").ToString( ));
                var id = node.GetProperty("id").ToString( );
                result.AudioTracks.Add(BuildAudio(node, pDur, codecs, Config.GetAudioQualityName(id, type)));
            }
        }

        // 处理 Hi-Res 无损：flac.audio 为单对象（非数组）
        if (dash.TryGetProperty("flac", out var hiRes) && hiRes.ValueKind == JsonValueKind.Object
            && hiRes.TryGetProperty("audio", out var hiResAudio) && hiResAudio.ValueKind != JsonValueKind.Null)
        {
            var codecs = NormalizeAudioCodec(hiResAudio.GetProperty("codecs").ToString( ));
            var id = hiResAudio.GetProperty("id").ToString( );
            result.AudioTracks.Add(BuildAudio(hiResAudio, pDur, codecs, Config.GetAudioQualityName(id)));
        }
    }
}
