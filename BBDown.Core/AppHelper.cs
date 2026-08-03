using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core.Protobuf;

using Google.Protobuf;

using static BBDown.Core.Logger;
using static BBDown.Core.Util.HTTPUtil;

namespace BBDown.Core;

internal static class AppHelper
{
    private const string API = BiliApi.GrpcPlayView;
    private const string API2 = BiliApi.GrpcPgcPlayView;
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
    private const string appKey = "android64";
    private const string sessionId = "dedf8669";
    private const string platform = "android";
    private const string env = "prod";
    private const int appId = 1;
    private const string region = "CN";
    private const string language = "zh";

    private static PlayViewReq.Types.CodeType GetVideoCodeType(string code)
    {
        return code switch
        {
            "AVC" => PlayViewReq.Types.CodeType.Code264,
            "HEVC" => PlayViewReq.Types.CodeType.Code265,
            "AV1" => PlayViewReq.Types.CodeType.Codeav1,
            _ => PlayViewReq.Types.CodeType.Code265
        };
    }

    public static async Task<PlayViewReply> DoReqAsync(string aid, string cid, string epId, bool bangumi, string encoding, AppConfig cfg, string appkey = "", CancellationToken ct = default)
    {
        var headers = GetHeader(appkey, cfg, bangumi ? "app.bilibili.com" : "grpc.biliapi.net");
        LogDebug("App-Req-Headers: {0}", JsonSerializer.Serialize(headers, JsonContext.Default.DictionaryStringString));
        byte[] data;
        // 只有pgc接口才有配音和片头尾信息
        if (bangumi)
        {
            if (!(string.IsNullOrEmpty(encoding) || encoding == "HEVC"))
            {
                LogWarn("APP 的番剧不支持 HEVC 以外的编码。");
            }

            var body = GetPayload(Convert.ToInt64(epId), Convert.ToInt64(cid), PlayViewReq.Types.CodeType.Code265);
            data = await GetPostResponseAsync(API2, body, headers, ct);
        }
        else
        {
            var body = GetPayload(Convert.ToInt64(aid), Convert.ToInt64(cid), GetVideoCodeType(encoding));
            data = await GetPostResponseAsync(API, body, headers, ct);
        }

        return ReplyParser.ParseFrom(ReadMessage(data));
    }

    private static readonly MessageParser<PlayViewReply> ReplyParser = new(( ) => new PlayViewReply( ));

    private static byte[] GetPayload(long aid, long cid, PlayViewReq.Types.CodeType codec)
    {
        var obj = new PlayViewReq
        {
            EpId = aid,
            Cid = cid,
            // 固定请求最高档，实际可用画质由响应决定；传入 qn 反而会被服务端限流到该档
            Qn = 127,
            Fnval = 4048,
            Fourk = true,
            Spmid = "main.ugc-video-detail.0.0",
            FromSpmid = "main.my-history.0.0",
            PreferCodecType = codec,
            Download = 0, //0:播放 1:flv下载 2:dash下载
            ForceHost = 2 //0:允许使用ip 1:使用http 2:使用https
        };
        LogDebug("PayLoadPlain: {0}", JsonSerializer.Serialize(obj, JsonContext.Default.PlayViewReq));
        return PackMessage(obj.ToByteArray( ));
    }

    #region 生成Headers相关方法

    internal static Dictionary<string, string> GetHeader(string appkey, AppConfig cfg, string host)
    {
        return new Dictionary<string, string>( )
        {
            ["Host"] = host,
            ["user-agent"] = $"Dalvik/{dalvikVer} (Linux; U; Android {osVer}; {brand} {model}) {appVer} os/android model/{brand} mobi_app/android build/{build} channel/{channel} innerVer/{build} osVer/{osVer} network/2 grpc-java-cronet/{cronet}",
            ["te"] = "trailers",
            ["x-bili-fawkes-req-bin"] = GenerateFawkesReqBin( ),
            ["x-bili-metadata-bin"] = GenerateMetadataBin(appkey),
            ["authorization"] = $"identify_v1 {cfg.Token}",
            ["x-bili-device-bin"] = GenerateDeviceBin( ),
            ["x-bili-network-bin"] = GenerateNetworkBin( ),
            ["x-bili-restriction-bin"] = "",
            ["x-bili-locale-bin"] = GenerateLocaleBin( ),
            ["x-bili-exps-bin"] = "",
            ["grpc-encoding"] = "gzip",
            ["grpc-accept-encoding"] = "identity,gzip",
            ["grpc-timeout"] = "17996161u",
        };
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

    private static string GenerateMetadataBin(string appkey)
    {
        var obj = new Metadata
        {
            AccessKey = appkey,
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
            Appkey = appKey,
            Env = env,
            SessionId = sessionId
        };
        return Convert.ToBase64String(obj.ToByteArray( ));
    }

    #endregion

    /// <summary>
    /// 读取gRPC响应流 通过前5字节信息 解析/解压后面的报文体
    /// </summary>
    public static byte[] ReadMessage(byte[] data)
    {
        if (data.Length < 5)
        {
            throw new InvalidDataException($"gRPC 响应帧头不足 5 字节(实际 {data.Length} 字节)");
        }

        var compressed = data[0] == 1;
        var size = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(1, 4));
        if (size < 0 || 5L + size > data.Length)
        {
            throw new InvalidDataException($"gRPC 帧头声明报文体 {size} 字节, 实际只有 {data.Length - 5} 字节");
        }

        var body = data[5..(5 + size)];
        return compressed ? GzipDecompress(body) : body;
    }

    /// <summary>
    /// 给请求载荷添加头部信息
    /// </summary>
    public static byte[] PackMessage(byte[] input)
    {
        using var stream = new MemoryStream( );
        using (var writer = new BinaryWriter(stream))
        {
            var comp = GzipCompress(input);
            Span<byte> reverse = stackalloc byte[4];
            writer.Write((byte) 1);
            BinaryPrimitives.WriteInt32BigEndian(reverse, comp.Length);
            writer.Write(reverse);
            writer.Write(comp);
        }

        return stream.ToArray( );
    }

    private static byte[] GzipCompress(byte[] data)
    {
        using var output = new MemoryStream( );
        using (var comp = new GZipStream(output, CompressionMode.Compress))
        {
            comp.Write(data, 0, data.Length);
        }

        return output.ToArray( );
    }

    private static byte[] GzipDecompress(byte[] data)
    {
        using var output = new MemoryStream( );
        using (var input = new MemoryStream(data))
        {
            using var decomp = new GZipStream(input, CompressionMode.Decompress);
            decomp.CopyTo(output);
        }

        return output.ToArray( );
    }
}

[JsonSerializable(typeof(PlayViewReq))]
[JsonSerializable(typeof(PlayViewReply))]
[JsonSerializable(typeof(Dictionary<string, string>))]
internal partial class JsonContext : JsonSerializerContext;
