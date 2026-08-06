using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core.Entity;
using BBDown.Mux;

using static BBDown.Core.Entity.Entity;

namespace BBDown.Drm;

/// <summary>
/// DRM 通道判定与 ffmpeg cbcs 解密执行。两条通道密文同为 CENC cbcs，拿到 key 后 ffmpeg 一行可解，
/// 区别只在 key 从哪来：bili_drm 靠用户提供的 --drm-key，widevine 无通用解法直接判不可解。
/// 解密产物为独立文件，源文件不覆盖，失败时调用方保留加密原件。
/// </summary>
internal static class DrmDecryptor
{
    /// <summary>bili_drm 的 KID 取自 bilidrm_uri 最后一个 // 之后（32 位 hex）。</summary>
    internal static string? KidFromUri(string? uri)
    {
        if (uri == null)
        {
            return null;
        }

        var i = uri.LastIndexOf("//", StringComparison.Ordinal);
        return i >= 0 ? uri[(i + 2)..] : uri;
    }

    internal static Task<DrmResult> DecryptAsync(Video track, string sourcePath, string destPath, DrmKeySource keys, string ffmpeg, CancellationToken ct = default)
    {
        return DecryptAsync(track.DrmType, track.BiliDrmUri, sourcePath, destPath, keys, ffmpeg, ct);
    }

    internal static Task<DrmResult> DecryptAsync(Audio track, string sourcePath, string destPath, DrmKeySource keys, string ffmpeg, CancellationToken ct = default)
    {
        return DecryptAsync(track.DrmType, track.BiliDrmUri, sourcePath, destPath, keys, ffmpeg, ct);
    }

    internal static async Task<DrmResult> DecryptAsync(string drmType, string? biliDrmUri, string sourcePath, string destPath, DrmKeySource keys, string ffmpeg, CancellationToken ct = default)
    {
        if (drmType == "widevine")
        {
            return DrmResult.Unsupported;
        }

        var key = keys.TryGetKey(KidFromUri(biliDrmUri));
        if (key == null)
        {
            return DrmResult.KeyMissing;
        }

        var code = await Muxer.RunExe(ffmpeg, BuildArgs(key, sourcePath, destPath), ct);
        return code == 0 && File.Exists(destPath) && new FileInfo(destPath).Length > 0
            ? DrmResult.Decrypted
            : DrmResult.Failed;
    }

    // 单 key 解密（cbcs 常量 IV，ffmpeg 自行从 tenc/senc 读取 KID 与子样本信息）
    internal static List<string> BuildArgs(string key, string sourcePath, string destPath)
    {
        return ["-loglevel", "warning", "-y", "-decryption_key", key, "-i", sourcePath, "-c", "copy", "-f", "mp4", "--", destPath];
    }
}
