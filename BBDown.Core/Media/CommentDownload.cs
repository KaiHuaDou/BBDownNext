using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core.Comment;
using BBDown.Core.Download;
using BBDown.Core.Mux;

using static BBDown.Core.Logger;

namespace BBDown.Core.Media;

/// <summary>
/// 把已下载分 P 的评论区导出为 JSON / TXT（按 <c>--comments-formats</c>）
/// 评论区按 oid 绑定，与 cid / 分 P 无关，挂 PageQueue 时用局部 HashSet 按 aid 去重
/// 与视频下载互不干扰：抓取失败只告警，不影响视频本体
/// </summary>
public static class CommentDownload
{
    public static async Task RunAsync(WorkContext ctx, PageContext pageCtx, PipelineSink sink = default, CancellationToken token = default)
    {
        if (ctx.Run.CommentCount <= 0 || pageCtx.Page.Aid.Length == 0)
        {
            return;
        }

        if (!long.TryParse(pageCtx.Page.Aid, out var oid) || oid <= 0)
        {
            // 番剧 / 课程分集的 aid 可能为空或非数字，评论接口需要有效的 oid
            LogWarn("当前分 P 无有效 aid，跳过评论下载");
            return;
        }

        var document = await CommentFetcher.FetchAsync(
            1,
            oid.ToString(CultureInfo.InvariantCulture),
            ctx.Run.CommentCount,
            ctx.Run.CommentSortHot,
            ctx.Run.Content.Has(DownloadContent.FullComments),
            ctx.Fetch.Cfg,
            token);

        document.Title = pageCtx.Title;
        document.Bvid = pageCtx.Page.Bvid;

        var basePath = SavePath.Build(ctx, pageCtx, null, null);
        // 产物扩展名随混流方式/内容集修正，评论文件须与混流产物基底一致
        basePath = MuxFinish.ToOutputPath(basePath, ctx.Run.Mux, ctx.Run.Content.Has(DownloadContent.Video));

        Directory.CreateDirectory(Path.GetDirectoryName(basePath)!);

        var paths = await CommentWriter.WriteAsync(
            basePath, document, ctx.Run.CommentFormats, ctx.Run.Content.Has(DownloadContent.FullComments), token);

        foreach (var path in paths)
        {
            Log($"已保存评论：{path}");
            sink.Saved?.Invoke(path);
        }
    }
}
