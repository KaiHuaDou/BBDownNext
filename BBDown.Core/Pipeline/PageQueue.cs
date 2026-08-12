using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core.Media;
using BBDown.Core.Util;

using static BBDown.Core.Logger;
using BBDown.Core.Entity;
using BBDown.Core.Download;

namespace BBDown.Core.Pipeline;

internal static class PageQueue
{
    public static async Task RunAsync(DownloadRequest myOption, RunConfig runConfig, FetchResult fetch, PipelineSink sink = default, CancellationToken ct = default)
    {
        var vInfo = fetch.VInfo;
        var pagesInfo = vInfo.PagesInfo;
        //获取已选择的分 P 列表：交互式优先，此时命令行 --pages 无意义
        List<string>? selectedPages;
        if (myOption.InteractivePages)
        {
            if (!string.IsNullOrWhiteSpace(myOption.Pages))
            {
                LogWarn("已同时指定 --interactive-pages 与 --pages，以交互选择为准。");
            }

            selectedPages = PageSelect.ResolveInteractive(vInfo);
        }
        else
        {
            selectedPages = PageSelect.Resolve(myOption, vInfo, runConfig.Input);
        }

        Log($"共计 {pagesInfo.Count} 个分 P，已选择：" + (selectedPages == null ? "ALL" : string.Join(",", selectedPages)));
        var totalPages = pagesInfo.Count;

        //过滤不需要的分 P
        if (selectedPages != null)
        {
            pagesInfo = [.. pagesInfo.Where(p => selectedPages.Contains(p.Index.ToString( )))];
            if (pagesInfo.Count == 0)
            {
                LogWarn("未匹配到任何分 P（收藏夹可能为空或指定的分 P 不存在），跳过下载。");
                return;
            }
        }

        var savePathFormat = SavePath.Resolve(myOption, totalPages, vInfo.IsBangumi, vInfo.IsBangumiEnd);

        // 一次性组装不可变上下文：启动参数（RunConfig）+ 解析结果（FetchResult）+ 保存路径模板
        var ctx = new WorkContext(runConfig, fetch, savePathFormat);

        // 评论区按 aid 绑定，与 cid / 分 P 无关；多 P 同 aid 只抓一次
        var commentedAids = new HashSet<string>(StringComparer.Ordinal);

        var isFirstPage = true;
        var errors = await RunPagesAsync(pagesInfo, myOption.StopOnError, async (p, token) =>
        {
            Log($"开始解析 P{p.Index}：{p.Aid}...（{pagesInfo.IndexOf(p) + 1} / {pagesInfo.Count}）");

            // 评论区关闭也能立刻反馈，视频下载失败也不丢评论；放在视频下载之前。--info-only 仅解析不产出评论。
            // o/O 只是开关，评论数量走 --comments-count：两者都满足才真正抓取
            if (ctx.Run.Content.HasAny(DownloadContent.Comments | DownloadContent.FullComments)
                && ctx.Run.CommentCount > 0 && !myOption.OnlyShowInfo && commentedAids.Add(p.Aid))
            {
                await CommentDownload.RunAsync(ctx, PageDownload.BuildPageContext(p, ctx, pagesInfo), sink, token);
            }

            // --delay-per-page 是分 P 间隔：首个分 P 与评论下载都不参与等待
            if (!isFirstPage && ctx.Run.Delay > 0)
            {
                Log($"停顿 {ctx.Run.Delay} 秒...");
                await Task.Delay(ctx.Run.Delay * 1000, token);
            }

            isFirstPage = false;

            if (myOption.SaveArchivesToFile && ArchiveLog.CheckArchive(p.Aid, p.Cid))
            {
                Log($"已下载过（aid：{p.Aid} / cid：{p.Cid}），跳过下载...");
                return;
            }

            var outcome = await PageDownload.RunAsync(p, myOption, ctx, pagesInfo, sink, token);

            // 只有完整成功（含混流）才记归档；半截失败/中止不应标记为已下载
            // 试看片段同样不记，否则用户日后拿到充电权限重跑会被 CheckArchive 静默跳过
            if (myOption.SaveArchivesToFile && !outcome.Aborted && !outcome.Preview && !string.IsNullOrWhiteSpace(outcome.SavePath))
            {
                ArchiveLog.SaveArchive(p.Aid, p.Cid, outcome.SavePath);
            }
        }, ct);

        if (errors.Count > 0)
        {
            var previews = errors.Where(e => e.Error is ChargedPreviewException).ToList( );
            var failures = errors.Where(e => e.Error is not ChargedPreviewException).ToList( );
            if (previews.Count > 0)
            {
                LogWarn($"以下分 P 为充电专属试看片段，已跳过：{FormatPages(previews)}");
            }

            if (failures.Count > 0)
            {
                LogError($"以下分 P 下载失败：{FormatPages(failures)}");
            }

            throw new AggregateException(errors.Select(e => e.Error));
        }

        Log("任务完成");
    }

    private static string FormatPages(List<(Page Page, Exception Error)> items)
    {
        return string.Join(", ", items.Select(e => $"P{e.Page.Index}（{e.Page.Aid}）"));
    }

    /// <summary>
    /// 逐个跑分 P 并收集失败：默认（stopOnError=false）遇到异常继续下一个，末尾一并返回；
    /// stopOnError=true 时第一个异常即停。Ctrl+C 的 OperationCanceledException 不被吞，直接上抛。
    /// 具体的延迟、归档校验、下载逻辑都在传入的委托里，本函数只负责"跑 + 聚合失败"。
    /// </summary>
    internal static async Task<List<(Page Page, Exception Error)>> RunPagesAsync(
        IReadOnlyList<Page> pages, bool stopOnError,
        Func<Page, CancellationToken, Task> run, CancellationToken ct)
    {
        var errors = new List<(Page, Exception)>( );
        foreach (var page in pages)
        {
            try
            {
                await run(page, ct);
            }
            // 仅当用户真的取消（ct 已请求取消）时才上抛；HttpClient 超时等瞬态故障被包装成
            // OperationCanceledException 但 ct 未取消，应落入普通错误分支继续下载其余分 P（§2.2）
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                errors.Add((page, ex));
                if (stopOnError)
                {
                    break;
                }
            }
        }

        return errors;
    }
}
