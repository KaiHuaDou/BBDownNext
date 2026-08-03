using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core.Entity;

using static BBDown.Core.Entity.Entity;
using static BBDown.Core.Logger;
using static BBDown.Core.Util.HTTPUtil;
using static BBDown.Core.Util.JsonUtil;

namespace BBDown.Core;

public static partial class Parser
{
    // 收拢 playurl 请求参数，避免在解析各分支间逐层透传 9 个形参
    private readonly record struct PlayUrlRequest(
        string AidOri,
        string Aid,
        string Cid,
        string EpId,
        bool TvApi,
        bool IntlApi,
        bool AppApi,
        string Encoding,
        AppConfig Cfg)
    {
        public bool IsCheese => AidOri.StartsWith(IdPrefix.Cheese);

        public bool IsEpisode => AidOri.StartsWith(IdPrefix.EpColon);

        public bool IsBangumi => IsCheese || IsEpisode;
    }

    // CA5351: MD5 由 B 站 wbi 签名协议规定，哈希值必须与服务端保持一致，不能替换为 SHA256
    // 算法见 bilibili-API-collect/docs/misc/sign/wbi.md：把含 wts 的参数按 key 升序排序后，对值做
    // encodeURIComponent 风格编码（并过滤 !'()*），末尾直接拼接 mixinKey 取 MD5 得 w_rid，再追加回原始 query。
    // 当前 playurl 参数均为数字/固定字面量，编码为恒等变换；排序是关键修正点（旧实现按书写序拼接，与服务端不一致）。
    public static string WbiSign(string api, AppConfig cfg)
    {
        if (cfg.Wbi.Length == 0) return api;

        // 先剔除任何已存在的 w_rid：既用于计算 canonical，也避免重签名时把旧 w_rid 残留在输出里
        var withoutWrid = string.Join("&",
            api.Split('&')
                .Select(part => part.Split('=', 2))
                .Where(kv => kv.Length == 2 && kv[0] != "w_rid")
                .Select(kv => $"{kv[0]}={kv[1]}"));

        var canonical = string.Join("&",
            withoutWrid.Split('&')
                .Select(part => part.Split('=', 2))
                .Where(kv => kv.Length == 2)
                .Select(kv => (Key: kv[0], Value: WbiEncodeValue(kv[1])))
                .OrderBy(p => p.Key, StringComparer.Ordinal)
                .Select(p => $"{p.Key}={p.Value}"))
            + cfg.Wbi;

        var w_rid = string.Concat(MD5.HashData(Encoding.UTF8.GetBytes(canonical)).Select(b => b.ToString("x2")));
        return $"{withoutWrid}&w_rid={w_rid}";
    }

    // 与浏览器 encodeURIComponent 一致：保留 A-Za-z0-9-_.~，过滤 wbi.md 要求的 !'()*，其余按 UTF-8 字节大写十六进制转义（空格 -> %20）。
    private static string WbiEncodeValue(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (ch is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '_' or '.' or '~')
            {
                sb.Append(ch);
            }
            else if (ch is '!' or '\'' or '(' or ')' or '*')
            {
                // wbi.md 要求过滤 !'()*
            }
            else
            {
                foreach (var b in Encoding.UTF8.GetBytes(new[] { ch }))
                {
                    sb.Append('%').Append(b.ToString("X2"));
                }
            }
        }

