using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

using BBDown.Core.Entity;

using static BBDown.Core.PlayUrl.TrackFactory;
using static BBDown.Core.Util.JsonUtil;

namespace BBDown.Core.PlayUrl;

/// <summary>
/// DASH 响应（JSON）到轨道实体的解析。纯函数：输入已解析好的 <see cref="JsonElement"/>，不发任何网络请求——
/// 二次请求（免二压）由编排层负责，见 <see cref="Parser.ExtractTracksAsync"/>。
/// </summary>
internal static class DashTrackReader
{
    // 收两份 playurl 响应：firstRoot 来自按请求画质(qn)的首次响应，maxQnRoot 来自按最高画质(MaxQn)的二次响应。
    // 视频轨取两次并集（免二压档位只在二次响应出现），音轨优先取二次响应（降级时回退到首次）。
    // pDur 必须用 firstRoot：它是充电专属试看的判据之一（等价点 A）。
    internal static void Collect(ParsedResult result, JsonElement firstRoot, JsonElement maxQnRoot, bool tvApi)
    {
        var pDur = ReadDuration(firstRoot);
        result.Duration = pDur;
        CollectVideoTracks(result, firstRoot, pDur, tvApi);
        CollectVideoTracks(result, maxQnRoot, pDur, tvApi);

        // 二次请求偶尔返回降级响应(限流/无 dash 节点)，此时沿用首次结果的音轨而不是丢弃。
        // 回退判据从"dash.audio 是否存在"改为"能否收集到任何音轨（含 dolby/flac）"，避免杜比/Hi-Res-only 片源被丢（§2.7）
        var audioRoot = HasAnyAudio(maxQnRoot) ? maxQnRoot : firstRoot;
        CollectAudioTracks(result, audioRoot, pDur, tvApi);
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
        if (result.VideoTracks.Any(v => v.id == qn))
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
                v.res = node.GetProperty("width").ToString( ) + "x" + node.GetProperty("height").ToString( );
                v.fps = node.GetProperty("frame_rate").ToString( );
            }

            if (!result.VideoTracks.Contains(v))
            {
                result.VideoTracks.Add(v);
            }
        }
    }

    // 判断某份 playurl 响应里是否存在任何可收集的音轨（dash.audio 数组，或 dolby/flac 节点），
    // 用于"二次请求降级时回退到首次响应"的判据（§2.7）
    private static bool HasAnyAudio(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("dash", out var dash) || dash.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (dash.TryGetProperty("audio", out var audio) && audio.ValueKind == JsonValueKind.Array && audio.GetArrayLength( ) > 0)
        {
            return true;
        }

        if (dash.TryGetProperty("dolby", out var dolby) && dolby.ValueKind == JsonValueKind.Object
            && dolby.TryGetProperty("audio", out var dolbyAudio) && dolbyAudio.ValueKind == JsonValueKind.Array && dolbyAudio.GetArrayLength( ) > 0)
        {
            return true;
        }

        if (dash.TryGetProperty("flac", out var flac) && flac.ValueKind == JsonValueKind.Object
            && flac.TryGetProperty("audio", out var flacAudio) && flacAudio.ValueKind != JsonValueKind.Null)
        {
            return true;
        }

        return false;
    }

    private static void CollectAudioTracks(ParsedResult result, JsonElement root, int pDur, bool tvApi)
    {
        // 即使 dash.audio 为 null（杜比/Hi-Res-only 片源），也要从 root 收集 dolby/flac 音轨（§2.7）；
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
