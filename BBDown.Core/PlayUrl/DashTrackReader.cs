using System.Collections.Generic;
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
        AppendDolbyAndHiRes(audio, root, tvApi);
        foreach (var node in audio)
        {
            result.AudioTracks.Add(BuildAudio(node, pDur, NormalizeAudioCodec(node.GetProperty("codecs").ToString( ))));
        }
    }

    private static void AppendDolbyAndHiRes(List<JsonElement> audio, JsonElement root, bool tvApi)
    {
        if (tvApi || root.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (!root.TryGetProperty("dash", out var dash) || dash.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        //处理杜比音频
        if (dash.TryGetProperty("dolby", out var dolby) && dolby.ValueKind == JsonValueKind.Object
            && dolby.TryGetProperty("audio", out var dolbyAudio) && dolbyAudio.ValueKind == JsonValueKind.Array)
        {
            audio.AddRange(dolbyAudio.EnumerateArray( ));
        }

        //处理Hi-Res无损
        if (dash.TryGetProperty("flac", out var hiRes) && hiRes.ValueKind == JsonValueKind.Object
            && hiRes.TryGetProperty("audio", out var hiResAudio) && hiResAudio.ValueKind != JsonValueKind.Null)
        {
            audio.Add(hiResAudio);
        }
    }
}
