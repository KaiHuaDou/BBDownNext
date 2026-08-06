namespace BBDown.Drm;

/// <summary>
/// 单条音视频轨的 DRM 解密结果。调用方据此决定用解密产物参与混流，还是保留加密原件。
/// </summary>
internal enum DrmResult
{
    /// <summary>解密成功，产物可用（DecryptAsync 返回解密后路径）</summary>
    Decrypted,
    /// <summary>bili_drm 通道但无可用 key（未提供或 KID 不匹配），提示如何传入 key</summary>
    KeyMissing,
    /// <summary>Widevine 通道，无 .NET 通用解法，不可自动解密</summary>
    Unsupported,
    /// <summary>有 key 但解密失败（ffmpeg 非零退出或产物为空）</summary>
    Failed
}
