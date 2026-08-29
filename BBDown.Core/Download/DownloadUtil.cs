using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using static BBDown.Core.Logger;
using static BBDown.Core.Util.Utils;

namespace BBDown.Core.Download;

public static class DownloadUtil
{
    // CMCC 的 CDN 节点不支持 Range/多线程分片，强行并发会整体失败，故对其强制单线程
    private const string CmccCdnMarker = "-cmcc-";

    /// <summary>封面、弹幕这类一次性小文件：下完即走，不保留续传状态。</summary>
    public static Task DownloadFileAsync(string url, string path, DownloadConfig config, CancellationToken ct = default)
    {
        return DownloadAsync(url, path, config, resumable: false, ct);
    }

    /// <summary>
    /// 唯一的下载入口。数据经 downloader 落到 &lt;path&gt;.download，完成后自动改名为正式文件；
    /// 失败或取消时 .download 保留（内嵌续传元数据），下次重跑接着下。
    /// </summary>
    public static async Task DownloadAsync(string url, string path, DownloadConfig config, bool resumable = true, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(url))
        {
            return;
        }

        if (!config.NoForceHttp)
        {
            url = ReplaceUrl(url);
        }

        LogDebug("Start downloading: {0}", url);

        var destDir = Path.GetDirectoryName(path)!;
        if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
        {
            Directory.CreateDirectory(destDir);
        }

        var singleThread = config.SingleThread;
        if (!singleThread && url.Contains(CmccCdnMarker))
        {
            LogWarn("检测到 CMCC 域名 CDN，已经禁用多线程。");
            singleThread = true;
        }

        if (config.UseAria2c)
        {
            // 最终文件已完整产出过：直接跳过（aria2 的续传元数据是其专属 .aria2 控制文件）
            if (File.Exists(path))
            {
                LogDebug("文件已下载过，跳过下载");
                return;
            }

            await BBDownAria2c.RunAsync(config.Aria2cPath ?? "aria2c", BBDownAria2c.BuildArgs(url, path, config.Aria2cArgs, config.Cookie, singleThread, config.ParallelCount), ct);
            if (File.Exists(path + ".aria2") || !File.Exists(path))
            {
                throw new InvalidOperationException("aria2 下载可能存在错误");
            }

            return;
        }

        // 目标文件已完整产出过：直接跳过（.download 只在中断时残留，此时最终文件不存在）
        if (File.Exists(path))
        {
            LogDebug("文件已下载过，跳过下载");
            return;
        }

        await DownloaderAdapter.RunAsync(url, path, config, singleThread, resumable, ct);
    }

    // 删除目标文件及其 downloader 临时文件（混流成功等场景的清理）
    internal static void Discard(string path)
    {
        SafeDelete(path);
        SafeDelete(path + ".download");
    }

    /// <summary>
    /// 将下载地址强制转换为 HTTP
    /// </summary>
    private static string ReplaceUrl(string url)
    {
        if (url.Contains(".mcdn.bilivideo.cn:"))
        {
            LogDebug("对 [*.mcdn.bilivideo.cn:xxx] 域名不做处理");
            return url;
        }

        LogDebug("将 https 更改为 http");
        return url.Replace("https:", "http:");
    }
}
