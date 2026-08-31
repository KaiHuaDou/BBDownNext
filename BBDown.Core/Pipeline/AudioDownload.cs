using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core.Download;
using BBDown.Core.Entity;
using BBDown.Core.Music;
using BBDown.Core.Util;
using BBDown.Core.Workflow;

using static BBDown.Core.Download.DownloadUtil;
using static BBDown.Core.Logger;

namespace BBDown.Core.Pipeline;

/// <summary>
/// 音频投稿（AU）下载编排。与专栏导出同为独立链路：不构造 WorkContext、不探测 ffmpeg，
/// 产物为音频文件（m4a / mp3 / flac）与可选歌词 .lrc，不经 SavePath.Format（其硬编码 .mp4）。
/// 分流点在 WorkerDispatcher，早于 DownloadPipeline。
/// </summary>
public static class AudioDownload
{
    public static async Task RunAsync(long auId, DownloadRequest myOption, PipelineSink sink = default, CancellationToken ct = default)
    {
        var workDir = WorkSetup.ResolveWorkDir(myOption);
        var cfg = WorkSetup.ResolveConfig(myOption, ApiType.Web);

        Log($"获取音频信息：au{auId}...");
        var info = await AudioFetcher.FetchInfoAsync(auId, cfg, ct);
        Log($"标题：{info.Title}");
        Log($"作者：{info.Author}");
        // serve 等宿主的任务契约回填（标题 / 封面 / 发布时间），CLI 传 default 无回调
        sink.Meta?.Invoke(new VInfo
        {
            Title = info.Title,
            Desc = "",
            Pic = info.Cover,
            PubTime = info.PublishTime,
            PagesInfo = [],
        });

        var playUrl = await AudioFetcher.FetchPlayUrlAsync(auId, cfg, ct);
        if (playUrl.Type < 0)
        {
            LogWarn("该音频为付费 / 大会员曲目，当前仅能下载试听片段（30 秒左右）。");
        }

        var baseName = FileNameUtil.GetValidFileName(info.Title);
        if (string.IsNullOrEmpty(baseName))
        {
            baseName = $"au{auId}";
        }
        else
        {
            // 同一 UP 可能存在同名音频，追加 au 号保证唯一，避免被跳过逻辑误判已下载
            baseName = $"{baseName}_{auId}";
        }

        var filePath = Path.Combine(workDir, baseName + ResolveExt(playUrl.Url));

        // 与 OpusDownload / MuxFinish.TrySkipExisting 同样的跳过语义
        if (File.Exists(filePath) && new FileInfo(filePath).Length > 0)
        {
            Log($"{filePath} 已存在，跳过下载...");
            return;
        }

        // 与专栏图片同款直连配置：音频 CDN 用 https 即可，NoForceHttp 避免被强制降成 http；
        // downloader 自动向 ProgressBus 上报进度（ProgressBar / serve 任务行共用）
        var config = new DownloadConfig { Cookie = cfg.Cookie, NoForceHttp = true };
        using var stage = ProgressBus.BeginStage("下载音频");
        await DownloadAsync(playUrl.Url, filePath, config, resumable: true, ct);
        Log($"已保存到 {filePath}");
        sink.Saved?.Invoke(filePath);

        // 歌词为附加产物：失败或无歌词仅告警，不影响音频文件产出
        try
        {
            var lyric = await AudioFetcher.FetchLyricAsync(auId, cfg, ct);
            if (lyric.Length > 0)
            {
                // 与 OpusMarkdownRenderer 一致：UTF8 不带 BOM，避免部分 lrc 解析器认不出首行时间轴
                var lyricPath = Path.Combine(workDir, baseName + ".lrc");
                await File.WriteAllTextAsync(lyricPath, lyric, new UTF8Encoding(false), ct);
                Log($"歌词已保存到 {lyricPath}");
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            LogWarn($"歌词下载失败（不影响音频下载）：{e.Message}");
        }
    }

    // 从流 URL 取扩展名；取不到或非音频扩展时兜底 .m4a（B 站音频流当前恒为 m4a 容器）
    internal static string ResolveExt(string url)
    {
        var clean = url.Split('?')[0];
        var ext = Path.GetExtension(clean);
        return ext is ".mp3" or ".m4a" or ".flac" ? ext : ".m4a";
    }
}
