using System.Text.Json;

using BBDown.Core.Entity;

using static BBDown.Core.PlayUrl.TrackFactory;
using static BBDown.Core.Util.JsonUtil;

namespace BBDown.Core.PlayUrl;

/// <summary>
/// INTL（BiliPlus / 海外）playurl 响应（JSON）到轨道实体的解析。纯函数：输入已解析好的 <see cref="JsonElement"/>。
/// 与 DASH 字段名不同（id vs stream_info.quality、codecid 位置不同），故不强行与 DASH 共用构建逻辑，
/// 仅 <see cref="TrackFactory.BuildVideo"/> / <see cref="TrackFactory.BuildAudio"/> 这两处真正同形的逻辑复用。
/// </summary>
internal static class IntlTrackReader
{
    // intl 接口的有效载荷落在 data.video_info 下，只有它带 stream_list 才走 intl 分支
    internal static bool TryGetVideoInfo(JsonElement root, out JsonElement videoInfo)
    {
        videoInfo = default;
        if (!HasObject(root, "data"))
        {
            return false;
        }

        var data = root.GetProperty("data");
        if (!HasObject(data, "video_info"))
        {
            return false;
        }

        videoInfo = data.GetProperty("video_info");
        return TryGetArray(videoInfo, "stream_list", out _);
    }

    internal static void Collect(ParsedResult result, JsonElement videoInfo)
    {
        // 缺字段时不应抛 KeyNotFoundException（P1-6）
        var pDur = videoInfo.TryGetProperty("timelength", out var tl) ? tl.GetInt32( ) / 1000 : 0;
        result.Duration = pDur;

        foreach (var stream in videoInfo.GetProperty("stream_list").EnumerateArray( ))
        {
            if (!stream.TryGetProperty("dash_video", out var dashVideo))
            {
                continue;
            }

            if (dashVideo.GetProperty("base_url").ToString( ).Length == 0)
            {
                continue;
            }

            var v = BuildVideo(dashVideo, pDur, stream.GetProperty("stream_info").GetProperty("quality").ToString( ));
            if (!result.VideoTracks.Contains(v))
            {
                result.VideoTracks.Add(v);
            }
        }

        // 缺字段时不应抛 KeyNotFoundException（P1-6）
        if (videoInfo.TryGetProperty("dash_audio", out var dashAudioArr) && dashAudioArr.ValueKind == JsonValueKind.Array)
        {
            foreach (var node in dashAudioArr.EnumerateArray( ))
            {
                var a = BuildAudio(node, pDur, "M4A");
                if (!result.AudioTracks.Contains(a))
                {
                    result.AudioTracks.Add(a);
                }
            }
        }
    }
}
