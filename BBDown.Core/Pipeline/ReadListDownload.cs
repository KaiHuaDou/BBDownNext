using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core.Download;
using BBDown.Core.Util;

using static BBDown.Core.Logger;
using static BBDown.Core.Util.HTTPUtil;
using static BBDown.Core.Util.JsonUtil;

namespace BBDown.Core.Pipeline;

/// <summary>
/// 文集（readlist）导出编排：拉取文集内全部文章 id，逐篇复用 <see cref="OpusDownload"/> 导出 Markdown。
/// 与音视频链路独立：不构造 WorkContext、不探测 ffmpeg，产物落在 workDir/{文集名}/ 下；
/// 逐篇失败继续（失败聚合在末尾统一抛出），与 PageQueue 的分 P 失败语义一致。
/// </summary>
public static class ReadListDownload
{
    public static async Task RunAsync(long rlId, DownloadRequest myOption, PipelineSink sink = default, CancellationToken ct = default)
    {
        var cfg = WorkSetup.ResolveConfig(myOption, ApiType.Web);
        await Buvid.InitAsync(ct);

        Log("获取文集信息...");
        var (name, articles) = await FetchArticlesAsync(rlId, cfg, ct);
        if (articles.Count == 0)
        {
            throw new InvalidOperationException($"文集 rl{rlId} 内没有可导出的文章");
        }

        Log($"文集「{name}」共 {articles.Count} 篇文章");
        var workDir = WorkSetup.ResolveWorkDir(myOption);
        var dirName = FileNameUtil.GetValidFileName(name);
        var itemDir = Path.Combine(workDir, string.IsNullOrEmpty(dirName) ? $"readlist_{rlId}" : dirName);

        var failures = new List<string>( );
        var done = 0;
        foreach (var cvId in articles)
        {
            ct.ThrowIfCancellationRequested( );
            Log($"[{done + 1}/{articles.Count}] 导出专栏 cv{cvId}...");
            var itemReq = myOption with { Url = $"{BiliApi.ReadPage}/cv{cvId}", WorkDir = itemDir };
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
                LogWarn($"导出 cv{cvId} 失败：{e.Message}");
                failures.Add($"cv{cvId}：{e.Message}");
            }

            done++;
        }

        if (failures.Count > 0)
        {
            throw new InvalidOperationException($"文集中 {failures.Count}/{articles.Count} 篇文章导出失败：{string.Join("；", failures)}");
        }

        Log("文集导出完成");
    }

    private static async Task<(string Name, List<long> Articles)> FetchArticlesAsync(long rlId, AppConfig cfg, CancellationToken ct)
    {
        var api = $"{BiliApi.ReadListArticles}?id={rlId}";
        using var doc = JsonDocument.Parse(await GetWebSourceAsync(api, cfg, null, ct));
        var data = GetApiData(doc.RootElement, "文集信息");

        var name = TryGetObject(data, "list", out var list) && ReadStr(list, "name") is { Length: > 0 } listName
            ? listName
            : $"readlist_{rlId}";

        List<long> articles = [];
        if (TryGetArray(data, "articles", out var array))
        {
            foreach (var item in array.EnumerateArray( ))
            {
                if (item.ValueKind == JsonValueKind.Object
                    && item.TryGetProperty("id", out var id)
                    && id.ValueKind == JsonValueKind.Number
                    && id.TryGetInt64(out var cvId)
                    && cvId > 0)
                {
                    articles.Add(cvId);
                }
            }
        }

        return (name, articles);
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
