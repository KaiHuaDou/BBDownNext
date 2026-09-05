using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core.Download;
using BBDown.Core.Util;

using static BBDown.Core.Logger;
using static BBDown.Core.Util.JsonUtil;

namespace BBDown.Core.Pipeline;

/// <summary>
/// UP 主空间图文 / 专栏投稿导出编排：翻页拉取动态流，仅提取图文动态（MAJOR_TYPE_OPUS），
/// 逐条复用 <see cref="OpusDownload"/> 导出 Markdown，产物落在 workDir/{UP 名}/ 下。
/// 视频 / 转发 / 直播 / 笔记等动态类型不在提取范围（空间动态页的全类型分发见 <see cref="SpaceDynamicDownload"/>）。
/// 与音视频链路独立，不构造 WorkContext。
/// </summary>
public static class SpaceOpusDownload
{
    // internal 供单测构造断言（TryGetOpus 的 out 形态）
    internal readonly record struct OpusItem(string OpusId, string Title, string Author);

    public static async Task RunAsync(long mid, DownloadRequest myOption, PipelineSink sink = default, CancellationToken ct = default)
    {
        var cfg = await SpaceDynamicFeed.ResolveConfigAsync(myOption, ct);

        Log($"获取 UP 主 {mid} 的图文动态...");
        var items = await CollectItemsAsync(mid, cfg, ct);
        if (items.Count == 0)
        {
            throw new InvalidOperationException($"UP 主 {mid} 没有可导出的图文动态");
        }

        var upName = items[0].Author;
        Log($"共 {items.Count} 条图文动态");
        var workDir = WorkSetup.ResolveWorkDir(myOption);
        var dirName = FileNameUtil.GetValidFileName(upName);
        var itemDir = Path.Combine(workDir, string.IsNullOrEmpty(dirName) ? $"UP{mid}" : dirName);

        var failures = new List<string>( );
        var done = 0;
        foreach (var item in items)
        {
            ct.ThrowIfCancellationRequested( );
            Log($"[{done + 1}/{items.Count}] 导出图文 {item.OpusId}...");
            var itemReq = myOption with { Url = $"{BiliApi.OpusPage}/{item.OpusId}", WorkDir = itemDir };
            try
            {
                await OpusDownload.RunAsync(itemReq, sink, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception e)
            {
                LogWarn($"导出 {item.OpusId} 失败：{e.Message}");
                failures.Add($"{item.OpusId}：{e.Message}");
            }

            done++;
        }

        if (failures.Count > 0)
        {
            throw new InvalidOperationException($"共 {failures.Count}/{items.Count} 条图文导出失败：{string.Join("；", failures)}");
        }

        Log("图文导出完成");
    }

    // 拉取全部动态 entry 后仅保留图文（MAJOR_TYPE_OPUS）
    private static async Task<List<OpusItem>> CollectItemsAsync(long mid, AppConfig cfg, CancellationToken ct)
    {
        List<OpusItem> items = [];
        foreach (var entry in await SpaceDynamicFeed.CollectEntriesAsync(mid, cfg, ct))
        {
            if (TryGetOpus(entry, out var item))
            {
                items.Add(item);
            }
        }

        return items;
    }

    // 仅提取图文动态（major.type == MAJOR_TYPE_OPUS）：id_str 即动态 id（与 basic.jump_url 的 /opus/{id} 一致）。
    // internal 供单测：JsonElement 为纯内存输入，与 TrackReader 系列纯函数同性质
    internal static bool TryGetOpus(JsonElement entry, out OpusItem item)
    {
        item = default;
        if (entry.ValueKind != JsonValueKind.Object
            || !TryGetObject(entry, "modules", out var modules)
            || !TryGetObject(modules, "module_dynamic", out var moduleDynamic)
            || !TryGetObject(moduleDynamic, "major", out var major)
            || !major.TryGetProperty("type", out var type)
            || type.GetString( ) != "MAJOR_TYPE_OPUS")
        {
            return false;
        }

        var opusId = ReadStr(entry, "id_str");
        if (opusId.Length == 0)
        {
            return false;
        }

        var title = TryGetObject(major, "opus", out var opus) ? ReadStr(opus, "title") : "";
        var author = TryGetObject(modules, "module_author", out var authorModule) ? ReadStr(authorModule, "name") : "";
        item = new OpusItem(opusId, title, author);
        return true;
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
