using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

using BBDown.Core.Entity;

using static BBDown.Core.PlayUrl.TrackFactory;

namespace BBDown.Core.PlayUrl;

/// <summary>
/// FLV 响应（JSON）到轨道实体的解析。纯函数：输入按最高画质(MaxQn)请求得到的 playurl 响应节点，
/// 收集分段与单一最高清视频轨。FLV 强制 qn=127 忽略 -q，故由编排层负责 MaxQn 请求，本类不发网络请求。
/// </summary>
internal static class FlvTrackReader
{
    internal static void Collect(ParsedResult result, JsonElement root)
    {
        double size = 0;
        double length = 0;
        //获取所有分段
        foreach (var node in root.GetProperty("durl").EnumerateArray( ))
        {
            result.Clips.Add(node.GetProperty("url").ToString( ));
            size += node.GetProperty("size").GetDouble( );
            length += node.GetProperty("length").GetDouble( );
        }

        result.Dfns.AddRange(ReadAcceptedDfns(root));
        result.Duration = (int) length / 1000;

        var quality = root.GetProperty("quality").ToString( );
        Video v = new( )
        {
            Id = quality,
            Dfn = Config.GetQualityName(quality),
            BaseUrl = "",
            Codecs = VideoCodec(root.GetProperty("video_codecid").ToString( )),
            Dur = (int) length / 1000,
            Size = size
        };
        if (!result.VideoTracks.Contains(v))
        {
            result.VideoTracks.Add(v);
        }
    }

    internal static IEnumerable<string> ReadAcceptedDfns(JsonElement root)
    {
        //TV模式可用清晰度
        if (root.TryGetProperty("qn_extras", out var qnExtras))
        {
            return qnExtras.EnumerateArray( ).Select(node => node.GetProperty("qn").ToString( ));
        }

        //非tv模式可用清晰度
        if (root.TryGetProperty("accept_quality", out var acceptQuality))
        {
            return acceptQuality.EnumerateArray( ).Select(node => node.ToString( )).Where(qn => !string.IsNullOrEmpty(qn));
        }

        return [];
    }
}
