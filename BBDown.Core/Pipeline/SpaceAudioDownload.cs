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
/// UP 主空间音频投稿下载编排：翻页拉取 AU 列表，逐条复用 <see cref="AudioDownload"/> 下载，
/// 产物落在 workDir/{UP 名}/ 下。与音视频链路独立，不构造 WorkContext、不探测 ffmpeg；
/// 逐条失败继续（失败聚合在末尾统一抛出），与 PageQueue 的分 P 失败语义一致。
/// </summary>
public static class SpaceAudioDownload
{
    private const int PageSize = 30;
    private const int MaxItems = 1000;

    private readonly record struct SongItem(long AuId, string Author);

    public static async Task RunAsync(long mid, DownloadRequest myOption, PipelineSink sink = default, CancellationToken ct = default)
    {
        var cfg = WorkSetup.ResolveConfig(myOption, ApiType.Web);
        await Buvid.InitAsync(ct);

        Log($"获取 UP 主 {mid} 的音频投稿...");
        var items = await CollectSongsAsync(mid, cfg, ct);
        if (items.Count == 0)
        {
            throw new InvalidOperationException($"UP 主 {mid} 没有可下载的音频投稿");
        }

        var upName = items[0].Author;
        Log($"共 {items.Count} 条音频");
        var workDir = WorkSetup.ResolveWorkDir(myOption);
        var dirName = FileNameUtil.GetValidFileName(upName);
        var itemDir = Path.Combine(workDir, string.IsNullOrEmpty(dirName) ? $"UP{mid}" : dirName);

        var failures = new List<string>( );
        var done = 0;
        foreach (var item in items)
        {
            ct.ThrowIfCancellationRequested( );
            Log($"[{done + 1}/{items.Count}] 下载音频 au{item.AuId}...");
            try
            {
                await AudioDownload.RunAsync(item.AuId, myOption with { WorkDir = itemDir }, sink, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception e)
            {
                LogWarn($"下载 au{item.AuId} 失败：{e.Message}");
                failures.Add($"au{item.AuId}：{e.Message}");
            }

            done++;
        }

        if (failures.Count > 0)
        {
            throw new InvalidOperationException($"共 {failures.Count}/{items.Count} 条音频下载失败：{string.Join("；", failures)}");
        }

        Log("音频下载完成");
    }

    // 按 pn 翻页拉取 song/upper；上限兜底防 pageCount 异常导致死循环
    private static async Task<List<SongItem>> CollectSongsAsync(long mid, AppConfig cfg, CancellationToken ct)
    {
        List<SongItem> items = [];
        var pn = 1;
        while (items.Count < MaxItems)
        {
            // song/upper 的用户参数名为 uid（BAC 文档表格标注 mid 有误，实测传 mid 静默返回空列表）
            var api = $"{BiliApi.SpaceAudioList}?uid={mid}&pn={pn}&ps={PageSize}&order=1";
            using var doc = JsonDocument.Parse(await GetWebSourceAsync(api, cfg, null, ct));
            var data = GetApiData(doc.RootElement, "UP 主音频列表");

            if (!TryGetArray(data, "data", out var songs))
            {
                throw new InvalidOperationException("获取 UP 主音频列表失败：接口未返回数据");
            }

            var got = 0;
            var author = "";
            foreach (var song in songs.EnumerateArray( ))
            {
                got++;
                var auId = song.ValueKind == JsonValueKind.Object
                    && song.TryGetProperty("id", out var id)
                    && id.ValueKind == JsonValueKind.Number
                    && id.TryGetInt64(out var value)
                    ? value
                    : 0;
                if (auId > 0)
                {
                    author = ReadStr(song, "uname");
                    items.Add(new SongItem(auId, author));
                }
            }

            // curPage / pageCount 缺失时按空页兜底退出
            var curPage = ReadLong(data, "curPage");
            var pageCount = ReadLong(data, "pageCount");
            if (got == 0 || (pageCount > 0 && curPage >= pageCount))
            {
                break;
            }

            pn++;
        }

        return items;
    }

    private static string ReadStr(JsonElement obj, string name)
    {
        return obj.ValueKind == JsonValueKind.Object
            && obj.TryGetProperty(name, out var v)
            && v.ValueKind == JsonValueKind.String
            ? v.GetString( ) ?? ""
            : "";
    }

    private static long ReadLong(JsonElement obj, string name)
    {
        return obj.ValueKind == JsonValueKind.Object
            && obj.TryGetProperty(name, out var v)
            && v.ValueKind == JsonValueKind.Number
            && v.TryGetInt64(out var value)
            ? value
            : 0;
    }
}
