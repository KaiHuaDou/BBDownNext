#pragma warning disable CA1308 // 产物文件名：小写扩展名与 URL 友好小写 hex

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core.Auth;
using BBDown.Core.Comment;
using BBDown.Core.Download;
using BBDown.Core.Entity;
using BBDown.Core.Opus;
using BBDown.Core.Util;
using BBDown.Core.Workflow;

using static BBDown.Core.Download.DownloadUtil;
using static BBDown.Core.Logger;

namespace BBDown.Core.Pipeline;

/// <summary>
/// 专栏（opus / cv）导出编排。与音视频下载链路完全独立：不构造 WorkContext、不探测 ffmpeg、不经过
/// SavePath.Format（后者在 SavePath.cs 硬编码 .mp4 后缀）。分流点在 Program.RunApp，
/// 早于 WorkSetup.Build（Build 会因缺 ffmpeg 抛异常）。
/// </summary>
public static class OpusDownload
{
    public static async Task RunAsync(DownloadRequest myOption, PipelineSink sink = default, CancellationToken token = default)
    {
        var workDir = WorkSetup.ResolveWorkDir(myOption);

        var input = myOption.Url;
        // b23.tv 短链展开（纯函数 TryParse 不触网，这里单独处理）
        if (input.Contains("b23.tv", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                input = await HTTPUtil.GetWebLocationAsync(input, token);
            }
            catch (Exception e)
            {
                LogWarn($"短链展开失败，按原输入解析：{e.Message}");
            }
        }

        if (!OpusInputResolver.TryParse(input, out var target))
        {
            throw new ArgumentException($"无法识别的专栏地址：{myOption.Url}");
        }

        var config = WorkSetup.ResolveConfig(myOption, ApiType.Web);

        // opus/detail 要求 Cookie 中带非空 buvid3；平时这一步在 VideoInfo.FetchAsync 完成，旁路后必须自己补
        await Buvid.InitAsync(token);

        Log("获取专栏信息...");
        var doc = await OpusFetcher.FetchAsync(target, config, token);
        Log($"标题：{doc.Title}");
        Log($"作者：{doc.AuthorName}");
        Log($"段落数：{doc.Paragraphs.Count}，图片数：{CountImages(doc)}");
        // serve 等宿主的任务契约回填（标题 / 保存路径），CLI 传 default 无回调
        sink.Meta?.Invoke(new VInfo
        {
            Title = doc.Title,
            Desc = "",
            Pic = "",
            PubTime = 0,
            PagesInfo = [],
        });

        var baseName = FileNameUtil.GetValidFileName(doc.Title);
        if (string.IsNullOrEmpty(baseName))
        {
            baseName = string.IsNullOrEmpty(doc.CvId) ? $"opus_{doc.OpusId}" : $"cv{doc.CvId}";
        }
        else
        {
            // 不同动态/专栏可能标题相同，追加动态号（cv 优先，否则 opus id）保证命名唯一，避免被自动跳过
            var idTag = doc.CvId.Length > 0 ? doc.CvId : doc.OpusId;
            if (idTag.Length > 0)
            {
                baseName = $"{baseName}_{idTag}";
            }
        }

        var mdPath = Path.Combine(workDir, baseName + ".md");

        // 评论导出先于 md 存在性早退：md 已存在时重跑仍能补抓缺失的评论产物（与视频链路「评论先于视频」同语义）
        if (myOption.Content.HasAny(DownloadContent.Comments | DownloadContent.FullComments)
            && myOption.CommentCount > 0 && !myOption.OnlyShowInfo)
        {
            await DownloadCommentsAsync(myOption, doc, mdPath, WorkSetup.ParseCommentFormats(myOption), config, sink, token);
        }

        // 与 MuxFinish.TrySkipExisting 同样的跳过语义
        if (File.Exists(mdPath) && new FileInfo(mdPath).Length > 0)
        {
            Log($"{mdPath} 已存在，跳过下载...");
            return;
        }

        IReadOnlyDictionary<string, string>? imageMap = null;
        if (myOption.Content.Has(DownloadContent.OpusImage) && CountImages(doc) > 0)
        {
            var imageDir = Path.Combine(workDir, baseName, "images");
            imageMap = await DownloadImagesAsync(doc, imageDir, $"{baseName}/images", config, token);
        }

        var markdown = OpusMarkdownRenderer.Render(doc, new OpusRenderOptions(
            EmbedFrontMatter: myOption.Content.Has(DownloadContent.FrontMatter),
            ImagePathMap: imageMap));

        // Encoding.UTF8 会写出 BOM，多数 YAML Frontmatter 解析器会因此认不出首行的 ---
        await File.WriteAllTextAsync(mdPath, markdown, new UTF8Encoding(false), token);
        Log($"已保存到 {mdPath}");
        sink.Saved?.Invoke(mdPath);
    }

