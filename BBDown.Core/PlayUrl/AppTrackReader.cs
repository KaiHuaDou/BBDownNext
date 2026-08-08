using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core.Entity;
using BBDown.Core.Protobuf;
using BBDown.Core.Util;

using static BBDown.Core.Logger;

namespace BBDown.Core.PlayUrl;

// App 端走 gRPC 拿到的是强类型 PlayViewReply, 直接构建轨道,
// 不再序列化成网页那套 JSON 再解析回来
internal static class AppTrackReader
{
    internal static async Task<ParsedResult> FetchAsync(PlayUrlRequest req, CancellationToken ct = default)
    {
        var reply = await AppHelper.DoReqAsync(req.Aid, req.Cid, req.EpId, req.IsBangumi, req.Encoding, req.Cfg, ct: ct);
        var result = Build(reply, req.IsEpisode, req.Aid, req.Cid);
        result.RawResponse = JsonSerializer.Serialize(reply, JsonContext.Default.PlayViewReply);
        LogDebug(result.RawResponse);
        return result;
    }

    internal static ParsedResult Build(PlayViewReply resp, bool isEpisode, string aid, string cid)
    {
        ParsedResult result = new( );
        if (resp.VideoInfo == null)
        {
            return result;
        }

        var pDur = (int) (resp.VideoInfo.Timelength / 1000);
        result.Duration = pDur;
        CollectVideoTracks(result, resp.VideoInfo, pDur);
        CollectAudioTracks(result, resp.VideoInfo, pDur);

        if (!isEpisode)
        {
            return result;
        }

        CollectDubbingTracks(result, resp.PlayExtInfo?.PlayDubbingInfo, pDur, aid, cid);
        if (resp.Business != null)
        {
            ViewPointUtil.Append(result, resp.Business.ClipInfo.Select(clip => new ViewPoint( )
            {
                Title = clip.ToastText.Replace("即将跳过", ""),
                Start = clip.Start,
                End = clip.End
            }));
        }

        return result;
    }

    private static void CollectVideoTracks(ParsedResult result, VideoInfo info, int pDur)
    {
        foreach (var stream in info.StreamList)
        {
            // 仅提供分段(flv)地址的档位没有 dashVideo, 直接跳过
            if (stream.DashVideo == null)
            {
                continue;
            }

            var quality = (stream.StreamInfo?.Quality ?? 0).ToString( );
            Video v = new( )
            {
                Dur = pDur,
                Id = quality,
                Dfn = Config.GetQualityName(quality),
                // App 端不下发 bandwidth, 由体积和时长反推
                Bandwidth = pDur == 0 ? 0 : (long) (stream.DashVideo.Size * 8 / (ulong) pDur / 1000),
                BaseUrl = PickBaseUrl(stream.DashVideo.BaseUrl, stream.DashVideo.BackupUrl),
                Codecs = TrackFactory.VideoCodec(stream.DashVideo.Codecid.ToString( )),
                Size = stream.DashVideo.Size
            };
            if (!result.VideoTracks.Contains(v))
            {
                result.VideoTracks.Add(v);
            }
        }
    }

    private static void CollectAudioTracks(ParsedResult result, VideoInfo info, int pDur)
    {
        result.AudioTracks.AddRange(info.DashAudio.Select(item => BuildAudio(item, pDur, "M4A")));

        if (info.Flac?.Audio != null)
        {
            result.AudioTracks.Add(BuildAudio(info.Flac.Audio, pDur, "FLAC"));
        }

        if (info.Dolby?.Audio != null)
        {
            result.AudioTracks.Add(BuildAudio(info.Dolby.Audio, pDur, "E-AC-3"));
        }
    }

    private static void CollectDubbingTracks(ParsedResult result, PlayDubbingInfo? dubbing, int pDur, string aid, string cid)
    {
        if (dubbing == null)
        {
            return;
        }

        if (dubbing.BackgroundAudio != null)
        {
            result.BackgroundAudioTracks.AddRange(dubbing.BackgroundAudio.Audio.Select(item => BuildAudio(item, pDur, "M4A")));
        }

        result.RoleAudioList.AddRange(dubbing.RoleAudioList
            .SelectMany(role => role.AudioMaterialList)
            .Select(role => new AudioMaterialInfo( )
            {
                // proto2 未设置的 optional string 读出的是 "" 而非 null, 不能用 ?? 兜底
                Title = role.Title.Length != 0 ? role.Title : role.AudioId,
                PersonName = role.PersonName.Length != 0 ? role.PersonName : role.Edition,
                Path = $"{aid}/{aid}.{cid}.{role.AudioId}.m4a",
                Audio = [.. role.Audio.Select(item => BuildAudio(item, pDur, "M4A"))]
            }));
    }

    private static Audio BuildAudio(DashItem item, int pDur, string codecs)
    {
        var id = item.Id.ToString( );
        return new Audio( )
        {
            Id = id,
            Dfn = id,
            Dur = pDur,
            Bandwidth = item.Bandwidth / 1000,
            BaseUrl = PickBaseUrl(item.BaseUrl, item.BackupUrl),
            Codecs = codecs
        };
    }

    // 与 TrackFactory.PickBaseUrl(List<string>) 签名不同: 这里把 (baseUrl, backupUrl) 组建成列表后转交 TrackFactory
    private static string PickBaseUrl(string baseUrl, IEnumerable<string> backupUrl)
    {
        return TrackFactory.PickBaseUrl([baseUrl, .. backupUrl]);
    }
}