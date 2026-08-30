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
    public static async Task<VInfo> FetchAsync(Fav fav, AppConfig cfg, CancellationToken ct = default)
    {
        var favId = fav.Fid.ToString( );
        var mid = fav.Mid.ToString( );
        //查找默认收藏夹
        if (fav.Fid == 0)
        {
            if (mid.Length == 0)
            {
                throw new ArgumentException($"收藏夹链接缺少 fid 与用户 id: {fav}", nameof(fav));
            }

            var favListApi = $"{BiliApi.FavFolderList}?up_mid={mid}";
            using var favJson = await GetJsonAsync(favListApi, cfg, ct);
            var folders = TryGetArray(GetApiData(favJson.RootElement, "收藏夹列表"), "list", out var list)
                ? EnumerateArrayOrEmpty(list)
                : [];
            favId = folders.FirstOrDefault( ) is { ValueKind: JsonValueKind.Object } folder
                ? folder.GetProperty("id").ToString( )
                : throw new InvalidOperationException($"用户 {mid} 没有可下载的收藏夹");
        }

        const int pageSize = 20;
        var index = 1;
        List<Page> pagesInfo = [];
        // 去重用 HashSet<Page> 做 O(1) 判定（与 SpaceListFetcher 一致），避免 List.Contains 的 O(n²)
        var seenPages = new HashSet<Page>( );

        var api = $"{BiliApi.FavResourceList}?media_id={favId}&pn=1&ps={pageSize}&order=mtime&type=2&tid=0&platform=web";
        var json = await GetWebSourceAsync(api, cfg, null, ct);
        using var infoJson = JsonDocument.Parse(json);
        var data = GetApiData(infoJson.RootElement, "收藏夹信息");
        var title = data.GetProperty("info").GetProperty("title").GetString( )!;
        var intro = data.GetProperty("info").GetProperty("intro").GetString( )!;
        var pubTime = data.GetProperty("info").GetProperty("ctime").GetInt64( );
        var userName = data.GetProperty("info").GetProperty("upper").GetProperty("name").ToString( );
        // 空收藏夹时 B 站返回 "medias": null，EnumerateArray 会抛不可读的 InvalidOperationException；
        // 用 EnumerateArrayOrEmpty 兜底，并在无媒体时给出可读提示（与下方 folder 缺失提示风格一致，§2.6）
        // 首页元素同样 Clone：与后续分页元素一致，不依赖外层 doc 存活到方法末尾
        var medias = EnumerateArrayOrEmpty(data.GetProperty("medias")).Select(m => m.Clone( )).ToList( );
        if (medias.Count == 0)
        {
            throw new InvalidOperationException($"收藏夹 {favId} 中没有可下载的视频");
        }

        // 终止条件只看实际返回量，不看 media_count：该计数偏大时按页数翻会把空页一路请求到底
        //（请求洪泛），偏小时又会漏掉后面的收藏。不满一页即已到底，空页同样命中
        var page = 2;
        while (true)
        {
            api = $"{BiliApi.FavResourceList}?media_id={favId}&pn={page}&ps={pageSize}&order=mtime&type=2&tid=0&platform=web";
            json = await GetWebSourceAsync(api, cfg, null, ct);
            // medias 元素要在循环外继续使用，Clone 后才能安全释放 jsonDoc
            using var jsonDoc = JsonDocument.Parse(json);
            var batch = EnumerateArrayOrEmpty(GetApiData(jsonDoc.RootElement, "收藏夹信息").GetProperty("medias")).Select(m => m.Clone( )).ToList( );
            medias.AddRange(batch);
            // 不满一页即已到底（空页同样命中），继续翻只会拿到空响应
            if (batch.Count < pageSize)
            {
                break;
            }

            page++;
        }

        // 多 P 视频此前逐个串行发 view 拿分 P 列表，N 个多 P = N 次串行 RTT；改为限并发并行拉取。
        var multiPIds = medias
            .Where(m => m.GetProperty("attr").GetInt32( ) == 0 && m.GetProperty("page").GetInt32( ) > 1)
            .Select(m => m.GetProperty("id").ToString( ))
            .ToList( );
        var fetched = new ConcurrentDictionary<string, VInfo>(StringComparer.Ordinal);
        using (var throttler = new SemaphoreSlim(8))
        {
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
                    // 单个视频被删/风控时跳过该视频，不让整个收藏夹因一条失败而中断（与 SpaceListFetcher 一致）
                    LogWarn($"获取多 P 视频 {id} 详情失败，已跳过：{ex.Message}");
                }
                finally
                {
                    throttler.Release( );
                }
            }).ToArray( );
            await Task.WhenAll(tasks);
        }

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

        var info = new VInfo
        {
            Title = title.Trim( ),
            Desc = intro.Trim( ),
            Pic = "",
            PubTime = pubTime,
            PagesInfo = pagesInfo,
            IsBangumi = false
        };

        return info;
    }
}
