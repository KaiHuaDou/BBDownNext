using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;

using BBDown.Core.Entity;

using static BBDown.Core.Entity.Entity;
using static BBDown.Core.Util.HTTPUtil;

namespace BBDown.Core.Fetcher;

public static partial class NormalInfoFetcher
{
    public static async Task<VInfo> FetchAsync(string id, AppConfig cfg)
    {
        var api = $"https://api.bilibili.com/x/web-interface/view?aid={id}";
        var json = await GetWebSourceAsync(api, cfg);
        using var infoJson = JsonDocument.Parse(json);
        var data = infoJson.RootElement.GetProperty("data");
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
        List<Page> pagesInfo = [];
        var pages = data.GetProperty("pages").EnumerateArray( ).ToList( );
        foreach (var page in pages)
        {
            Page p = new( )
            {
                index = page.GetProperty("page").GetInt32( ),
                aid = id,
                cid = page.GetProperty("cid").ToString( ),
                epid = "",
                title = page.GetProperty("part").ToString( ).Trim( ),
                dur = page.GetProperty("duration").GetInt32( ),
                res = page.GetProperty("dimension").GetProperty("width").ToString( ) + "x" + page.GetProperty("dimension").GetProperty("height").ToString( ),
                pubTime = pubTime, //分p视频没有发布时间
                cover = "",
                desc = "",
                ownerName = ownerName,
                ownerMid = ownerMid,
            };
            pagesInfo.Add(p);
        }

        if (isSteinGate == 1) // 互动视频获取分P信息
        {
            var playerSoApi = $"https://api.bilibili.com/x/player.so?bvid={bvid}&id=cid:{cid}";
            var playerSoText = await GetWebSourceAsync(playerSoApi, cfg);
            var playerSoXml = new XmlDocument( );
            playerSoXml.LoadXml($"<root>{playerSoText}</root>");

            var interactionNode = playerSoXml.SelectSingleNode("//interaction");

            if (interactionNode is { InnerText.Length: > 0 })
            {
                var graphVersion = JsonDocument.Parse(interactionNode.InnerText).RootElement
                    .GetProperty("graph_version").GetInt64( );
                var edgeInfoApi = $"https://api.bilibili.com/x/stein/edgeinfo_v2?graph_version={graphVersion}&bvid={bvid}";
                var edgeInfoJson = await GetWebSourceAsync(edgeInfoApi, cfg);
                var edgeInfoData = JsonDocument.Parse(edgeInfoJson).RootElement.GetProperty("data");
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
                            index = index++,
                            aid = id,
                            cid = page.GetProperty("cid").ToString( ),
                            epid = "",
                            title = page.GetProperty("option").ToString( ).Trim( ),
                            dur = 0,
                            res = "",
                            pubTime = pubTime, //分p视频没有发布时间
                            cover = "",
                            desc = "",
                            ownerName = ownerName,
                            ownerMid = ownerMid,
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

        try
        {
            if (data.GetProperty("redirect_url").ToString( ).Contains("bangumi"))
            {
                bangumi = true;
                var epId = EpIdRegex( ).Match(data.GetProperty("redirect_url").ToString( )).Groups[1].Value;
                //番剧内容通常不会有分P，如果有分P则不需要epId参数
                if (pages.Count == 1)
                {
                    pagesInfo.ForEach(p => p.epid = epId);
                }
            }
        }
        catch { }

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