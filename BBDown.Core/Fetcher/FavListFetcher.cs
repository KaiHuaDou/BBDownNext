using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core.Entity;

using static BBDown.Core.Logger;
using static BBDown.Core.ResourceId;
using static BBDown.Core.Util.HTTPUtil;
using static BBDown.Core.Util.JsonUtil;

namespace BBDown.Core.Fetcher;

/// <summary>
/// 收藏夹解析
/// https://space.bilibili.com/3/favlist
///
/// </summary>
public static class FavListFetcher
{
    // 翻页硬上限：防服务端异常（恒回满页重复数据）把循环变成请求洪泛（与 SpaceListFetcher 的 MaxPages 一致）
    private const int MaxPages = 1000;
    private const int PageSize = 20;
    // 多 P 详情回填的并发上限（与 SpaceListFetcher / WatchLaterFetcher 一致）
    private const int BackfillConcurrency = 8;

    public static async Task<VInfo> FetchAsync(Fav fav, AppConfig cfg, CancellationToken ct = default)
    {
        var favId = await ResolveFavIdAsync(fav, cfg, ct);
        var (medias, title, intro, pubTime) = await CollectMediasAsync(favId, cfg, ct);
        var pagesInfo = await ExpandPagesAsync(medias, cfg, ct);
        return new VInfo
        {
            Title = title.Trim( ),
            Desc = intro.Trim( ),
            Pic = "",
            PubTime = pubTime,
            PagesInfo = pagesInfo,
            IsBangumi = false
        };
    }

    // 显式 fid 直接用；fid=0 时探测该用户的默认收藏夹
    private static async Task<string> ResolveFavIdAsync(Fav fav, AppConfig cfg, CancellationToken ct)
    {
        var favId = fav.Fid.ToString( );
        if (fav.Fid != 0)
        {
            return favId;
        }

        var mid = fav.Mid.ToString( );
        if (mid.Length == 0)
        {
            throw new ArgumentException($"收藏夹链接缺少 fid 与用户 id: {fav}", nameof(fav));
        }

        var favListApi = $"{BiliApi.FavFolderList}?up_mid={mid}";
        using var favJson = await GetJsonAsync(favListApi, cfg, ct);
        var folders = TryGetArray(GetApiData(favJson.RootElement, "收藏夹列表"), "list", out var list)
            ? EnumerateArrayOrEmpty(list)
            : [];
        return folders.FirstOrDefault( ) is { ValueKind: JsonValueKind.Object } folder
            ? folder.GetProperty("id").ToString( )
            : throw new InvalidOperationException($"用户 {mid} 没有可下载的收藏夹");
    }

    private static async Task<(List<JsonElement> Medias, string Title, string Intro, long PubTime)> CollectMediasAsync(string favId, AppConfig cfg, CancellationToken ct)
    {
        var api = $"{BiliApi.FavResourceList}?media_id={favId}&pn=1&ps={PageSize}&order=mtime&type=2&tid=0&platform=web";
        var json = await GetWebSourceAsync(api, cfg, null, ct);
        using var infoJson = JsonDocument.Parse(json);
        var data = GetApiData(infoJson.RootElement, "收藏夹信息");
        var title = data.GetProperty("info").GetProperty("title").GetString( )!;
        var intro = data.GetProperty("info").GetProperty("intro").GetString( )!;
        var pubTime = data.GetProperty("info").GetProperty("ctime").GetInt64( );
        // 空收藏夹时 B 站返回 "medias": null，EnumerateArray 会抛不可读的 InvalidOperationException
        // 用 EnumerateArrayOrEmpty 补空。media_count 预估容量，避免大收藏夹翻页时反复扩容拷贝
        var medias = EnumerateArrayOrEmpty(data.GetProperty("medias")).Select(m => m.Clone( )).ToList( );
        if (medias.Count == 0)
        {
            throw new InvalidOperationException($"收藏夹 {favId} 中没有可下载的视频");
        }

        var mediaCount = data.GetProperty("info").TryGetProperty("media_count", out var count) && count.ValueKind == JsonValueKind.Number
            ? count.GetInt32( )
            : 0;
        // 元素要在 doc 释放后继续使用，Clone 后收集
        var allMedias = new List<JsonElement>(Math.Max(mediaCount, medias.Count));
        allMedias.AddRange(medias);

        // 终止条件只看实际返回量，不看 media_count：该计数偏大时按页数翻会把空页一路请求到底
        //（请求洪泛），偏小时又会漏掉后面的收藏。不满一页即已到底，空页同样命中
        var page = 2;
        while (true)
        {
            if (page > MaxPages)
            {
                LogWarn($"收藏夹 {favId} 翻页达到上限（{MaxPages} 页），仅取前 {allMedias.Count} 条");
                break;
            }

            api = $"{BiliApi.FavResourceList}?media_id={favId}&pn={page}&ps={PageSize}&order=mtime&type=2&tid=0&platform=web";
            json = await GetWebSourceAsync(api, cfg, null, ct);
            // medias 元素要在循环外继续使用，Clone 后才能安全释放 jsonDoc
            using var jsonDoc = JsonDocument.Parse(json);
            var batch = EnumerateArrayOrEmpty(GetApiData(jsonDoc.RootElement, "收藏夹信息").GetProperty("medias")).Select(m => m.Clone( )).ToList( );
            allMedias.AddRange(batch);
            // 不满一页即已到底（空页同样命中），继续翻只会拿到空响应
            if (batch.Count < PageSize)
            {
                break;
            }

            page++;
        }

        return (allMedias, title, intro, pubTime);
    }

