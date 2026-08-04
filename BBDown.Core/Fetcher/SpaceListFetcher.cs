using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core.Entity;

using static BBDown.Core.Entity.Entity;
using static BBDown.Core.Logger;
using static BBDown.Core.Util.HTTPUtil;
using static BBDown.Core.Util.JsonUtil;

namespace BBDown.Core.Fetcher;

/// <summary>
/// UP 主空间全部投稿解析。
/// 输入经 InputResolver 统一转为 spaceMid:{mid} 前缀后在此处理，支持三种入口：
/// 空间 URL（https://space.bilibili.com/{mid} 及 /upload/video、/video?tid=0 等子路径）、裸 mid、space{mid}。
/// 接口 x/space/wbi/arc/search 只返回 aid，不含 cid 与分 P，故对每条稿件并发回填一次 wbi/view 取 cid 并展开多 P，
/// 摊平为 VInfo.PagesInfo；下游下载链路（PageQueue / PageSelect / SavePath / ArchiveLog）自动按「列表」处理。
/// </summary>
public static class SpaceListFetcher
{
    private const int PageSize = 30;
    private const int BackfillConcurrency = 8;   // 与 FavListFetcher 一致；大 UP 触发 -412 风控时改这一处即可
    private const int MaxPages = 1000;           // 兜底，防止接口 count 异常导致死循环

    // 只从 vlist 抽取需要的标量，避免长期持有整棵 JsonDocument
    private readonly record struct SpaceItem(
        string Aid, string Title, string Desc, string Pic, long Created,
        string Author, string Mid, bool IsLesson, bool IsLivePlayback, bool IsCharging);

    public static async Task<VInfo> FetchAsync(string id, AppConfig cfg, CancellationToken ct = default)
    {
        var mid = id[IdPrefix.SpaceMid.Length..];
        if (mid.Length == 0 || !mid.All(char.IsDigit))
        {
            throw new ArgumentException($"无效的 UP 主 mid: {id}", nameof(id));
        }

        var items = await CollectItemsAsync(mid, cfg, ct);
        if (items.Count == 0)
        {
            throw new InvalidOperationException($"UP 主 {mid} 没有可下载的公开投稿视频");
        }

        // UP 名：优先取 mid 与目标一致的条目（排除合作视频里的他人署名），否则退回首条作者
        var firstMatch = items.FirstOrDefault(i => i.Mid == mid);
        var upName = !string.IsNullOrWhiteSpace(firstMatch.Author) ? firstMatch.Author : items[0].Author;
        if (string.IsNullOrWhiteSpace(upName))
        {
            upName = $"UP主{mid}";
        }

        // 课堂视频（cheese）预剔除：wbi/view 必失败，省掉注定失败的请求
        var lessons = items.Where(i => i.IsLesson).ToList( );
        foreach (var lesson in lessons)
        {
            LogWarn($"跳过不可下载稿件 aid={lesson.Aid}（课堂视频，请用 cheese 链接单独下载）：{lesson.Title}");
        }

        var targets = items.Where(i => !i.IsLesson).ToList( );

        Log($"共 {items.Count} 条投稿，正在获取分 P 详情...");
        var (fetched, failures) = await BackfillAsync(targets, cfg, ct);

        List<Page> pagesInfo = [];
        // Page 以 (aid,cid,epid) 判等，HashSet 做 O(1) 去重（优于 FavListFetcher 的 List.Contains O(n²)）
        var seen = new HashSet<Page>( );
        var index = 1;
        foreach (var item in targets)
        {
            if (!fetched.TryGetValue(item.Aid, out var tmp) || tmp.PagesInfo.Count == 0)
            {
                var reason = failures.TryGetValue(item.Aid, out var msg) ? msg : "无可用分 P";
                LogWarn($"跳过不可下载稿件 aid={item.Aid}（{reason}）：{item.Title}");
                continue;
            }

            // 稿件已重定向为番剧：playurl 按 UGC 分支走会失败，跳过并提示用 ep 链接
            if (tmp.IsBangumi)
            {
                LogWarn($"跳过不可下载稿件 aid={item.Aid}（已转为番剧，请用 ep 链接下载）：{item.Title}");
                continue;
            }

            var multi = tmp.PagesInfo.Count > 1;
            foreach (var page in tmp.PagesInfo)
            {
                var p = page.CopyWith(index);
                p.title = multi ? $"{item.Title}_P{page.index}_{page.title}" : item.Title;
                p.cover = item.Pic;
                p.desc = item.Desc;
                // ownerName / ownerMid 由 CopyWith 保留 NormalInfoFetcher 从 view.owner 解析的真实作者
                if (seen.Add(p))
                {
                    pagesInfo.Add(p);
                    index++;
                }
            }
        }

        if (pagesInfo.Count == 0)
        {
            throw new InvalidOperationException($"UP 主 {upName}({mid}) 的 {items.Count} 条投稿均不可下载");
        }

        return new VInfo
        {
            Title = upName.Trim( ),
            Desc = "",
            Pic = "",                       // 必须为空串，逐分 P 才会用各自的 Page.cover
            PubTime = items[0].Created,     // pubdate 倒序 → 首条即最新稿件
            PagesInfo = pagesInfo,
            IsBangumi = false
        };
    }

