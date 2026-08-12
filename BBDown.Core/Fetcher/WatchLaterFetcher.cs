using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core.Entity;

using static BBDown.Core.Logger;
using static BBDown.Core.Util.HTTPUtil;
using static BBDown.Core.Util.JsonUtil;

namespace BBDown.Core.Fetcher;

/// <summary>
/// 稍后再看列表解析
/// https://www.bilibili.com/watchlater/
/// </summary>
public static class WatchLaterFetcher
{
    public static async Task<VInfo> FetchAsync(AppConfig cfg, CancellationToken ct = default)
    {
        var json = await GetWebSourceAsync(BiliApi.ToviewList, cfg, null, ct);
        using var jDoc = JsonDocument.Parse(json);

        // toview 是私有接口，未登录返回 -101，先给出可操作的提示再走统一错误处理
        if (ReadApiError(jDoc.RootElement).Code == -101)
        {
            throw new InvalidOperationException("获取稍后再看列表需要登录，请通过 --cookie 或配置文件提供 SESSDATA");
        }

        var data = GetApiData(jDoc.RootElement, "稍后再看列表");
        var medias = EnumerateArrayOrEmpty(data.GetProperty("list")).ToList( );
        if (medias.Count == 0)
        {
            throw new InvalidOperationException("稍后再看列表为空");
        }

        // 多P视频此前逐个串行发 view 拿分P列表，改为限并发并行拉取
        var multiPIds = medias
            .Where(m => m.GetProperty("videos").GetInt32( ) > 1)
            .Select(m => m.GetProperty("aid").ToString( ))
            .ToList( );
        var fetched = new ConcurrentDictionary<string, VInfo>(StringComparer.Ordinal);
        using (var throttler = new SemaphoreSlim(8))
        {
            var tasks = multiPIds.Select(async aid =>
            {
                await throttler.WaitAsync(ct);
                try
                {
                    fetched[aid] = await NormalInfoFetcher.FetchAsync(long.Parse(aid), cfg, ct);
                }
                catch (OperationCanceledException)
                {
                    throw;   // Ctrl+C 不吞
                }
                catch (Exception ex)
                {
                    // 单个视频被删/风控时跳过该视频，不让整个列表因一条失败而中断（与 SpaceListFetcher 一致）
                    LogWarn($"获取多 P 视频 {aid} 详情失败，已跳过：{ex.Message}");
                }
                finally
                {
                    throttler.Release( );
                }
            }).ToArray( );
            await Task.WhenAll(tasks);
        }

        List<Page> pagesInfo = [];
        var index = 1;
        foreach (var m in medias)
        {
            var aid = m.GetProperty("aid").ToString( );
            if (m.GetProperty("videos").GetInt32( ) > 1 && fetched.TryGetValue(aid, out var tmpInfo))
            {
                foreach (var item in tmpInfo.PagesInfo)
                {
                    var p = item.CopyWith(index++);
                    p.Title = m.GetProperty("title").ToString( ) + $"_P{item.Index}_{item.Title}";
                    p.Cover = tmpInfo.Pic;
                    p.Desc = m.GetProperty("desc").ToString( );
                    if (!pagesInfo.Contains(p))
                    {
                        pagesInfo.Add(p);
                    }
                }
            }
            else
            {
                Page p = new( )
                {
                    Index = index++,
                    Aid = aid,
                    Cid = m.GetProperty("cid").ToString( ),
                    EpId = "",
                    Title = m.GetProperty("title").ToString( ),
                    Dur = m.GetProperty("duration").GetInt32( ),
                    Res = "",
                    PubTime = m.GetProperty("pubdate").GetInt64( ),
                    Cover = m.GetProperty("pic").ToString( ),
                    Desc = m.GetProperty("desc").ToString( ),
                    OwnerName = m.GetProperty("owner").GetProperty("name").ToString( ),
                    OwnerMid = m.GetProperty("owner").GetProperty("mid").ToString( ),
                };
                if (!pagesInfo.Contains(p))
                {
                    pagesInfo.Add(p);
                }
            }
        }

        var info = new VInfo
        {
            Title = "稍后再看",
            Desc = "",
            Pic = "",
            PubTime = 0,
            PagesInfo = pagesInfo,
            IsBangumi = false
        };

        return info;
    }
}