    // 单个视频被删 / 风控时跳过该视频，不让整个收藏夹因一条失败而中断（与 SpaceListFetcher 一致）
    private static async Task<ConcurrentDictionary<string, VInfo>> BackfillMultiPAsync(List<string> multiPIds, AppConfig cfg, CancellationToken ct)
    {
        var fetched = new ConcurrentDictionary<string, VInfo>(StringComparer.Ordinal);
        using var throttler = new SemaphoreSlim(BackfillConcurrency);
        var tasks = multiPIds.Select(async id =>
        {
            await throttler.WaitAsync(ct);
            try
            {
                fetched[id] = await NormalInfoFetcher.FetchAsync(long.Parse(id), cfg, ct);
            }
            catch (OperationCanceledException)
            {
                throw;   // Ctrl+C 不吞
            }
            catch (Exception ex)
            {
                LogWarn($"获取多 P 视频 {id} 详情失败，已跳过：{ex.Message}");
            }
            finally
            {
                throttler.Release( );
            }
        }).ToArray( );
        await Task.WhenAll(tasks);
        return fetched;
    }

    private static async Task<List<Page>> ExpandPagesAsync(List<JsonElement> medias, AppConfig cfg, CancellationToken ct)
    {
        var index = 1;
        List<Page> pagesInfo = [];
        // 去重用 HashSet<Page> 做 O(1) 判定（与 SpaceListFetcher 一致），避免 List.Contains 的 O(n²)
        var seenPages = new HashSet<Page>( );

        var multiPIds = medias
            .Where(m => m.GetProperty("attr").GetInt32( ) == 0 && m.GetProperty("page").GetInt32( ) > 1)
            .Select(m => m.GetProperty("id").ToString( ))
            .ToList( );
        var fetched = await BackfillMultiPAsync(multiPIds, cfg, ct);

        // 只处理视频类型 (可以直接在 query param 上指定 type=2)
        // 只处理未失效视频
        foreach (var m in medias)
        {
            if (m.GetProperty("attr").GetInt32( ) != 0)
            {
                continue;
            }

            var pageCount = m.GetProperty("page").GetInt32( );
            if (pageCount > 1 && fetched.TryGetValue(m.GetProperty("id").ToString( ), out var tmpInfo))
            {
                foreach (var item in tmpInfo.PagesInfo)
                {
                    var p = item.CopyWith(index++);
                    p.Title = m.GetProperty("title").ToString( ) + $"_P{item.Index}_{item.Title}";
                    p.Cover = tmpInfo.Pic;
                    p.Desc = m.GetProperty("intro").ToString( );
                    if (seenPages.Add(p))
                    {
                        pagesInfo.Add(p);
                    }
                }
            }
            else
            {
                if (pageCount > 1)
                {
                    LogWarn($"多 P 收藏视频 {m.GetProperty("id")} 详情抓取失败，仅下载首分 P");
                }

                Page p = new( )
                {
                    Index = index++,
                    Aid = m.GetProperty("id").ToString( ),
                    Cid = m.GetProperty("ugc").GetProperty("first_cid").ToString( ),
                    EpId = "",
                    Title = m.GetProperty("title").ToString( ),
                    Dur = m.GetProperty("duration").GetInt32( ),
                    Res = "",
                    PubTime = m.GetProperty("pubtime").GetInt64( ),
                    Cover = m.GetProperty("cover").ToString( ),
                    Desc = m.GetProperty("intro").ToString( ),
                    OwnerName = m.GetProperty("upper").GetProperty("name").ToString( ),
                    OwnerMid = m.GetProperty("upper").GetProperty("mid").ToString( ),
                };
                if (seenPages.Add(p))
                {
                    pagesInfo.Add(p);
                }
            }
        }

        return pagesInfo;
    }
}