        return sb.ToString();
    }

    private static async Task<string> GetPlayJsonAsync(PlayUrlRequest req, string qn = "0", CancellationToken ct = default)
    {
        LogDebug("aid={0},cid={1},epId={2},tvApi={3},IntlApi={4},qn={5}", req.Aid, req.Cid, req.EpId, req.TvApi, req.IntlApi, qn);

        if (req.IntlApi)
        {
            return await GetIntlPlayJsonAsync(req.Aid, req.Cid, req.EpId, qn, req.Cfg, ct: ct);
        }

        LogDebug("bangumi={0},cheese={1}", req.IsBangumi, req.IsCheese);

        var api = BuildPlayUrlPrefix(req.TvApi, req.IsBangumi, req.IsCheese, req.Cfg.TvHost, req.Cfg.Host)
            + (req.TvApi ? BuildTvApiQuery(req, qn) : BuildWebApiQuery(req, qn));

        var webJson = await GetWebSourceAsync(api, req.Cfg, null, ct);
        if (!IsVipRestricted(webJson))
        {
            return webJson;
        }

        //大会员专享限制时从网页源代码尝试解析
        Log("此视频需要大会员，您大概率需要登录一个有大会员的账号才可以下载，尝试从网页源码解析。");
        return await GetPlayJsonFromWebPageAsync(req, ct);
    }

    // playurl 接口对大会员限制没有专用错误码，只能认 message 文案；不同端点分别用 message / msg
    internal static bool IsVipRestricted(string webJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(webJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return false;

            foreach (var key in VipRestrictionMessageKeys)
            {
                if (doc.RootElement.TryGetProperty(key, out var message)
                    && message.ValueKind == JsonValueKind.String
                    && message.GetString( ) == "大会员专享限制")
                {
                    return true;
                }
            }

            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static readonly string[] VipRestrictionMessageKeys = ["message", "msg"];

    // 大会员专享限制时, 改从网页源码抠 window.__playinfo__。
    // 与正常 API 路径解耦为独立方法, 并按 cheese / 番剧构造正确的播放页地址,
    // 匹配失败时抛明确异常(而非返回空串导致后续 JSON 解析报莫名其妙的错)。
    private static async Task<string> GetPlayJsonFromWebPageAsync(PlayUrlRequest req, CancellationToken ct = default)
    {
        var pageUrl = req.IsCheese
            ? $"{BiliApi.CheesePlayPage}/ep{req.EpId}"
            : $"{BiliApi.BangumiPlayPage}/ep{req.EpId}";
        var webSource = await GetWebSourceAsync(pageUrl, req.Cfg, null, ct);
        var match = PlayerJsonRegex( ).Match(webSource);
        if (!match.Success)
        {
            throw new InvalidOperationException("从网页源码解析播放信息失败");
        }

        return match.Groups[1].Value;
    }

    internal static string BuildPlayUrlPrefix(bool tvApi, bool bangumi, bool cheese, string tvHost, string host)
    {
        var prefix = (tvApi, bangumi) switch
        {
            (true, true) => tvHost + BiliApi.PlayUrlPgcTvPath,
            (true, false) => tvHost + BiliApi.PlayUrlTvPath,
            (false, true) => host + BiliApi.PlayUrlPgcPath,
            (false, false) => host + BiliApi.PlayUrlWebPath
        };
        // 课程（cheese）与番剧共用同一套 playurl 网关，仅域名路径中的 /pgc/ 需替换为 /pugv/。
        // 因此直接复用 PGC 的 v2 路径（含 DASH 支持），再整体换域名——并非文档里写的非 v2 端点，属有意设计。
        if (cheese) prefix = prefix.Replace("/pgc/", "/pugv/");
        return $"https://{prefix}?";
    }

    private static string BuildTvApiQuery(PlayUrlRequest req, string qn)
    {
        StringBuilder query = new( );
        if (req.Cfg.Token.Length != 0)
        {
            query.Append($"access_key={req.Cfg.Token}&");
        }

        query.Append($"appkey=4409e2ce8ffd12b8&build=106500&cid={req.Cid}&device=android");
        if (req.IsBangumi)
        {
            query.Append($"&ep_id={req.EpId}&expire=0");
        }

        query.Append("&fnval=4048&fnver=0&fourk=1&mid=0&mobi_app=android_tv_yst");
        query.Append($"&object_id={req.Aid}&platform=android&playurl_type=1&qn={qn}&ts={GetTimeStamp(true)}");
        return $"{query}&sign={GetSign(query.ToString( ), false)}";
    }

    private static string BuildWebApiQuery(PlayUrlRequest req, string qn)
    {
        StringBuilder query = new( );
        query.Append($"support_multi_audio=true&from_client=BROWSER&avid={req.Aid}&cid={req.Cid}&fnval=4048&fnver=0&fourk=1");
        if (req.Cfg.Area.Length != 0)
        {
            query.Append($"&access_key={req.Cfg.Token}&area={req.Cfg.Area}");
        }

        query.Append($"&otype=json&qn={qn}");
        if (req.IsBangumi)
        {
            // 课程（cheese）复用番剧 playurl 参数（module=bangumi&ep_id&session）；pugv 端点会忽略 module，ep_id 为必需。
            query.Append($"&module=bangumi&ep_id={req.EpId}&session=");
        }

        if (req.Cfg.Cookie.Length == 0)
        {
            query.Append("&try_look=1");
        }

        query.Append($"&wts={GetTimeStamp(true)}");
        return req.IsBangumi ? query.ToString( ) : WbiSign(query.ToString( ), req.Cfg);
    }

    private static async Task<string> GetIntlPlayJsonAsync(string aid, string cid, string epId, string qn, AppConfig cfg, string code = "0", CancellationToken ct = default)
    {
        var isBiliPlus = cfg.Host != BiliApi.MainHost;
        var api = $"https://{(isBiliPlus ? cfg.Host : BiliApi.IntlWebHost)}{BiliApi.IntlPlayUrlPath}?";

        StringBuilder query = new( );
        if (cfg.Token.Length != 0)
        {
            query.Append($"access_key={cfg.Token}&");
        }

        query.Append($"aid={aid}");
        if (isBiliPlus)
        {
            query.Append($"&appkey=7d089525d3611b1c&area={(cfg.Area.Length == 0 ? "th" : cfg.Area)}");
        }

        query.Append($"&cid={cid}&ep_id={epId}&platform=android&prefer_code_type={code}&qn={qn}");
        if (isBiliPlus)
        {
            query.Append($"&ts={GetTimeStamp(true)}");
        }

        query.Append("&s_locale=zh_SG");
        var param = query.ToString( );
        return await GetWebSourceAsync(api + (isBiliPlus ? $"{param}&sign={GetSign(param, true)}" : param), cfg, null, ct);
    }

    public static async Task<ParsedResult> ExtractTracksAsync(string aidOri, string aid, string cid, string epId, bool tvApi, bool intlApi, bool appApi, string encoding, AppConfig cfg, string qn = "0", CancellationToken ct = default)
    {
        PlayUrlRequest req = new(aidOri, aid, cid, epId, tvApi, intlApi, appApi, encoding, cfg);

        // intl 与 app 同时指定时沿用 intl, 与 GetPlayJsonAsync 的优先级保持一致
        if (req.AppApi && !req.IntlApi)
        {
            return await ExtractAppTracksAsync(req, ct);
        }

        ParsedResult parsedResult = new( )
        {
            //调用解析
            RawResponse = await GetPlayJsonAsync(req, qn, ct)
        };

        LogDebug(parsedResult.RawResponse);

        //intl接口: prefer_code_type 0/1 各请求一次, 合并两次拿到的轨道
        for (var intlAttempt = 0; intlAttempt < 2; intlAttempt++)
        {
            using var intlDoc = JsonDocument.Parse(parsedResult.RawResponse);
            if (!TryGetIntlVideoInfo(intlDoc.RootElement, out var videoInfo)) break;

            CollectIntlTracks(parsedResult, videoInfo);
            if (intlAttempt == 1) return parsedResult;

            parsedResult.RawResponse = await GetIntlPlayJsonAsync(aid, cid, epId, qn, cfg, "1", ct);
        }

        using var doc = JsonDocument.Parse(parsedResult.RawResponse);
        var data = doc.RootElement;
        var nodeName = ResolveDataNodeName(data);
        var root = GetRootNode(data, nodeName);

        if (HasObject(root, "dash"))
        {
            root = await ExtractDashTracksAsync(parsedResult, req, root, nodeName, ct);
        }
        else if (TryGetArray(root, "durl", out _))
        {
            root = await ExtractFlvTracksAsync(parsedResult, req, nodeName, ct);
        }

        if (req.IsEpisode)
        {
            AppendBangumiViewPoints(parsedResult, root);
        }

        return parsedResult;
    }

    // data 节点一次性判断完；v2 接口把有效载荷藏在 result.video_info 下
    internal static string? ResolveDataNodeName(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object) return null;

        if (data.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.Object)
        {
            return HasObject(result, "video_info") ? "video_info" : "result";
        }

        return HasObject(data, "data") ? "data" : null;
    }

    // intl 接口的有效载荷落在 data.video_info 下，只有它带 stream_list 才走 intl 分支
    private static bool TryGetIntlVideoInfo(JsonElement root, out JsonElement videoInfo)
    {
        videoInfo = default;
        if (!HasObject(root, "data")) return false;

        var data = root.GetProperty("data");
        if (!HasObject(data, "video_info")) return false;

        videoInfo = data.GetProperty("video_info");
        return TryGetArray(videoInfo, "stream_list", out _);
    }

    internal static JsonElement GetRootNode(JsonElement data, string? nodeName)
    {
        return nodeName switch
        {
            null => data,
            "video_info" => data.GetProperty("result").GetProperty("video_info"),
            _ => data.GetProperty(nodeName)
        };
    }

    private static void CollectIntlTracks(ParsedResult parsedResult, JsonElement videoInfo)
    {
        // 缺字段时不应抛 KeyNotFoundException（P1-6）
        var pDur = videoInfo.TryGetProperty("timelength", out var tl) ? tl.GetInt32( ) / 1000 : 0;

        foreach (var stream in videoInfo.GetProperty("stream_list").EnumerateArray( ))
        {
            if (!stream.TryGetProperty("dash_video", out var dashVideo)) continue;
            if (dashVideo.GetProperty("base_url").ToString( ).Length == 0) continue;

            var videoId = stream.GetProperty("stream_info").GetProperty("quality").ToString( );
            Video v = new( )
            {
                dur = pDur,
                id = videoId,
                dfn = Config.GetQualityName(videoId),
                bandwidth = Convert.ToInt64(dashVideo.GetProperty("bandwidth").ToString( )) / 1000,
                baseUrl = PickBaseUrl(BuildUrlList(dashVideo)),
                codecs = GetVideoCodec(dashVideo.GetProperty("codecid").ToString( )),
                size = dashVideo.TryGetProperty("size", out var sizeNode) ? Convert.ToDouble(sizeNode.ToString( )) : 0
            };
            if (!parsedResult.VideoTracks.Contains(v))
            {
                parsedResult.VideoTracks.Add(v);
            }
        }

        // 缺字段时不应抛 KeyNotFoundException（P1-6）
        if (videoInfo.TryGetProperty("dash_audio", out var dashAudioArr) && dashAudioArr.ValueKind == JsonValueKind.Array)
        {
            foreach (var node in dashAudioArr.EnumerateArray( ))
            {
                var a = BuildAudio(node, pDur, "M4A");
                if (!parsedResult.AudioTracks.Contains(a))
                {
                    parsedResult.AudioTracks.Add(a);
                }
            }
        }
    }

    private static async Task<JsonElement> ExtractDashTracksAsync(ParsedResult parsedResult, PlayUrlRequest req, JsonElement root, string? nodeName, CancellationToken ct = default)
    {
        var pDur = ReadDashDuration(root);
        CollectDashVideoTracks(parsedResult, root, pDur, req.TvApi);

        //此处处理免二压视频，需要单独再请求一次；视频轨取两次的并集，音轨只取重新请求后的结果
        var firstRoot = root;
        parsedResult.RawResponse = await GetPlayJsonAsync(req, Config.MaxQn, ct);
        using var maxQnDoc = JsonDocument.Parse(parsedResult.RawResponse);
        // Clone 后节点脱离 maxQnDoc 独立存活, 可以安全返回给调用方
        root = GetRootNode(maxQnDoc.RootElement, nodeName).Clone( );
        CollectDashVideoTracks(parsedResult, root, pDur, req.TvApi);

        // 二次请求偶尔返回降级响应(限流/无 dash 节点)，此时沿用首次结果的音轨而不是丢弃
        var audioRoot = TryEnumerateArray(root, "dash", "audio") != null ? root : firstRoot;
        CollectDashAudioTracks(parsedResult, audioRoot, pDur, req.TvApi);

        return root;
    }

    internal static int ReadDashDuration(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) return 0;

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

    private static void CollectDashVideoTracks(ParsedResult parsedResult, JsonElement root, int pDur, bool tvApi)
    {
        var video = TryEnumerateArray(root, "dash", "video");
        if (video == null) return;

        foreach (var node in video)
        {
            var videoId = node.GetProperty("id").ToString( );
            Video v = new( )
            {
                dur = pDur,
                id = videoId,
                dfn = Config.GetQualityName(videoId),
                bandwidth = Convert.ToInt64(node.GetProperty("bandwidth").ToString( )) / 1000,
                baseUrl = PickBaseUrl(BuildUrlList(node)),
                codecs = GetVideoCodec(node.GetProperty("codecid").ToString( )),
                size = node.TryGetProperty("size", out var sizeNode) ? Convert.ToDouble(sizeNode.ToString( )) : 0
            };
            if (!tvApi)
            {
                v.res = node.GetProperty("width").ToString( ) + "x" + node.GetProperty("height").ToString( );
                v.fps = node.GetProperty("frame_rate").ToString( );
            }

            if (!parsedResult.VideoTracks.Contains(v))
            {
                parsedResult.VideoTracks.Add(v);
            }
        }
    }

    private static void CollectDashAudioTracks(ParsedResult parsedResult, JsonElement root, int pDur, bool tvApi)
    {
        var audio = TryEnumerateArray(root, "dash", "audio");
        if (audio == null) return;

        AppendDolbyAndHiResAudio(audio, root, tvApi);
        foreach (var node in audio)
        {
            parsedResult.AudioTracks.Add(BuildAudio(node, pDur, NormalizeAudioCodec(node.GetProperty("codecs").ToString( ))));
        }
    }

    private static void AppendDolbyAndHiResAudio(List<JsonElement> audio, JsonElement root, bool tvApi)
    {
        if (tvApi || root.ValueKind != JsonValueKind.Object) return;
        if (!root.TryGetProperty("dash", out var dash) || dash.ValueKind != JsonValueKind.Object) return;

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

    private static async Task<JsonElement> ExtractFlvTracksAsync(ParsedResult parsedResult, PlayUrlRequest req, string? nodeName, CancellationToken ct = default)
    {
        //默认以最高清晰度解析
        parsedResult.RawResponse = await GetPlayJsonAsync(req, Config.MaxQn, ct);
        using var doc = JsonDocument.Parse(parsedResult.RawResponse);
        // Clone 后节点脱离 doc 独立存活, 可以安全返回给调用方
        var root = GetRootNode(doc.RootElement, nodeName).Clone( );

        double size = 0;
        double length = 0;
        //获取所有分段
        foreach (var node in root.GetProperty("durl").EnumerateArray( ))
        {
            parsedResult.Clips.Add(node.GetProperty("url").ToString( ));
            size += node.GetProperty("size").GetDouble( );
            length += node.GetProperty("length").GetDouble( );
        }

        parsedResult.Dfns.AddRange(ReadAcceptedDfns(root));

        var quality = root.GetProperty("quality").ToString( );
        Video v = new( )
        {
            id = quality,
            dfn = Config.GetQualityName(quality),
            baseUrl = "",
            codecs = GetVideoCodec(root.GetProperty("video_codecid").ToString( )),
            dur = (int) length / 1000,
            size = size
        };
        if (!parsedResult.VideoTracks.Contains(v))
        {
            parsedResult.VideoTracks.Add(v);
        }

        return root;
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

    private static void AppendBangumiViewPoints(ParsedResult parsedResult, JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("clip_info_list", out var clipList)) return;

        AppendViewPoints(parsedResult, clipList.EnumerateArray( ).Select(clip => new ViewPoint( )
        {
            title = clip.GetProperty("toastText").ToString( ).Replace("即将跳过", ""),
            start = clip.GetProperty("start").GetInt32( ),
            end = clip.GetProperty("end").GetInt32( )
        }));
    }

    private static void AppendViewPoints(ParsedResult parsedResult, IEnumerable<ViewPoint> points)
    {
        parsedResult.ExtraPoints.AddRange(points);
        parsedResult.ExtraPoints.Sort((p1, p2) => p1.start.CompareTo(p2.start));
        parsedResult.ExtraPoints = FillGapsWithMainContent(parsedResult.ExtraPoints);
    }

    // 番剧片头片尾转分段信息, 预计效果: 正片? -> 片头 -> 正片 -> 片尾
    internal static List<ViewPoint> FillGapsWithMainContent(List<ViewPoint> points)
    {
        List<ViewPoint> result = [];
        var lastEnd = 0;
        foreach (var point in points)
        {
            if (lastEnd < point.start)
            {
                result.Add(new ViewPoint( ) { title = "正片", start = lastEnd, end = point.start });
            }

            result.Add(point);
            lastEnd = point.end;
        }

        return result;
    }

    private static Audio BuildAudio(JsonElement node, int pDur, string? codecs = null)
    {
        var audioId = node.GetProperty("id").ToString( );
        return new Audio( )
        {
            id = audioId,
            dfn = audioId,
            dur = pDur,
            bandwidth = Convert.ToInt64(node.GetProperty("bandwidth").ToString( )) / 1000,
            baseUrl = PickBaseUrl(BuildUrlList(node)),
            codecs = codecs ?? node.GetProperty("codecs").ToString( )
        };
    }

    private static List<JsonElement>? TryEnumerateArray(JsonElement parent, params string[] path)
    {
        var node = parent;
        foreach (var name in path)
        {
            if (node.ValueKind != JsonValueKind.Object || !node.TryGetProperty(name, out var child))
            {
                return null;
            }

            node = child;
        }

        return node.ValueKind == JsonValueKind.Array ? [.. node.EnumerateArray( )] : null;
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

    /// <summary>
    /// 编码转换
    /// </summary>
    /// <param name="code"></param>
    /// <returns></returns>
    internal static string GetVideoCodec(string code)
    {
        return code switch
        {
            "13" => "AV1",
            "12" => "HEVC",
            "7" => "AVC",
            _ => "UNKNOWN"
        };
    }

    private static string GetTimeStamp(bool bflag)
    {
        var ts = DateTimeOffset.Now;
        return bflag ? ts.ToUnixTimeSeconds( ).ToString( ) : ts.ToUnixTimeMilliseconds( ).ToString( );
    }

    // CA5351: MD5 由 B 站 appkey 签名协议规定，哈希值必须与服务端保持一致，不能替换为 SHA256
    private static string GetSign(string parms, bool isBiliPlus)
    {
        var toEncode = parms + (isBiliPlus ? "acd495b248ec528c2eed1e862d393126" : "59b43e04ad6965f34319062b478f83dd");
        return string.Concat(MD5.HashData(Encoding.UTF8.GetBytes(toEncode)).Select(i => i.ToString("x2")).ToArray( ));
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
        return urlList.FirstOrDefault(i => !BaseUrlRegex( ).IsMatch(i), urlList.First( ));
    }

    [GeneratedRegex("window.__playinfo__=([\\s\\S]*?)<\\/script>")]
    private static partial Regex PlayerJsonRegex( );
    // 仅当 authority 部分带显式端口（如 http://host:8080）才识别为 PCDN，避免误命中带数字查询参数的普通 URL（P2）
    [GeneratedRegex("^https?://[^/]*:\\d+")]
    private static partial Regex BaseUrlRegex( );
}
