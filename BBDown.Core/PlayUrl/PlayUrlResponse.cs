using System;
using System.Text.Json;

using static BBDown.Core.Util.JsonUtil;

namespace BBDown.Core.PlayUrl;

/// <summary>
/// playurl 响应形状导航：定位有效载荷节点（data / result / video_info）、大会员限制判定
/// 纯导航、无 IO，输入是已解析好的 <see cref="JsonElement"/>
/// </summary>
internal static class PlayUrlResponse
{
    // playurl 接口对大会员限制没有专用错误码，只能认 message 文案；不同端点分别用 message / msg
    internal static bool IsVipRestricted(string webJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(webJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

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

    // 风控人机验证的凭据：HTTP 200、code=0，数据根下只有 v_voucher，既无 dash 也无 durl
    // 抛出可读错误，使其与 code=-352 同等待遇：按重试设置退避重来，而不是静默产出空轨道
    internal static void ThrowIfRiskControlled(JsonElement root)
    {
        if (!IsRiskControlled(root)
            && !(TryGetObject(root, "data", out var data) && IsRiskControlled(data))
            && !(TryGetObject(root, "result", out var result) && IsRiskControlled(result)))
        {
            return;
        }

        throw new InvalidOperationException(
            $"播放信息被风控拦截：接口要求人机验证（{VoucherName}），未返回任何播放地址。请稍后重试；若持续出现，请在浏览器打开该视频完成验证后再运行");
    }

    private const string VoucherName = "v_voucher";

    private static bool IsRiskControlled(JsonElement root)
    {
        return root.ValueKind == JsonValueKind.Object
               && root.TryGetProperty(VoucherName, out var voucher)
               && voucher.ValueKind == JsonValueKind.String
               && !string.IsNullOrEmpty(voucher.GetString( ));
    }

    // data 节点一次性判断完；v2 接口把有效载荷藏在 result.video_info 下
    internal static string? ResolveDataNodeName(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (data.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.Object)
        {
            return HasObject(result, "video_info") ? "video_info" : "result";
        }

        return HasObject(data, "data") ? "data" : null;
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
}
