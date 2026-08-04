using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using static BBDown.Core.Entity.Entity;
using static BBDown.Core.Logger;

namespace BBDown;

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
            if (myOption.SaveArchivesToFile && !outcome.Aborted && !string.IsNullOrWhiteSpace(outcome.SavePath))
            {
                ArchiveLog.SaveArchive(p.aid, p.cid, outcome.SavePath);
            }
        }, ct);

        if (errors.Count > 0)
        {
            var list = string.Join(", ", errors.Select(e => $"P{e.Page.index}（{e.Page.aid}）"));
            LogError($"以下分 P 下载失败：{list}");
            throw new AggregateException(errors.Select(e => e.Error));
        }

        Log("任务完成");
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
            catch (OperationCanceledException)
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
