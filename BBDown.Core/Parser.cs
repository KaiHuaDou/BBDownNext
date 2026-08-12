using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core.Entity;
using BBDown.Core.PlayUrl;
using BBDown.Core.Util;

using static BBDown.Core.Logger;
using static BBDown.Core.Util.JsonUtil;

namespace BBDown.Core;

public static class Parser
{
    public static async Task<ParsedResult> ExtractTracksAsync(ResourceId aidOri, string aid, string cid, string epId, ApiType api, string encoding, AppConfig cfg, string qn = "0", CancellationToken ct = default)
    {
        PlayUrlRequest req = new(aidOri, aid, cid, epId, api, encoding, cfg);

        if (req.Api == ApiType.App)
        {
            return await AppTrackReader.FetchAsync(req, ct);
        }

        ParsedResult result = new( )
        {
            //调用解析
            RawResponse = await PlayUrlClient.FetchAsync(req, qn, ct)
        };

        LogDebug(result.RawResponse);

        // INTL 双次请求（prefer_code_type 0/1 各一次）合并轨道；任一次缺 stream_list 即放弃 intl 通道，
        // 保留已收集轨道并交回通用 dash/durl 通道解析（等价点 B：勿当作 bug 顺手"修"掉）
        if (await TryCollectIntlAsync(result, req, qn, ct))
        {
            return result;
        }

        using var firstDoc = JsonDocument.Parse(result.RawResponse);
        // playurl 非 0 code（-400/-404/-352 等）意味着拉流失败：直接抛可读错误，
        // 否则会静默落到根节点解析出空轨道，下游只报一句晦涩的「解析此分 P 失败」
        var (code, message) = ReadApiError(firstDoc.RootElement);
        if (code != 0)
        {
            throw new InvalidOperationException(code == -352
                ? $"播放信息被风控拦截（code={code}）：{message}，请稍后重试或补充已登录的 Cookie"
                : $"获取播放信息失败（code={code}）：{message}");
        }

        var nodeName = PlayUrlResponse.ResolveDataNodeName(firstDoc.RootElement);
        var firstRoot = PlayUrlResponse.GetRootNode(firstDoc.RootElement, nodeName);

        if (HasObject(firstRoot, "dash"))
        {
            // 免二压视频需要按最高档再请求一次；视频轨取两次并集，音轨优先取第二次
            result.RawResponse = await PlayUrlClient.FetchAsync(req, Config.MaxQn, ct);
            using var maxQnDoc = JsonDocument.Parse(result.RawResponse);
            var maxQnRoot = PlayUrlResponse.GetRootNode(maxQnDoc.RootElement, nodeName);
            DashTrackReader.Collect(result, firstRoot, maxQnRoot, req.Api == ApiType.Tv);
            if (DashTrackReader.DeclaredButMissing(maxQnRoot, result, Config.AiRepairQn))
            {
                LogWarn("该视频存在「智能修复」画质，当前账号非大会员无法获取");
            }

            if (req.IsEpisode)
            {
                AppendBangumiViewPoints(result, maxQnRoot);
            }
        }
        else if (TryGetArray(firstRoot, "durl", out _))
        {
            // FLV 强制最高清晰度
            result.RawResponse = await PlayUrlClient.FetchAsync(req, Config.MaxQn, ct);
            using var maxQnDoc = JsonDocument.Parse(result.RawResponse);
            var maxQnRoot = PlayUrlResponse.GetRootNode(maxQnDoc.RootElement, nodeName);
            FlvTrackReader.Collect(result, maxQnRoot);
            if (req.IsEpisode)
            {
                AppendBangumiViewPoints(result, maxQnRoot);
            }
        }
        else if (req.IsEpisode)
        {
            AppendBangumiViewPoints(result, firstRoot);
        }

        return result;
    }

    // INTL 两次请求合并：首响应用 prefer_code_type=0，成功收集后再以 prefer_code_type=1 请求并合并。
    // 任一次缺 stream_list 返回 false，把响应体交回通用 dash/durl 通道（等价点 B）。
    private static async Task<bool> TryCollectIntlAsync(ParsedResult result, PlayUrlRequest req, string qn, CancellationToken ct)
    {
        bool TryCollect( )
        {
            using var doc = JsonDocument.Parse(result.RawResponse);
            if (!IntlTrackReader.TryGetVideoInfo(doc.RootElement, out var videoInfo))
            {
                return false;
            }

            IntlTrackReader.Collect(result, videoInfo);
            return true;
        }

        if (!TryCollect( ))
        {
            return false;
        }

        result.RawResponse = await PlayUrlClient.FetchIntlAsync(req, qn, "1", ct);
        return TryCollect( );
    }

    private static void AppendBangumiViewPoints(ParsedResult parsedResult, JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("clip_info_list", out var clipList))
        {
            return;
        }

        ViewPointUtil.Append(parsedResult, clipList.EnumerateArray( ).Select(clip => new ViewPoint( )
        {
            Title = clip.GetProperty("toastText").ToString( ).Replace("即将跳过", ""),
            Start = clip.GetProperty("start").GetInt32( ),
            End = clip.GetProperty("end").GetInt32( )
        }));
    }
}
