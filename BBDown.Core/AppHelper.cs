using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core.Protobuf;
using BBDown.Core.Util;

using Google.Protobuf;

using static BBDown.Core.Logger;
using static BBDown.Core.Util.HTTPUtil;

namespace BBDown.Core;

internal static class AppHelper
{
    private const string dalvikVer = "2.1.0";
    private const string osVer = "11";
    private const string brand = "M2012K11AC";
    private const string model = "Build/RKQ1.200826.002";
    private const string appVer = "7.32.0";
    private const int build = 7320200; // 新版才能抓到配音
    private const string channel = "xiaomi_cn_tv.danmaku.bili_zm20200902";
    private const Network.Types.TYPE networkType = Network.Types.TYPE.Wifi;
    private const string networkOid = "46007";
    private const string cronet = "1.36.1";
    private const string mobiApp = "android";
    private const string fawkesAppKey = "android64";
    private const string sessionId = "dedf8669";
    private const string platform = "android";
    private const string env = "prod";
    private const int appId = 1;
    private const string region = "CN";
    private const string language = "zh";

    private static readonly MessageParser<PlayViewReply> ReplyParser = new(( ) => new PlayViewReply( ));

    private static PlayViewReq.Types.CodeType GetVideoCodeType(string code)
    {
        return code switch
        {
            "AVC" => PlayViewReq.Types.CodeType.Code264,
            "AV1" => PlayViewReq.Types.CodeType.Codeav1,
            // HEVC 与未知编码统一兜底
            _ => PlayViewReq.Types.CodeType.Code265
        };
    }

    public static async Task<PlayViewReply> DoReqAsync(string aid, string cid, string epId, bool bangumi, string encoding, AppConfig cfg, CancellationToken ct = default)
    {
        var api = bangumi ? BiliApi.GrpcPgcPlayView : BiliApi.GrpcPlayView;
        var headers = GetHeader(cfg, new Uri(api).Host);
        LogDebug("App-Req-Headers: {0}", JsonSerializer.Serialize(headers, JsonContext.Default.DictionaryStringString));
        byte[] data;
        // 只有pgc接口才有配音和片头尾信息
        if (bangumi)
        {
            if (!string.IsNullOrEmpty(encoding) && encoding != "HEVC")
            {
                LogWarn("APP 的番剧不支持 HEVC 以外的编码。");
            }

            var body = GetPayload(Convert.ToInt64(epId), Convert.ToInt64(cid), PlayViewReq.Types.CodeType.Code265);
            data = await GetPostResponseAsync(api, body, headers, ct);
        }
        else
        {
            var body = GetPayload(Convert.ToInt64(aid), Convert.ToInt64(cid), GetVideoCodeType(encoding));
            data = await GetPostResponseAsync(api, body, headers, ct);
        }

        return ReplyParser.ParseFrom(GrpcUtil.ReadMessage(data));
    }

    private static byte[] GetPayload(long aid, long cid, PlayViewReq.Types.CodeType codec)
    {
        var obj = new PlayViewReq
        {
            EpId = aid,
            Cid = cid,
            // 固定请求最高档，实际可用画质由响应决定；传入 qn 反而会被服务端限流到该档
            Qn = 127,
            // 4048（dash/4K/HDR/杜比/8K/AV1）+ 16384（HDR Vivid 位，仅 APP 端有效）
            Fnval = 4048 | 16384,
            Fourk = true,
            Spmid = "main.ugc-video-detail.0.0",
            FromSpmid = "main.my-history.0.0",
            PreferCodecType = codec,
            Download = 0, //0:播放 1:flv下载 2:dash下载
            ForceHost = 2 //0:允许使用ip 1:使用http 2:使用https
        };
        LogDebug("PayLoadPlain: {0}", JsonSerializer.Serialize(obj, JsonContext.Default.PlayViewReq));
        return GrpcUtil.PackMessage(obj.ToByteArray( ));
    }

    internal static Dictionary<string, string> GetHeader(AppConfig cfg, string host)
    {
        var headers = new Dictionary<string, string>( )
        {
            ["Host"] = host,
            ["user-agent"] = $"Dalvik/{dalvikVer} (Linux; U; Android {osVer}; {brand} {model}) {appVer} os/android model/{brand} mobi_app/android build/{build} channel/{channel} innerVer/{build} osVer/{osVer} network/2 grpc-java-cronet/{cronet}",
            ["te"] = "trailers",
            ["x-bili-fawkes-req-bin"] = GenerateFawkesReqBin( ),
            ["x-bili-metadata-bin"] = GenerateMetadataBin(cfg.Token),
            ["x-bili-device-bin"] = GenerateDeviceBin( ),
            ["x-bili-network-bin"] = GenerateNetworkBin( ),
            ["x-bili-restriction-bin"] = "",
            ["x-bili-locale-bin"] = GenerateLocaleBin( ),
            ["x-bili-exps-bin"] = "",
            ["grpc-encoding"] = "gzip",
            ["grpc-accept-encoding"] = "identity,gzip",
            ["grpc-timeout"] = "17996161u",
        };
        // 未登录不发送鉴权头
        if (cfg.Token.Length != 0)
        {
            headers["authorization"] = $"identify_v1 {cfg.Token}";
        }

        return headers;
    }

    private static string GenerateLocaleBin( )
    {
        var obj = new Locale
        {
            CLocale = new Locale.Types.LocaleIds
            {
                Language = language,
                Region = region
            }
        };
        return Convert.ToBase64String(obj.ToByteArray( ));
    }

    private static string GenerateNetworkBin( )
    {
        var obj = new Network
        {
            Type = networkType,
            Oid = networkOid
        };
        return Convert.ToBase64String(obj.ToByteArray( ));
    }

    private static string GenerateDeviceBin( )
    {
        var obj = new Device
        {
            AppId = appId,
            Build = build,
            Buvid = Buvid.Value,
            MobiApp = mobiApp,
            Platform = platform,
            Channel = channel,
            Brand = brand,
            Model = model,
            Osver = osVer
        };
        return Convert.ToBase64String(obj.ToByteArray( ));
    }

    private static string GenerateMetadataBin(string accessKey)
    {
        var obj = new Metadata
        {
            AccessKey = accessKey,
            MobiApp = mobiApp,
            Build = build,
            Channel = channel,
            Buvid = Buvid.Value,
            Platform = platform
        };
        return Convert.ToBase64String(obj.ToByteArray( ));
    }

    private static string GenerateFawkesReqBin( )
    {
        var obj = new FawkesReq
        {
            Appkey = fawkesAppKey,
            Env = env,
            SessionId = sessionId
        };
        return Convert.ToBase64String(obj.ToByteArray( ));
    }
}

[JsonSerializable(typeof(PlayViewReq))]
[JsonSerializable(typeof(PlayViewReply))]
[JsonSerializable(typeof(Dictionary<string, string>))]
internal partial class JsonContext : JsonSerializerContext;
