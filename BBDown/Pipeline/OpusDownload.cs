using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Auth;
using BBDown.Core;
using BBDown.Core.Opus;
using BBDown.Core.Util;

using static BBDown.Core.Logger;
using static BBDown.Download.DownloadUtil;

namespace BBDown.Pipeline;

/// <summary>
/// 专栏（opus / cv）导出编排。与音视频下载链路完全独立：不构造 WorkContext、不探测 ffmpeg、不经过
/// SavePath.Format（后者在 SavePath.cs 硬编码 .mp4 后缀）。分流点在 Program.RunApp，
/// 早于 WorkSetup.Build（Build 会因缺 ffmpeg 抛异常）。
/// </summary>
internal static class OpusDownload
{
    internal static async Task RunAsync(DownloadOptions myOption, bool allowBareId = true, CancellationToken ct = default)
    {
        Config.SetDebugLog(myOption.Debug);
        if (!string.IsNullOrEmpty(myOption.UserAgent))
        {
            HTTPUtil.SetUserAgent(myOption.UserAgent);
        }

        var workDir = WorkSetup.ResolveWorkDir(myOption);

        var input = myOption.Url;
        // b23.tv 短链展开（纯函数 TryParse 不触网，这里单独处理）
        if (input.Contains("b23.tv", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                input = await HTTPUtil.GetWebLocationAsync(input, ct);
            }
            catch (Exception e)
            {
                LogWarn($"短链展开失败，按原输入解析：{e.Message}");
            }
        }

        if (!OpusInputResolver.TryParse(input, out var target, allowBareId))
        {
            throw new ArgumentException($"无法识别的专栏地址：{myOption.Url}");
        }

        // 专栏只需要 cookie，不需要 token / wbi；凭据加载与 VideoInfo.FetchAsync 保持一致
        var (cookie, _) = CredentialStore.LoadAll(myOption.Cookie, myOption.AccessToken, false, false);
        var cfg = new AppConfig(cookie, "", myOption.Host, myOption.EpHost, myOption.TvHost, myOption.Area, "");

        // opus/detail 要求 Cookie 中带非空 buvid3；平时这一步在 VideoInfo.FetchAsync 完成，旁路后必须自己补
        await Buvid.InitAsync(ct);

        Log("获取专栏信息...");
        var doc = await OpusFetcher.FetchAsync(target, cfg, ct);
        Log($"标题：{doc.Title}");
        Log($"作者：{doc.AuthorName}");
        Log($"段落数：{doc.Paragraphs.Count}，图片数：{CountImages(doc)}");

        var baseName = FileNameUtil.GetValidFileName(doc.Title);
        if (string.IsNullOrEmpty(baseName))
        {
            baseName = string.IsNullOrEmpty(doc.CvId) ? $"opus_{doc.OpusId}" : $"cv{doc.CvId}";
        }

        var mdPath = Path.Combine(workDir, baseName + ".md");

        // 与 MuxFinish.TrySkipExisting 同样的跳过语义
        if (File.Exists(mdPath) && new FileInfo(mdPath).Length > 0)
        {
            Log($"{mdPath} 已存在，跳过下载...");
            return;
        }

        IReadOnlyDictionary<string, string>? imageMap = null;
        if (!myOption.NoImages && CountImages(doc) > 0)
        {
            var imageDir = Path.Combine(workDir, baseName, "images");
            imageMap = await DownloadImagesAsync(doc, imageDir, $"{baseName}/images", cfg, ct);
        }

        var markdown = OpusMarkdownRenderer.Render(doc, new OpusRenderOptions(
            EmbedFrontMatter: !myOption.NoMetadata,
            ImagePathMap: imageMap));

        // Encoding.UTF8 会写出 BOM，多数 YAML front matter 解析器会因此认不出首行的 ---
        await File.WriteAllTextAsync(mdPath, markdown, new UTF8Encoding(false), ct);
        Log($"已保存到 {mdPath}");
    }

    private static int CountImages(OpusDocument doc)
    {
        return doc.Paragraphs.Sum(p => p.Images.Count);
    }

    private static async Task<IReadOnlyDictionary<string, string>> DownloadImagesAsync(
        OpusDocument doc, string imageDir, string relativeDir, AppConfig cfg, CancellationToken ct)
    {
        var urls = new List<string>( );
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in doc.Paragraphs)
        {
            foreach (var img in p.Images)
            {
                var u = OpusImageUtil.Normalize(img.Url);
                if (u.Length > 0 && seen.Add(u))
                {
                    urls.Add(u);
                }
            }
        }

        if (urls.Count == 0)
        {
            return new Dictionary<string, string>( );
        }

        Directory.CreateDirectory(imageDir);
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        // 原图 CDN 用 https 即可，NoForceHttp 避免被 DownloadUtil 降成 http
        var config = new DownloadConfig { Cookie = cfg.Cookie, NoForceHttp = true };

        for (var i = 0; i < urls.Count; i++)
        {
            ct.ThrowIfCancellationRequested( );
            var fileName = BuildImageFileName(urls[i], i + 1);
            var path = Path.Combine(imageDir, fileName);
            try
            {
                if (!(File.Exists(path) && new FileInfo(path).Length > 0))
                {
                    Log($"下载图片 [{i + 1}/{urls.Count}] {fileName}");
                    await DownloadFileAsync(urls[i], path, config, ct);
                }

                map[urls[i]] = $"{relativeDir}/{fileName}";
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                LogWarn($"图片下载失败，将保留远程链接：{urls[i]}（{e.Message}）");
            }
        }

        return map;
    }

    private static string BuildImageFileName(string url, int index)
    {
        var clean = url;
        var q = clean.IndexOf('?');
        if (q >= 0)
        {
            clean = clean[..q];
        }

        var at = clean.LastIndexOf('@');
        var slash = clean.LastIndexOf('/');
        if (at > slash)
        {
            clean = clean[..at];
        }

        var ext = Path.GetExtension(clean).ToLowerInvariant( );
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".gif", ".webp", ".avif", ".bmp",
        };
        if (string.IsNullOrEmpty(ext) || !allowed.Contains(ext))
        {
            ext = ".jpg";
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)))[..8].ToLowerInvariant( );
        return $"{index:D3}-{hash}{ext}";
    }
}
