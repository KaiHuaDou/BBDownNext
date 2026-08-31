using System.Text.Json.Serialization;

namespace BBDown.Serve.Auth;

/// <summary>扫码登录状态（供 WebUI 轮询端点读取）。</summary>
[JsonConverter(typeof(JsonStringEnumConverter<QrLoginState>))]
public enum QrLoginState
{
    WaitingScan,
    WaitingConfirm,
    Expired,
    Success,
    Failed,
}

/// <summary>扫码登录起点请求体：登录通道（web / tv / app，忽略大小写）。</summary>
public sealed record QrLoginStartRequest(string Channel);

/// <summary>扫码登录起点响应：二维码 PNG（base64）与轮询键 qrcodeKey。</summary>
public sealed record QrLoginStartResponse(string QrcodeKey, string QrPngBase64, string Channel);

/// <summary>扫码登录状态轮询响应：success 时携带凭据（WEB 为 cookie，TV / APP 为 accessToken）。</summary>
public sealed record QrLoginStatusResponse(
    QrLoginState State,
    string? AccountName,
    string? Cookie,
    string? AccessToken,
    string? RefreshToken,
    string? Error);