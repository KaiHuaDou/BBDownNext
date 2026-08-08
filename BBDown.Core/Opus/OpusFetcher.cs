using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using static BBDown.Core.Logger;
using static BBDown.Core.Util.HTTPUtil;
using static BBDown.Core.Util.JsonUtil;

namespace BBDown.Core.Opus;

/// <summary>
/// opus / cv 的抓取与解析入口。按职责拆为三个 partial 文件：
/// <see cref="OpusFetcher"/>（网络编排与判定）、OpusFetcher.Parse.cs（文档级解析）、OpusFetcher.Paragraph.cs（段落与节点解析）。
/// </summary>
public static partial class OpusFetcher
{
    // htmlNewStyle 会让 opus/detail 直出专栏正文与 rid_str；不带该 feature 时只回退 fallback.Id
    private const string OpusFeatures =
        "itemOpusStyle,opusBigCover,onlyfansVote,decorationCard,forwardListHidden,ugcDelete,onlyfansQaCard,htmlNewStyle";

    public static async Task<OpusDocument> FetchAsync(OpusTarget target, AppConfig cfg, CancellationToken ct = default)
    {
        try
        {
            var cvId = target.CvId;
            OpusParagraph? topAlbum = null;
            if (string.IsNullOrEmpty(cvId))
            {
                var detailUrl = $"{BiliApi.OpusDetail}?timezone_offset=-480&id={target.OpusId}&features={OpusFeatures}";
                using var detailDoc = JsonDocument.Parse(await GetWebSourceAsync(detailUrl, cfg, null, ct));
                var data = GetApiData(detailDoc.RootElement, "专栏信息");
                topAlbum = ParseTopAlbum(data);
                cvId = TryGetCvId(data) ?? "";
                if (string.IsNullOrEmpty(cvId))
                {
                    LogWarn("该 opus 不是专栏文章，将按图文动态导出。");
                    var doc = ParseOpusDetail(data, $"{BiliApi.OpusPage}/{target.OpusId}");
                    PrependTopAlbum(doc, topAlbum);
                    return doc;
                }
            }

            var viewUrl = $"{BiliApi.ArticleView}?id={cvId}";
            using var viewDoc = JsonDocument.Parse(await GetWebSourceAsync(viewUrl, cfg, null, ct));
            var viewData = GetApiData(viewDoc.RootElement, "专栏正文");
            var article = ParseArticleView(viewData, cvId, $"{BiliApi.ReadPage}/cv{cvId}");
            PrependTopAlbum(article, topAlbum);
            return article;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw Friendly(ex);
        }
    }

    /// <summary>
    /// 从 opus/detail 的返回里取出 cv id：优先 <c>data.fallback.Id</c>（type==2 为专栏）；
    /// 其次仅当 <c>data.item.type</c> 为 1（专栏动态）时，<c>data.item.basic.rid_str</c> 才是 cv 号。
    /// 纯动态（type==0）的 rid_str 不是 cv，返回 null 走图文动态导出。
    /// </summary>
    internal static string? TryGetCvId(JsonElement data)
    {
        if (data.TryGetProperty("fallback", out var fallback) && fallback.ValueKind == JsonValueKind.Object)
        {
            if (fallback.TryGetProperty("type", out var ftype) && ftype.ValueKind == JsonValueKind.Number && ftype.GetInt32( ) == 2
                && fallback.TryGetProperty("id", out var fid))
            {
                return fid.ValueKind == JsonValueKind.Number ? fid.GetRawText( ) : (fid.GetString( ) ?? "");
            }
        }

        if (data.TryGetProperty("item", out var item) && item.ValueKind == JsonValueKind.Object
            && item.TryGetProperty("type", out var itype) && itype.ValueKind == JsonValueKind.Number && itype.GetInt32( ) == 1
            && item.TryGetProperty("basic", out var basic) && basic.TryGetProperty("rid_str", out var rid) && rid.ValueKind == JsonValueKind.String)
        {
            return rid.GetString( );
        }

        return null;
    }

    // module_top.display.album.pics 是顶部相册（动态翻页相册 / 专栏顶部图），渲染时置于正文最前
    private static OpusParagraph? ParseTopAlbum(JsonElement data)
    {
        if (!data.TryGetProperty("item", out var item) || item.ValueKind != JsonValueKind.Object
            || !item.TryGetProperty("modules", out var modules) || modules.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var images = new List<OpusImage>( );
        foreach (var module in modules.EnumerateArray( ))
        {
            var moduleType = module.TryGetProperty("module_type", out var mt) ? (mt.GetString( ) ?? "") : "";
            if (moduleType != "MODULE_TYPE_TOP"
                || !module.TryGetProperty("module_top", out var top) || top.ValueKind != JsonValueKind.Object
                || !top.TryGetProperty("display", out var display) || display.ValueKind != JsonValueKind.Object
                || !display.TryGetProperty("album", out var album) || album.ValueKind != JsonValueKind.Object
                || !album.TryGetProperty("pics", out var pics) || pics.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var pic in pics.EnumerateArray( ))
            {
                var url = pic.TryGetProperty("url", out var u) ? (u.GetString( ) ?? "") : "";
                if (url.Length != 0)
                {
                    images.Add(new OpusImage { Url = url });
                }
            }

            break;
        }

        return images.Count > 0 ? new OpusParagraph { Kind = OpusParagraphKind.Image, Images = images } : null;
    }

    private static void PrependTopAlbum(OpusDocument doc, OpusParagraph? topAlbum)
    {
        if (topAlbum is not null)
        {
            doc.Paragraphs.Insert(0, topAlbum);
        }
    }

    private static Exception Friendly(Exception ex)
    {
        if (ex is InvalidOperationException ioe && ioe.Message.Contains("code="))
        {
            var m = OpusRegexes.CodeInMessage( ).Match(ioe.Message);
            if (m.Success)
            {
                var hint = m.Groups[1].Value switch
                {
                    "-352" => "触发风控(-352)，请稍后重试或使用 BBDown login 登录后再试",
                    "-412" => "请求被拦截(-412)，通常是 buvid3 缺失或请求过于频繁，稍等几分钟后重试",
                    "-404" => "专栏不存在或已被删除",
                    "-403" or "62002" => "专栏已被设为私密或无权访问，请先登录",
                    _ => "",
                };
                if (!string.IsNullOrEmpty(hint))
                {
                    return new InvalidOperationException(hint);
                }
            }
        }

        return ex;
    }
}