    /// <summary>
    /// 专栏 / 图文的评论区导出：type 与 oid 由 <see cref="OpusDocument"/> 携带（专栏 12/cvid，图文动态取
    /// opus/detail 下发的 basic.comment_type / comment_id_str）。失败只告警，不影响 Markdown 导出。
    /// </summary>
    private static async Task DownloadCommentsAsync(DownloadRequest myOption, OpusDocument doc, string mdPath, CommentFormat[] formats, AppConfig config, PipelineSink sink, CancellationToken token)
    {
        if (doc.CommentType <= 0 || doc.CommentOid.Length == 0)
        {
            LogWarn("该内容未提供评论区定位信息，跳过评论下载");
            return;
        }

        // 目标格式产物齐备（存在且非空）则不再抓取，与 md 的跳过语义一致
        if (formats.All(f => CommentPath(mdPath, f) is { } path && File.Exists(path) && new FileInfo(path).Length > 0))
        {
            Log("评论文件已存在，跳过下载...");
            return;
        }

        var fullReplies = myOption.Content.Has(DownloadContent.FullComments);
        // 与 WorkSetup.Build 的评论排序解析同式：非 time 即热度
        var sortHot = !string.Equals(myOption.CommentSort, "time", StringComparison.OrdinalIgnoreCase);
        try
        {
            // reply/wbi/main 需要 WBI 签名：nav 探测补密钥（未登录也能拿到），缺失签名会被服务端以 -403 拒绝
            var (_, wbi) = await Account.ProbeAccountAsync(config, token);
            if (wbi.Length > 0)
            {
                config = config with { Wbi = wbi };
            }

            await RetryUtil.RetryAsync(async ( ) =>
            {
                var document = await CommentFetcher.FetchAsync(
                    doc.CommentType, doc.CommentOid, myOption.CommentCount, sortHot, fullReplies, config, token);
                document.Title = doc.Title;
                var paths = await CommentWriter.WriteAsync(mdPath, document, formats, fullReplies, token);
                foreach (var path in paths)
                {
                    Log($"已保存评论：{path}");
                    sink.Saved?.Invoke(path);
                }
            }, myOption.MaxRetry, "评论", token, ex => ex is not OperationCanceledException);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            LogWarn($"评论下载失败（不影响专栏导出）：{e.Message}");
        }
    }

    private static string CommentPath(string mdPath, CommentFormat format)
    {
        return Path.ChangeExtension(mdPath, $".comments.{format.ToString( ).ToLowerInvariant( )}");
    }

    private static int CountImages(OpusDocument doc)
    {
        return doc.Paragraphs.Sum(p => p.Images.Count);
    }

    private static async Task<IReadOnlyDictionary<string, string>> DownloadImagesAsync(
        OpusDocument doc, string imageDir, string relativeDir, AppConfig config, CancellationToken token)
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
        var downloadConfig = new DownloadConfig { Cookie = config.Cookie, NoForceHttp = true };

        // 图片下载有明确总量（urls.Count），按张数上报进度；CLI 经 ProgressBar 展示，事件服务 GUI 任务行
        using var stage = ProgressBus.BeginStage("下载图片");
        for (var i = 0; i < urls.Count; i++)
        {
            token.ThrowIfCancellationRequested( );
            var fileName = BuildImageFileName(urls[i], i + 1);
            var path = Path.Combine(imageDir, fileName);
            try
            {
                if (!(File.Exists(path) && new FileInfo(path).Length > 0))
                {
                    Log($"下载图片 [{i + 1}/{urls.Count}] {fileName}");
                    await DownloadFileAsync(urls[i], path, downloadConfig, token);
                }

                map[urls[i]] = $"{relativeDir}/{fileName}";
                ProgressBus.Publish((i + 1) / (double) urls.Count, 0, 0, $"图片 {i + 1}/{urls.Count}");
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
