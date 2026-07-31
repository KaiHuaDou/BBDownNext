using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core.Entity;

using static BBDown.Core.Entity.Entity;
using static BBDown.Core.Util.HTTPUtil;
using static BBDown.Core.Util.JsonUtil;

namespace BBDown.Core.Fetcher;

/// <summary>
/// 合集解析
/// https://space.bilibili.com/23630128/channel/collectiondetail?sid=2045
/// https://www.bilibili.com/medialist/play/23630128?business=space_collection&business_id=2045 (无法从该链接打开合集)
/// </summary>
public static class MediaListFetcher
{
    public static async Task<VInfo> FetchAsync(string id, AppConfig cfg, CancellationToken ct = default)
    {
        var bizId = id[10..];
        try
        {
            return await FetchListAsync(bizId, 8, false, "合集", cfg, ct);
        }
        catch (InvalidOperationException listEx)
        {
            // 合集被删除、设为私密或无权访问时 data 为 null; 也可能是"系列"被误识别为合集
            try
            {
                return await SeriesListFetcher.FetchByBizIdAsync(bizId, cfg, ct);
            }
            catch (Exception seriesEx)
            {
                throw new InvalidOperationException($"{listEx.Message}; 按系列解析同样失败: {seriesEx.Message}", listEx);
            }
        }
    }

    // 合集(type=8)与系列(type=5)共用同一套 medialist 接口, 仅 type 与排序方向不同
    internal static async Task<VInfo> FetchListAsync(string bizId, int type, bool descOrder, string label, AppConfig cfg, CancellationToken ct = default)
    {
        var api = $"{BiliApi.MediaListInfo}?type={type}&biz_id={bizId}&tid=0";
        using var infoJson = JsonDocument.Parse(await GetWebSourceAsync(api, cfg, null, ct));
        var data = infoJson.RootElement.GetProperty("data");
        if (data.ValueKind != JsonValueKind.Object)
        {
            var (code, message) = ReadApiError(infoJson.RootElement);
            throw new InvalidOperationException($"获取{label}信息失败(code={code}): {message}");
        }

        var listTitle = data.GetProperty("title").GetString( )!;
        var intro = data.GetProperty("intro").GetString( )!;
        var pubTime = data.GetProperty("ctime").GetInt64( );

        List<Page> pagesInfo = [];
        var hasMore = true;
        var oid = "";
        var index = 1;
        while (hasMore)
        {
            var listApi = $"{BiliApi.MediaListResource}?type={type}&oid={oid}&otype=2&biz_id={bizId}&bvid=&with_current=true&mobi_app=web&ps=20&direction=false&sort_field=1&tid=0&desc={(descOrder ? "true" : "false")}";
            using var listJson = JsonDocument.Parse(await GetWebSourceAsync(listApi, cfg, null, ct));
            var listData = listJson.RootElement.GetProperty("data");
            if (listData.ValueKind != JsonValueKind.Object)
            {
                var (code, message) = ReadApiError(listJson.RootElement);
                throw new InvalidOperationException($"获取{label}视频列表失败(code={code}): {message}");
            }

            hasMore = listData.GetProperty("has_more").GetBoolean( );
            foreach (var m in listData.GetProperty("media_list").EnumerateArray( ))
            {
                // 只处理未失效的视频条目（与收藏夹解析逻辑保持一致）
                if (m.TryGetProperty("attr", out var attrElem) && attrElem.GetInt32( ) != 0)
                {
                    continue;
                }

                var pageCount = m.GetProperty("page").GetInt32( );
                var desc = m.GetProperty("intro").GetString( )!;
                var ownerName = m.GetProperty("upper").GetProperty("name").ToString( );
                var ownerMid = m.GetProperty("upper").GetProperty("mid").ToString( );
                foreach (var page in m.GetProperty("pages").EnumerateArray( ))
                {
                    Page p = new( )
                    {
                        index = index++,
                        aid = m.GetProperty("id").ToString( ),
                        cid = page.GetProperty("id").ToString( ),
                        epid = "",
                        title = pageCount == 1 ? m.GetProperty("title").ToString( ) : $"{m.GetProperty("title")}_P{page.GetProperty("page")}_{page.GetProperty("title")}", //单P使用外层标题 多P则拼接内层子标题
                        dur = page.GetProperty("duration").GetInt32( ),
                        res = ReadDimension(page),
                        pubTime = m.GetProperty("pubtime").GetInt64( ),
                        cover = m.GetProperty("cover").ToString( ),
                        desc = desc,
                        ownerName = ownerName,
                        ownerMid = ownerMid,
                    };
                    if (!pagesInfo.Contains(p))
                    {
                        pagesInfo.Add(p);
                    }
                    else
                    {
                        index--;
                    }
                }

                oid = m.GetProperty("id").ToString( );
            }
        }

        return new VInfo
        {
            Title = listTitle.Trim( ),
            Desc = intro.Trim( ),
            Pic = "",
            PubTime = pubTime,
            PagesInfo = pagesInfo,
            IsBangumi = false
        };
    }
}