    // 分页拉取 vlist，内置风控守卫与越界页兜底
    private static async Task<List<SpaceItem>> CollectItemsAsync(string mid, AppConfig cfg, CancellationToken ct)
    {
        List<SpaceItem> items = [];
        var pn = 1;
        var total = int.MaxValue;
        while (pn <= MaxPages && items.Count < total)
        {
            var wts = DateTimeOffset.Now.ToUnixTimeSeconds( ).ToString( );
            var api = $"{BiliApi.SpaceArcSearch}?{Parser.WbiSign($"mid={mid}&order=pubdate&pn={pn}&ps={PageSize}&tid=0&wts={wts}", cfg)}";
            using var doc = JsonDocument.Parse(await GetWebSourceAsync(api, cfg, null, ct));
            var data = GetApiData(doc.RootElement, "UP 主投稿列表");   // code!=0（-400/-412/-352）在此抛带 code 错误

            // 风控：code=0 但 data 只回 v_voucher / is_risk=true，list.vlist 缺失或为空
            if (!HasObject(data, "list") || !TryGetArray(data.GetProperty("list"), "vlist", out var vlist))
            {
                throw new InvalidOperationException(IsRisk(data)
                    ? "获取 UP 主投稿列表被风控拦截（需要验证），请稍后重试或补充已登录的 Cookie"
                    : "获取 UP 主投稿列表失败：接口未返回 vlist");
            }

            var got = 0;
            foreach (var v in vlist.EnumerateArray( ))
            {
                items.Add(ReadItem(v));
                got++;
            }

            if (data.TryGetProperty("page", out var page) && page.ValueKind == JsonValueKind.Object
                && page.TryGetProperty("count", out var count) && count.ValueKind == JsonValueKind.Number)
            {
                total = count.GetInt32( );
            }

            if (got == 0)
            {
                break;   // 空页兜底：count 虚高 / 越界页
            }

            pn++;
        }

        return items;
    }

    // 并发回填每条稿件的 wbi/view，取的 cid 与分 P 列表；失败统一降级为「跳过」
    private static async Task<(ConcurrentDictionary<string, VInfo> Fetched, ConcurrentDictionary<string, string> Failures)>
        BackfillAsync(List<SpaceItem> targets, AppConfig cfg, CancellationToken ct)
    {
        var fetched = new ConcurrentDictionary<string, VInfo>(StringComparer.Ordinal);
        var failures = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);
        var done = 0;
        using var throttler = new SemaphoreSlim(BackfillConcurrency);
        var tasks = targets.Select(async item =>
        {
            await throttler.WaitAsync(ct);
            try
            {
                fetched[item.Aid] = await NormalInfoFetcher.FetchAsync(item.Aid, cfg, ct);
            }
            catch (OperationCanceledException)
            {
                throw;   // Ctrl+C 不吞
            }
            catch (Exception ex)
            {
                // 直播回放 / 充电专属 / 已删除 / 大会员限定等在此统一降级为跳过
                failures[item.Aid] = ex.Message;
            }
            finally
            {
                var n = Interlocked.Increment(ref done);
                if (n % 50 == 0)
                {
                    Log($"已获取 {n}/{targets.Count} 条投稿详情...");
                }

                throttler.Release( );
            }
        }).ToArray( );
        await Task.WhenAll(tasks);
        return (fetched, failures);
    }

    private static bool IsRisk(JsonElement data) =>
        (data.TryGetProperty("is_risk", out var risk) && risk.ValueKind == JsonValueKind.True)
        || (data.TryGetProperty("gaia_res_type", out var gaia) && gaia.ValueKind == JsonValueKind.Number && gaia.GetInt32( ) != 0);

    // is_lesson_video / is_live_playback 是 num，is_charging_arc 是 bool → 统一容错读取
    private static bool ReadFlag(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v)
        && v.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.Number => v.GetInt32( ) != 0,
            _ => false
        };

    private static string ReadStr(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.String or JsonValueKind.Number
            ? v.ToString( )
            : "";

    private static SpaceItem ReadItem(JsonElement v) => new(
        Aid: ReadStr(v, "aid"),
        Title: ReadStr(v, "title").Trim( ),
        Desc: ReadStr(v, "description").Trim( ),
        Pic: ReadStr(v, "pic"),
        Created: v.TryGetProperty("created", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetInt64( ) : 0,
        Author: ReadStr(v, "author"),
        Mid: ReadStr(v, "mid"),
        IsLesson: ReadFlag(v, "is_lesson_video"),
        IsLivePlayback: ReadFlag(v, "is_live_playback"),
        IsCharging: ReadFlag(v, "is_charging_arc"));
}
