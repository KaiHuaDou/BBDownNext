using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

using BBDown.Core.Entity;
using BBDown.Core.Util;

using static BBDown.Core.Util.HTTPUtil;
using static BBDown.Core.Util.JsonUtil;

namespace BBDown.Core.Fetcher;

public static partial class NormalInfoFetcher
{
    public static async Task<VInfo> FetchAsync(long aid, AppConfig cfg, CancellationToken ct = default)
    {
        var api = $"{BiliApi.ViewWbi}?{SignUtil.WbiSignNow($"aid={aid}", cfg)}";
        var json = await GetWebSourceAsync(api, cfg, null, ct);
        using var infoJson = JsonDocument.Parse(json);
        var data = GetApiData(infoJson.RootElement, "视频信息");
        var title = data.GetProperty("title").ToString( );
        var desc = data.GetProperty("desc").ToString( );
        var pic = data.GetProperty("pic").ToString( );
        var owner = data.GetProperty("owner");
        var ownerMid = owner.GetProperty("mid").ToString( );
        var ownerName = owner.GetProperty("name").ToString( );
        var pubTime = data.GetProperty("pubdate").GetInt64( );
        var bangumi = false;
        var bvid = data.GetProperty("bvid").ToString( );
        var cid = data.GetProperty("cid").GetInt64( );

        // 互动视频 1:是 0:否
        var isSteinGate = data.GetProperty("rights").GetProperty("is_stein_gate").GetInt16( );

        // 分p信息
        var aidStr = aid.ToString( );
        List<Page> pagesInfo = [];
        var pages = data.GetProperty("pages").EnumerateArray( ).ToList( );
        foreach (var page in pages)
        {
            Page p = new( )
            {
                Index = page.GetProperty("page").GetInt32( ),
                Aid = aidStr,
                Cid = page.GetProperty("cid").ToString( ),
                EpId = "",
                Title = page.GetProperty("part").ToString( ).Trim( ),
                Dur = page.GetProperty("duration").GetInt32( ),
                Res = ReadDimension(page),
                PubTime = pubTime, //分p视频没有发布时间
                Cover = "",
                Desc = "",
                OwnerName = ownerName,
                OwnerMid = ownerMid,
            };
            pagesInfo.Add(p);
        }

        if (isSteinGate == 1) // 互动视频获取分P信息
        {
            var playerSoApi = $"{BiliApi.PlayerSo}?bvid={bvid}&id=cid:{cid}";
            var playerSoText = await GetWebSourceAsync(playerSoApi, cfg, null, ct);
            var playerSoXml = new XmlDocument( );
            playerSoXml.LoadXml($"<root>{playerSoText}</root>");

            var interactionNode = playerSoXml.SelectSingleNode("//interaction");

            if (interactionNode is { InnerText.Length: > 0 })
            {
                using var interactionDoc = JsonDocument.Parse(interactionNode.InnerText);
                var graphVersion = interactionDoc.RootElement.GetProperty("graph_version").GetInt64( );
                var edgeInfoApi = $"{BiliApi.EdgeInfo}?graph_version={graphVersion}&bvid={bvid}";
                using var edgeInfoDoc = JsonDocument.Parse(await GetWebSourceAsync(edgeInfoApi, cfg, null, ct));
                var edgeInfoData = GetApiData(edgeInfoDoc.RootElement, "互动视频分P信息");
                var questions = edgeInfoData.GetProperty("edges").GetProperty("questions").EnumerateArray( )
                    .ToList( );
                var index = 2; // 互动视频分P索引从2开始
                foreach (var question in questions)
                {
                    var choices = question.GetProperty("choices").EnumerateArray( ).ToList( );
                    foreach (var page in choices)
                    {
                        Page p = new( )
                        {
                            Index = index++,
                            Aid = aidStr,
                            Cid = page.GetProperty("cid").ToString( ),
                            EpId = "",
                            Title = page.GetProperty("option").ToString( ).Trim( ),
                            Dur = 0,
                            Res = "",
                            PubTime = pubTime, //分p视频没有发布时间
                            Cover = "",
                            Desc = "",
                            OwnerName = ownerName,
                            OwnerMid = ownerMid,
                        };
                        pagesInfo.Add(p);
                    }
                }
            }
            else
            {
                throw new InvalidOperationException("互动视频获取分P信息失败");
            }
        }

        //稿件被重定向到番剧播放页时，该稿件按番剧处理
        if (data.TryGetProperty("redirect_url", out var redirectUrl) && redirectUrl.ValueKind == JsonValueKind.String
            && IsBangumiPlayPage(redirectUrl.GetString( ) ?? ""))
        {
            bangumi = true;
            //番剧内容通常不会有分P，如果有分P则不需要epId参数
            if (pages.Count == 1 && EpIdRegex( ).Match(redirectUrl.GetString( )!) is { Success: true } epMatch)
            {
                pagesInfo.ForEach(p => p.EpId = epMatch.Groups[1].Value);
            }
        }

        var info = new VInfo
        {
            Title = title.Trim( ),
            Desc = desc.Trim( ),
            Pic = pic,
            PubTime = pubTime,
            PagesInfo = pagesInfo,
            IsBangumi = bangumi,
            IsSteinGate = isSteinGate == 1
        };

        return info;
    }

    [GeneratedRegex("ep(\\d+)")]
    private static partial Regex EpIdRegex( );
}