using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core.Entity;

using static BBDown.Core.Entity.Entity;
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
    public static async Task<VInfo> FetchAsync(string id, AppConfig cfg, CancellationToken ct = default)
    {
        var parts = id[IdPrefix.FavId.Length..].Split(':');
        var favId = parts[0];
        var mid = parts.Length > 1 ? parts[1] : "";
        //查找默认收藏夹
        if (favId.Length == 0)
        {
            if (mid.Length == 0)
            {
                throw new ArgumentException($"收藏夹链接缺少 fid 与用户 id: {id}", nameof(id));
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

        var api = $"{BiliApi.FavResourceList}?media_id={favId}&pn=1&ps={pageSize}&order=mtime&type=2&tid=0&platform=web";
        var json = await GetWebSourceAsync(api, cfg, null, ct);
        using var infoJson = JsonDocument.Parse(json);
        var data = GetApiData(infoJson.RootElement, "收藏夹信息");
        var totalCount = data.GetProperty("info").GetProperty("media_count").GetInt32( );
        var totalPage = (int) Math.Ceiling((double) totalCount / pageSize);
        var title = data.GetProperty("info").GetProperty("title").GetString( )!;
        var intro = data.GetProperty("info").GetProperty("intro").GetString( )!;
        var pubTime = data.GetProperty("info").GetProperty("ctime").GetInt64( );
        var userName = data.GetProperty("info").GetProperty("upper").GetProperty("name").ToString( );
        var medias = data.GetProperty("medias").EnumerateArray( ).ToList( );

        for (var page = 2; page <= totalPage; page++)
        {
            api = $"{BiliApi.FavResourceList}?media_id={favId}&pn={page}&ps={pageSize}&order=mtime&type=2&tid=0&platform=web";
            json = await GetWebSourceAsync(api, cfg, null, ct);
            // medias 元素要在循环外继续使用, Clone 后才能安全释放 jsonDoc
            using var jsonDoc = JsonDocument.Parse(json);
            medias.AddRange(EnumerateArrayOrEmpty(GetApiData(jsonDoc.RootElement, "收藏夹信息").GetProperty("medias")).Select(m => m.Clone( )));
        }

        foreach (var m in medias)
        {
            //只处理视频类型(可以直接在query param上指定type=2)
            //只处理未失效视频
            if (m.GetProperty("attr").GetInt32( ) != 0)
            {
                continue;
            }

            var pageCount = m.GetProperty("page").GetInt32( );
            if (pageCount > 1)
            {
                var tmpInfo = await NormalInfoFetcher.FetchAsync(m.GetProperty("id").ToString( ), cfg, ct);
                foreach (var item in tmpInfo.PagesInfo)
                {
                    var p = item.CopyWith(index++);
                    p.title = m.GetProperty("title").ToString( ) + $"_P{item.index}_{item.title}";
                    p.cover = tmpInfo.Pic;
                    p.desc = m.GetProperty("intro").ToString( );
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
                    index = index++,
                    aid = m.GetProperty("id").ToString( ),
                    cid = m.GetProperty("ugc").GetProperty("first_cid").ToString( ),
                    epid = "",
                    title = m.GetProperty("title").ToString( ),
                    dur = m.GetProperty("duration").GetInt32( ),
                    res = "",
                    pubTime = m.GetProperty("pubtime").GetInt64( ),
                    cover = m.GetProperty("cover").ToString( ),
                    desc = m.GetProperty("intro").ToString( ),
                    ownerName = m.GetProperty("upper").GetProperty("name").ToString( ),
                    ownerMid = m.GetProperty("upper").GetProperty("mid").ToString( ),
                };
                if (!pagesInfo.Contains(p))
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
