namespace BBDown.DRM;

/// <summary>
/// 单条音视频轨的 DRM 处理结果。调用方据此决定用解密产物参与混流，还是保留加密原件。
/// </summary>
internal enum DrmResult
{
    /// <summary>解密成功，产物可用</summary>
    Decrypted,
    /// <summary>bili_drm 通道无可用 key（未提供或 KID 不匹配）</summary>
    KeyMissing,
    /// <summary>widevine 通道缺 device.wvd / pssh，无法发起取钥</summary>
    DeviceMissing,
    /// <summary>widevine 取钥交互失败（license 服务器拒绝或响应校验不过）</summary>
    FetchFailed,
    /// <summary>有 key 但解密失败（ffmpeg 非零退出或产物为空）</summary>
    Failed
}
