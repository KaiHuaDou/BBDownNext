using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BBDown;

/// <summary>
/// 断点续传的显式状态。数据写在单个 .bbdown.part 里（各分片按 offset 并发写入），
/// 这份清单回答「这堆字节属于哪条流、下到哪了、能不能接着下」。
/// </summary>
internal sealed class PartManifest
{
    public int Version { get; set; } = PartFile.CurrentVersion;
    public string Fingerprint { get; set; } = "";
    /// <summary>-1 表示远端没给长度</summary>
    public long TotalSize { get; set; } = -1;
    public long ChunkSize { get; set; }
    /// <summary>服务器给的 ETag（优先）或 Last-Modified 原文，续传时原样回传</summary>
    public string? IfRange { get; set; }
    /// <summary>每个分片已写入的字节数，下标与 <see cref="PartFile.Ranges"/> 对应</summary>
    public long[] Completed { get; set; } = [];
    /// <summary>数据已校验通过并 move 成正式文件</summary>
    public bool Done { get; set; }
}

internal static class PartFile
{
    internal const int CurrentVersion = 1;
    internal const long DefaultChunkSize = 20 * 1024 * 1024;

    internal static string PartPath(string destPath)
    {
        return destPath + ".bbdown.part";
    }

    internal static string ManifestPath(string destPath)
    {
        return destPath + ".bbdown.json";
    }

    /// <summary>
    /// B 站 CDN 的 host 和 query（deadline/oi/trid）每次解析都不同，只有 path 里的
    /// .../&lt;cid&gt;-1-30280.m4s 唯一标识一条流。取 path 做指纹，既能让主备 URL 互相续传，
    /// 又能在用户换画质时自动判定旧数据作废。
    /// </summary>
    internal static string Fingerprint(string url)
    {
        var path = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.AbsolutePath : url;
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(path)))[..16];
    }

    /// <summary>按闭区间 [From, To] 切片；totalSize 未知时返回空表，由调用方退化为单流下载。</summary>
    internal static List<(long From, long To)> Ranges(long totalSize, long chunkSize)
    {
        List<(long From, long To)> ranges = [];
        if (totalSize <= 0)
        {
            return ranges;
        }

        if (chunkSize <= 0)
        {
            chunkSize = DefaultChunkSize;
        }

        for (var from = 0L; from < totalSize; from += chunkSize)
        {
            ranges.Add((from, Math.Min(from + chunkSize, totalSize) - 1));
        }

        return ranges;
    }

    internal static bool Matches(PartManifest manifest, string fingerprint, long totalSize)
    {
        return manifest.Fingerprint == fingerprint
               && (totalSize <= 0 || manifest.TotalSize == totalSize)
               && manifest.Completed.Length == Math.Max(Ranges(manifest.TotalSize, manifest.ChunkSize).Count, 1);
    }

    internal static long DownloadedBytes(PartManifest manifest)
    {
        return manifest.Completed.Sum( );
    }

    internal static PartManifest? TryLoad(string destPath)
    {
        var path = ManifestPath(destPath);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var manifest = JsonSerializer.Deserialize(File.ReadAllText(path), PartJsonContext.Default.PartManifest);
            return manifest?.Version == CurrentVersion ? manifest : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>先写临时文件再原子替换，避免进程被杀时留下半截 JSON 导致整份进度作废。</summary>
    internal static void Save(string destPath, PartManifest manifest)
    {
        var path = ManifestPath(destPath);
        var staging = path + ".tmp";
        File.WriteAllText(staging, JsonSerializer.Serialize(manifest, PartJsonContext.Default.PartManifest));
        File.Move(staging, path, true);
    }

    internal static void Discard(string destPath)
    {
        Utils.SafeDelete(PartPath(destPath));
        Utils.SafeDelete(ManifestPath(destPath));
    }
}

[JsonSerializable(typeof(PartManifest))]
internal sealed partial class PartJsonContext : JsonSerializerContext;
