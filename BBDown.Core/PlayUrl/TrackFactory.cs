using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using BBDown.Core.Entity;


namespace BBDown.Core.PlayUrl;

/// <summary>
/// playurl 响应节点到轨道实体的装配：备用地址收集、PCDN 规避、编码名归一。
/// DASH / FLV / INTL 三条 JSON 路径共用，APP 的 protobuf 路径复用其中的地址与编码工具。
/// </summary>
internal static partial class TrackFactory
{
    /// <summary>
    /// id 为空时取节点自身的 <c>id</c> 字段；intl 接口的清晰度落在兄弟节点 stream_info.quality 上，需显式传入。
    /// </summary>
    internal static Video BuildVideo(JsonElement node, int dur, string? id = null)
    {
        id ??= node.GetProperty("id").ToString( );
        return new Video( )
        {
            Dur = dur,
            Id = id,
            Dfn = Config.GetQualityName(id),
            Bandwidth = Convert.ToInt64(node.GetProperty("bandwidth").ToString( )) / 1000,
            BaseUrl = PickBaseUrl(BuildUrlList(node)),
            Codecs = VideoCodec(node.GetProperty("codecid").ToString( )),
            Size = node.TryGetProperty("size", out var size) ? Convert.ToDouble(size.ToString( )) : 0,
            IsEncrypted = ReadEncrypted(node)
        };
    }

    internal static Audio BuildAudio(JsonElement node, int dur, string? codecs = null)
    {
        var id = node.GetProperty("id").ToString( );
        return new Audio( )
        {
            Id = id,
            Dfn = id,
            Dur = dur,
            Bandwidth = Convert.ToInt64(node.GetProperty("bandwidth").ToString( )) / 1000,
            BaseUrl = PickBaseUrl(BuildUrlList(node)),
            Codecs = codecs ?? node.GetProperty("codecs").ToString( ),
            IsEncrypted = ReadEncrypted(node)
        };
    }

    // 加密标记逐流下发：widevine_pssh / bilidrm_uri 任一存在即视为受保护（协议字段）
    private static bool ReadEncrypted(JsonElement node)
    {
        return ReadString(node, "widevine_pssh") != null || ReadString(node, "bilidrm_uri") != null;
    }

    private static string? ReadString(JsonElement node, string name)
    {
        return node.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null ? value.ToString( ) : null;
    }

    internal static List<string> BuildUrlList(JsonElement node)
    {
        List<string> urlList = [node.GetProperty("base_url").ToString( )];
        if (node.TryGetProperty("backup_url", out var element) && element.ValueKind != JsonValueKind.Null)
        {
            urlList.AddRange(element.EnumerateArray( ).Select(i => i.ToString( )));
        }

        return urlList;
    }

    internal static string PickBaseUrl(List<string> urlList)
    {
        return urlList.FirstOrDefault(i => !PcdnRegex( ).IsMatch(i), urlList[0]);
    }

    internal static string VideoCodec(string code)
    {
        return code switch
        {
            "13" => "AV1",
            "12" => "HEVC",
            "7" => "AVC",
            _ => "UNKNOWN"
        };
    }

    internal static string NormalizeAudioCodec(string codecs)
    {
        return codecs switch
        {
            "mp4a.40.2" => "M4A",
            "mp4a.40.5" => "M4A",
            "ec-3" => "E-AC-3",
            "fLaC" => "FLAC",
            _ => codecs
        };
    }

    // 仅当 authority 部分带显式端口（如 http://host:8080）才识别为 PCDN，避免误命中带数字查询参数的普通 URL（P2）
    [GeneratedRegex("^https?://[^/]*:\\d+")]
    private static partial Regex PcdnRegex( );
}
