using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core.Util;

namespace BBDown.DRM;

/// <summary>
/// cbcs 解密执行。两条通道密文同为 CENC cbcs，拿到 key 后 ffmpeg 一行可解；
/// key 的获取（密钥表 / Widevine CDM）由 <see cref="DrmKeys"/> 收敛，本类不感知通道差异。
/// 解密产物为独立文件，源文件不覆盖，失败时调用方保留加密原件。
/// </summary>
internal static class DrmDecryptor
{
    public static async Task<DrmResult> DecryptAsync(string drmType, string? biliDrmUri, string? psshBase64, string sourcePath, string destPath, DrmKeySource keys, string ffmpeg, string? wvdPath, CancellationToken token = default)
    {
        var (key, failure) = await DrmKeys.ResolveAsync(drmType, biliDrmUri, psshBase64, keys, wvdPath, token);
        if (failure is not null)
        {
            return failure.Value;
        }

        var code = await Utils.RunExe(ffmpeg, BuildArgs(key!, sourcePath, destPath), token);
        return code == 0 && File.Exists(destPath) && new FileInfo(destPath).Length > 0
            ? DrmResult.Decrypted
            : DrmResult.Failed;
    }

    // 单 key 解密（cbcs 常量 IV，ffmpeg 自行从 tenc/senc 读取 KID 与子样本信息）
    public static List<string> BuildArgs(string key, string sourcePath, string destPath)
    {
        return ["-loglevel", "warning", "-y", "-decryption_key", key, "-i", sourcePath, "-c", "copy", "-f", "mp4", "--", destPath];
    }
}
