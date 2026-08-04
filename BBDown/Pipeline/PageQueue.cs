using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Media;
using BBDown.Util;

using static BBDown.Core.Entity.Entity;
using static BBDown.Core.Logger;

namespace BBDown.Pipeline;

internal static class PageQueue
{
    public static async Task RunAsync(DownloadOptions myOption, WorkContext ctx, DownloadTask? relatedTask = null, CancellationToken ct = default)
    {
        var vInfo = ctx.VInfo!;
        var pagesInfo = vInfo.PagesInfo;
        //获取已选择的分 P 列表
        var selectedPages = PageSelect.Resolve(myOption, vInfo, ctx.Input);

        Log($"共计 {pagesInfo.Count} 个分 P，已选择：" + (selectedPages == null ? "ALL" : string.Join(",", selectedPages)));
        var totalPages = pagesInfo.Count;

        //过滤不需要的分 P
        if (selectedPages != null)
        {
            pagesInfo = [.. pagesInfo.Where(p => selectedPages.Contains(p.index.ToString( )))];
            if (pagesInfo.Count == 0)
            {
                LogWarn("未匹配到任何分 P（收藏夹可能为空或指定的分 P 不存在），跳过下载。");
                return;
            }
        }

        ctx = ctx with { SavePathFormat = SavePath.Resolve(myOption, totalPages, vInfo.IsBangumi, vInfo.IsBangumiEnd) };

        var errors = await RunPagesAsync(pagesInfo, myOption.StopOnError, async (p, token) =>
        {
            if (pagesInfo.Count > 1 && ctx.Delay > 0)
            {
                Log($"停顿 {ctx.Delay} 秒...");
                await Task.Delay(ctx.Delay * 1000, token);
            }

            Log($"开始解析 P{p.index}：{p.aid}...（{pagesInfo.IndexOf(p) + 1} / {pagesInfo.Count}）");

            if (myOption.SaveArchivesToFile && ArchiveLog.CheckArchive(p.aid, p.cid))
            {
                Log($"已下载过（aid：{p.aid} / cid：{p.cid}），跳过下载...");
                return;
            }

            var outcome = await PageDownload.RunAsync(p, myOption, ctx, pagesInfo, relatedTask, token);

            // 只有完整成功（含混流）才记归档；半截失败/中止不应标记为已下载
            // 试看片段同样不记，否则用户日后拿到充电权限重跑会被 CheckArchive 静默跳过
            if (myOption.SaveArchivesToFile && !outcome.Aborted && !outcome.Preview && !string.IsNullOrWhiteSpace(outcome.SavePath))
            {
                ArchiveLog.SaveArchive(p.aid, p.cid, outcome.SavePath);
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
        return string.Join(", ", items.Select(e => $"P{e.Page.index}（{e.Page.aid}）"));
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
