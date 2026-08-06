using System.Globalization;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core.Comment;
using BBDown.Mux;
using BBDown.Util;

using static BBDown.Core.Logger;

namespace BBDown.Media;

/// <summary>
/// 把已下载分 P 的评论区导出为 JSON / TXT（按 <c>--comment-formats</c>）。
/// 评论区按 aid 绑定，与 cid / 分 P 无关，挂 PageQueue 时用局部 HashSet 按 aid 去重；
/// 与视频下载互不干扰：抓取失败只告警，不影响视频本体。
/// </summary>
internal static class CommentDownload
{
    // JsonSerializerContext 的 Encoder 默认会把中文转成 \u4e2d\u6587，AOT 下必须显式放开转义
    private static readonly CommentJsonContext JsonContext = new(new JsonSerializerOptions
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    });

    public static async Task RunAsync(WorkContext ctx, PageContext pageCtx, PipelineSink sink = default, CancellationToken ct = default)
    {
        if (ctx.Run.CommentCount <= 0 || pageCtx.Page.aid.Length == 0)
        {
            return;
        }

        if (!long.TryParse(pageCtx.Page.aid, out var oid) || oid <= 0)
        {
            // 番剧 / 课程分集的 aid 可能为空或非数字，评论接口需要有效的 oid
            LogWarn("当前分 P 无有效 aid，跳过评论下载");
            return;
        }

        var document = await CommentFetcher.FetchAsync(
            oid.ToString(CultureInfo.InvariantCulture),
            ctx.Run.CommentCount,
            ctx.Run.CommentSortHot,
            ctx.Run.Content.Has(DownloadContent.FullComments),
            ctx.Fetch.Cfg,
            ct);

        document.Title = pageCtx.Title;
        document.Bvid = pageCtx.Page.bvid;

        var basePath = SavePath.Build(ctx, pageCtx, null, null);
        // 内容集无 v（仅音频）时产物为 .m4a，评论文件须与之一致
        if (!ctx.Run.Content.Has(DownloadContent.Video))
        {
            basePath = MuxFinish.ToAudioOnlyPath(basePath);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(basePath)!);

        foreach (var format in ctx.Run.CommentFormats)
        {
            var path = Path.ChangeExtension(basePath, $".comments.{format.ToString( ).ToLowerInvariant( )}");
            try
            {
                switch (format)
                {
                    case CommentFormat.Json:
                        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(document, JsonContext.CommentDocument), ct);
                        break;
                    case CommentFormat.Txt:
                        await File.WriteAllTextAsync(path, CommentRenderer.Render(document, ctx.Run.Content.Has(DownloadContent.FullComments)), ct);
                        break;
                }
            }
            catch (IOException ex) when (ex is PathTooLongException or DirectoryNotFoundException)
            {
                // FileNameUtil 的 200 字节截断只作用于标题，再追加 .comments.json 可能越限；不阻断其余格式
                LogWarn($"评论文件因路径过长无法写入（{path}）：{ex.Message}");
                continue;
            }

            Log($"已保存评论：{path}");
            sink.Saved?.Invoke(path);
        }
    }
}
