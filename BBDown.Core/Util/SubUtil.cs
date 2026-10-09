using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core.Entity;
using BBDown.Core.Protobuf;

using Google.Protobuf;

using static BBDown.Core.Logger;
using static BBDown.Core.Util.HTTPUtil;

namespace BBDown.Core.Util;

public static partial class SubUtil
{
    // 表内 region/script 子标签大小写并不统一（en-US 全大写、zh-Hans 首字母大写）
    // 与其在查表前做大小写归一，不如直接用大小写不敏感的字典
    public static (string Code, string Name) GetSubtitleCode(string key)
    {
        return SubtitleCodes.TryGetValue(key, out var value) ? value : ("und", "Undetermined");
    }

    #region 字幕接口

    // 任一环节抛异常或返回空 URL 都视为该接口不可用，由调用方回退到下一个候选
    private static async Task<List<Subtitle>?> TryFetchAsync(Func<Task<List<Subtitle>>> fetch)
    {
        try
        {
            return FilterUsable(await fetch( ));
        }
        catch (Exception ex)
        {
            // 网络故障与"该源确实没有字幕"在这里被压平成同一个结果，至少让 debug 日志能区分
            LogDebug("字幕候选接口不可用: {0}", ex.Message);
            return null;
        }
    }

    // view 接口的 AI 字幕只有 lan 没有下载地址：view 响应里 lan 以 "ai-" 开头时 subtitle_url 恒为空串
    // （bilibili-API-collect/docs/video/info.md 的 view 响应样例，lan=ai-zh 对应 subtitle_url=""），
    // 属正常数据而非接口故障，逐条过滤即可；全部无效才整表回退
    internal static List<Subtitle>? FilterUsable(List<Subtitle> subtitles)
    {
        var valid = subtitles.Where(s => !string.IsNullOrEmpty(s.Url)).ToList( );
        return valid.Count == 0 ? null : valid;
    }

    internal static List<Subtitle> ReadSubtitles(JsonElement array, string lanKey, string urlKey, string pathPrefix, bool intl)
    {
        return [.. array.EnumerateArray( ).Select(sub =>
        {
            // lan 来自响应体（镜像站 / --insecure 下由对端控制），且会被拼进写入路径
            // 故在产生处一次净化：下游的混流内嵌与产物命名一律使用净化后的值
            var lan = FileNameUtil.GetValidFileName(sub.GetProperty(lanKey).ToString( ));
            var url = sub.GetProperty(urlKey).ToString( ).Replace("\\\\/", "/");
            // 国际版只有 json 接口给的是可转 srt 的结构，其余是 ass 成品
            var ext = !intl || url.Contains(".json") ? ".srt" : ".ass";
            return new Subtitle { Lan = lan, Url = url, Path = $"{pathPrefix}.{lan}{ext}" };
        })];
    }

    #endregion

    public static async Task<List<Subtitle>> GetSubtitlesAsync(string aid, string cid, string epId, int index, bool intl, AppConfig cfg, CancellationToken ct = default)
    {
        var pathPrefix = $"{aid}/{aid}.{cid}";
        var intlWebHost = cfg.EpHost == BiliApi.MainHost ? BiliApi.IntlWebHost : cfg.EpHost;
        var intlAppHost = cfg.Host == BiliApi.MainHost ? BiliApi.IntlAppHost : cfg.Host;
        var accessKey = cfg.Token.Length != 0 ? $"&access_key={cfg.Token}" : "";

        // 候选接口按优先级排列，第一个成功返回的结果生效
        Func<Task<List<Subtitle>>>[] candidates = intl
            ?
            [
                ( ) => FromJsonAsync($"https://{intlWebHost}{BiliApi.IntlSubtitleWebPath}?episode_id={epId}",
                    root => root.GetProperty("data").GetProperty("subtitles"), "lang_key", "url", pathPrefix, intl, cfg, ct),
                ( ) => FromJsonAsync($"https://{intlAppHost}{BiliApi.IntlSeasonAppPath}?ep_id={epId}&platform=android&s_locale=zh_SG{accessKey}",
                    root => root.GetProperty("result").GetProperty("modules")[0].GetProperty("data").GetProperty("episodes")[index - 1].GetProperty("subtitles"),
                    "key", "url", pathPrefix, intl, cfg, ct),
            ]
            : cfg.Cookie.Length == 0
                // 未登录只有 APP 端能拿到字幕
                ? [( ) => FromAppAsync(aid, cid, pathPrefix, cfg, ct)]
                :
                [
                    // wbi 接口未签名会被服务端拒绝
                    ( ) => FromJsonAsync($"{BiliApi.PlayerWbiV2}?{SignUtil.WbiSignNow($"aid={aid}&cid={cid}", cfg)}",
                        root => root.GetProperty("data").GetProperty("subtitle").GetProperty("subtitles"), "lan", "subtitle_url", pathPrefix, intl, cfg, ct),
                    ( ) => FromJsonAsync($"{BiliApi.View}?aid={aid}&cid={cid}",
                        root => root.GetProperty("data").GetProperty("subtitle").GetProperty("list"), "lan", "subtitle_url", pathPrefix, intl, cfg, ct),
                    ( ) => FromAppAsync(aid, cid, pathPrefix, cfg, ct),
                ];

        foreach (var candidate in candidates)
        {
            if (await TryFetchAsync(candidate) is not { } subtitles)
            {
                continue;
            }

            foreach (var item in subtitles.Where(s => s.Url.StartsWith("//")))
            {
                item.Url = "https:" + item.Url;
            }

            return subtitles;
        }

        return [];
    }

