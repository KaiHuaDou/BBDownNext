using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core.Entity;

using static BBDown.Core.ResourceId;
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
    public static Task<VInfo> FetchAsync(MediaList list, AppConfig cfg, CancellationToken ct = default)
    {
        return FetchListAsync(list.BizId, 8, false, "合集", cfg, ct);
    }

    // 合集(type=8)与系列(type=5)共用同一套 medialist 接口, 仅 type 与排序方向不同
    internal static async Task<VInfo> FetchListAsync(long bizId, int type, bool descOrder, string label, AppConfig cfg, CancellationToken ct = default)
    {
        var api = $"{BiliApi.MediaListInfo}?type={type}&biz_id={bizId}&tid=0";
        using var infoJson = JsonDocument.Parse(await GetWebSourceAsync(api, cfg, null, ct));
        var data = GetApiData(infoJson.RootElement, $"{label}信息");
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
            var listData = GetApiData(listJson.RootElement, $"{label}视频列表");
            hasMore = listData.GetProperty("has_more").GetBoolean( );
            var got = 0;
            foreach (var m in listData.GetProperty("media_list").EnumerateArray( ))
            {
                // 只处理未失效的视频条目（与收藏夹解析逻辑保持一致）
                if (m.TryGetProperty("attr", out var attrElem) && attrElem.GetInt32( ) != 0)
                {
                    continue;
                }

                got++;
                var pageCount = m.GetProperty("page").GetInt32( );
                var desc = m.GetProperty("intro").GetString( )!;
                var ownerName = m.GetProperty("upper").GetProperty("name").ToString( );
                var ownerMid = m.GetProperty("upper").GetProperty("mid").ToString( );
                foreach (var page in m.GetProperty("pages").EnumerateArray( ))
                {
                    Page p = new( )
                    {
                        Index = index++,
                        Aid = m.GetProperty("id").ToString( ),
                        Cid = page.GetProperty("id").ToString( ),
                        EpId = "",
                        Title = pageCount == 1 ? m.GetProperty("title").ToString( ) : $"{m.GetProperty("title")}_P{page.GetProperty("page")}_{page.GetProperty("title")}", //单P使用外层标题 多P则拼接内层子标题
                        Dur = page.GetProperty("duration").GetInt32( ),
                        Res = ReadDimension(page),
                        PubTime = m.GetProperty("pubtime").GetInt64( ),
                        Cover = m.GetProperty("cover").ToString( ),
                        Desc = desc,
                        OwnerName = ownerName,
                        OwnerMid = ownerMid,
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

            // 整页条目均失效时 oid 不前进、has_more 恒 true，不中断会死循环
            if (got == 0)
            {
                break;
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