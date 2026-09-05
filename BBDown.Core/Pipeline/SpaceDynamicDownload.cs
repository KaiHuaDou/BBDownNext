using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core.Download;
using BBDown.Core.Util;
using BBDown.Core.Workflow;

using static BBDown.Core.Logger;
using static BBDown.Core.Util.JsonUtil;

namespace BBDown.Core.Pipeline;

/// <summary>
/// UP 主空间动态流下载编排：翻页拉取动态流并按类型分发——图文动态（MAJOR_TYPE_OPUS）导出 Markdown、
/// 视频动态（MAJOR_TYPE_ARCHIVE）复用视频管道下载、转发动态递归取原动态按其类型处理；
/// 直播 / 剧集 / 充电等其余类型跳过。产物落在 workDir/{UP 名}/ 下，逐条失败继续（末尾汇总抛出）。
/// </summary>
public static class SpaceDynamicDownload
{
    // 转发递归深度上限：orig 指向根原动态，正常一层即达；上限仅防异常数据自嵌套
    private const int MaxForwardDepth = 3;

    // internal 供单测构造断言（TryResolveItem 的 out 形态）；OpusId 与 BvId 恰有其一非空
    internal readonly record struct DynamicItem(string OpusId, string BvId)
    {
        public bool HasOpus => OpusId.Length > 0;

        public bool HasVideo => BvId.Length > 0;
    }

    public static async Task RunAsync(long mid, DownloadRequest myOption, PipelineSink sink = default, ChannelWorkflowContext? workflow = null, CancellationToken ct = default)
    {
        var cfg = await SpaceDynamicFeed.ResolveConfigAsync(myOption, ct);

        Log($"获取 UP 主 {mid} 的动态...");
        var entries = await SpaceDynamicFeed.CollectEntriesAsync(mid, cfg, ct);

        var upName = "";
        var items = new List<DynamicItem>( );
        var skipped = 0;
        foreach (var entry in entries)
        {
            if (upName.Length == 0 && TryReadAuthor(entry, out var name))
            {
                upName = name;
            }

            if (TryResolveItem(entry, 0, out var item))
            {
                items.Add(item);
            }
            else
            {
                skipped++;
            }
        }

        if (items.Count == 0)
        {
            throw new InvalidOperationException($"UP 主 {mid} 没有可下载的动态（图文 / 视频 / 转发）");
        }

        Log($"共 {entries.Count} 条动态，可下载 {items.Count} 条，跳过 {skipped} 条（直播 / 剧集等不支持类型）");
        var workDir = WorkSetup.ResolveWorkDir(myOption);
        var dirName = FileNameUtil.GetValidFileName(upName);
        var itemDir = Path.Combine(workDir, string.IsNullOrEmpty(dirName) ? $"UP{mid}" : dirName);

        var failures = new List<string>( );
        var done = 0;
        foreach (var item in items)
        {
            ct.ThrowIfCancellationRequested( );
            // 图文取动态 id、视频取 bvid 作进度标签
            var label = item.HasVideo ? item.BvId : item.OpusId;
            Log($"[{done + 1}/{items.Count}] 下载动态 {label}...");
            try
            {
                if (item.HasVideo)
                {
                    var videoReq = myOption with { Url = $"{BiliApi.VideoPage}/{item.BvId}", WorkDir = itemDir };
                    await DownloadPipeline.RunAsync(videoReq, sink, workflow, ct);
                }
                else
                {
                    var opusReq = myOption with { Url = $"{BiliApi.OpusPage}/{item.OpusId}", WorkDir = itemDir };
                    await OpusDownload.RunAsync(opusReq, sink, ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception e)
            {
                LogWarn($"下载 {label} 失败：{e.Message}");
                failures.Add($"{label}：{e.Message}");
            }

            done++;
        }

        if (failures.Count > 0)
        {
            throw new InvalidOperationException($"共 {failures.Count}/{items.Count} 条动态下载失败：{string.Join("；", failures)}");
        }

        Log("动态下载完成");
    }

    // 动态条目 → 下载目标：图文（MAJOR_TYPE_OPUS）取动态 id，视频（MAJOR_TYPE_ARCHIVE）取 bvid，
    // 转发（module_dynamic.orig）递归取原动态按其类型处理；其余类型返回 false 由调用方跳过。
    // internal 供单测：JsonElement 为纯内存输入，与 TrackReader 系列纯函数同性质
    internal static bool TryResolveItem(JsonElement entry, int depth, out DynamicItem item)
    {
        item = default;
        if (entry.ValueKind != JsonValueKind.Object
            || !TryGetObject(entry, "modules", out var modules)
            || !TryGetObject(modules, "module_dynamic", out var moduleDynamic))
        {
            return false;
        }

        // 转发条目 major 为 MAJOR_TYPE_NONE，内容在 orig（与根原动态同构的动态条目）
        if (TryGetObject(moduleDynamic, "orig", out var orig) && orig.ValueKind == JsonValueKind.Object)
        {
            return depth < MaxForwardDepth && TryResolveItem(orig, depth + 1, out item);
        }

        if (!TryGetObject(moduleDynamic, "major", out var major)
            || !major.TryGetProperty("type", out var type)
            || type.GetString( ) is not { } majorType)
        {
            return false;
        }

        switch (majorType)
        {
            case "MAJOR_TYPE_OPUS":
                var opusId = ReadStr(entry, "id_str");
                if (opusId.Length == 0)
                {
                    return false;
                }

                item = new DynamicItem(opusId, "");
                return true;
            case "MAJOR_TYPE_ARCHIVE":
                var bvid = TryGetObject(major, "archive", out var archive) ? ReadStr(archive, "bvid") : "";
                if (bvid.Length == 0)
                {
                    return false;
                }

                item = new DynamicItem("", bvid);
                return true;
            default:
                return false;
        }
    }

    private static bool TryReadAuthor(JsonElement entry, out string name)
    {
        name = "";
        return entry.ValueKind == JsonValueKind.Object
            && TryGetObject(entry, "modules", out var modules)
            && TryGetObject(modules, "module_author", out var authorModule)
            && (name = ReadStr(authorModule, "name")).Length > 0;
    }

    private static string ReadStr(JsonElement obj, string name)
    {
        return obj.ValueKind == JsonValueKind.Object
            && obj.TryGetProperty(name, out var v)
            && v.ValueKind == JsonValueKind.String
            ? v.GetString( ) ?? ""
            : "";
    }
}