    private static async Task<List<Subtitle>> FromJsonAsync(string api, Func<JsonElement, JsonElement> locate, string lanKey, string urlKey, string pathPrefix, bool intl, AppConfig cfg, CancellationToken ct)
    {
        using var json = JsonDocument.Parse(await GetWebSourceAsync(api, cfg, null, ct));
        return ReadSubtitles(locate(json.RootElement), lanKey, urlKey, pathPrefix, intl);
    }

    private static async Task<List<Subtitle>> FromAppAsync(string aid, string cid, string pathPrefix, AppConfig cfg, CancellationToken ct)
    {
        var payload = GrpcUtil.PackMessage(new DmViewReq
        {
            Pid = Convert.ToInt64(aid),
            Oid = Convert.ToInt64(cid),
            Type = 1,
            Spmid = "main.ugc-video-detail.0.0",
        }.ToByteArray( ));
        var headers = AppHelper.GetHeader(cfg, BiliApi.GrpcDmView);
        var body = GrpcUtil.ReadMessage(await GetPostResponseAsync(BiliApi.GrpcDmView, payload, headers, ct));
        var reply = new MessageParser<DmViewReply>(( ) => new DmViewReply( )).ParseFrom(body);
        return reply.Subtitle?.Subtitles?
            .Select(s =>
            {
                var lan = FileNameUtil.GetValidFileName(s.Lan);
                return new Subtitle { Lan = lan, Url = s.SubtitleUrl, Path = $"{pathPrefix}.{lan}.srt" };
            })
            .ToList( ) ?? [];
    }

    // CA1054: url 保持 string —— 该方法被 BBDown 主项目直接调用（传入 Subtitle.Url 字符串）
    public static async Task SaveSubtitleAsync(string url, string path, AppConfig cfg, CancellationToken ct = default)
    {
        if (path.EndsWith(".srt"))
        {
            await File.WriteAllTextAsync(path, ConvertSubFromJson(await GetWebSourceAsync(url, cfg, null, ct)), Encoding.UTF8, ct);
        }
        else
        {
            await File.WriteAllTextAsync(path, await GetWebSourceAsync(url, cfg, null, ct), Encoding.UTF8, ct);
        }
    }

    internal static string ConvertSubFromJson(string jsonString)
    {
        StringBuilder lines = new( );
        using var json = JsonDocument.Parse(jsonString);
        // 字幕 body 缺失（异常 schema）时降级为空字幕，避免整条字幕保存失败
        if (!json.RootElement.TryGetProperty("body", out var body))
        {
            return "";
        }

        var sub = body.EnumerateArray( ).ToList( );
        for (var i = 0; i < sub.Count; i++)
        {
            var line = sub[i];
            lines.AppendLine((i + 1).ToString( ));
            if (line.TryGetProperty("from", out var from))
            {
                lines.AppendLine($"{FormatTime(from.GetDouble( ))} --> {FormatTime(line.GetProperty("to").GetDouble( ))}");
            }
            else
            {
                lines.AppendLine($"{FormatTime(0.0)} --> {FormatTime(line.GetProperty("to").GetDouble( ))}");
            }
            //有的没有内容
            if (line.TryGetProperty("content", out var content))
            {
                lines.AppendLine(EscapeSrtText(content.ToString( )));
            }

            lines.AppendLine( );
        }

        return lines.ToString( );
    }

    private static string FormatTime(double sec) //64.13
    {
        return TimeSpan.FromSeconds(sec).ToString(@"hh\:mm\:ss\,fff");
    }

    // SRT 无原生转义；正文内的换行会破坏条目边界（宽松解析器按空行分段）
    // 恰为 "-->" 又会被误判为时间轴分隔符，故换行替换为空格、箭头替换为视觉等价的长横
    private static string EscapeSrtText(string text)
    {
        return text.Replace("\r", "").Replace("\n", " ").Replace("-->", "—>");
    }
}
