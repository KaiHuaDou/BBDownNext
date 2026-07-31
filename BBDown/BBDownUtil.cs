using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

using static BBDown.Core.Entity.Entity;
using static BBDown.Core.Logger;
using static BBDown.Core.Util.HTTPUtil;

namespace BBDown;

internal static partial class Utils
{
    public static async Task<string> GetAvIdAsync(string input, Core.AppConfig cfg)
    {
        var avid = input.StartsWith("http")
            ? await ResolveUrlAsync(input, cfg)
            : await ResolveShorthandAsync(input, cfg);
        return await FixAvidAsync(avid);
    }

    private static async Task<string> ResolveUrlAsync(string input, Core.AppConfig cfg)
    {
        if (input.Contains("b23.tv"))
        {
            var tmp = await GetWebLocationAsync(input);
            if (tmp == input) throw new InvalidOperationException("无限重定向");
            input = tmp;
        }

        if (input.Contains("video/av"))
            return AvRegex( ).Match(input).Groups[1].Value;
        if (input.ToLower( ).Contains("video/bv"))
            return GetAidByBV(BVRegex( ).Match(input).Groups[1].Value);
        if (input.Contains("/cheese/"))
            return await ResolveCheeseAsync(input, cfg);
        if (input.Contains("/ep"))
            return $"ep:{EpRegex( ).Match(input).Groups[1].Value}";
        if (input.Contains("/ss"))
            return $"ep:{await GetEpIdByBangumiSSIdAsync(SsRegex( ).Match(input).Groups[1].Value, cfg)}";
        if (input.Contains("/medialist/") && input.Contains("business_id=") && input.Contains("business=space_collection")) // 列表类型是合集
            return $"listBizId:{GetQueryString("business_id", input)}";
        if (input.Contains("/medialist/") && input.Contains("business_id=") && input.Contains("business=space_series")) // 列表类型是系列
            return $"seriesBizId:{GetQueryString("business_id", input)}";
        if (input.Contains("/channel/collectiondetail?sid="))
            return $"listBizId:{GetQueryString("sid", input)}";
        if (input.Contains("/channel/seriesdetail?sid="))
            return $"seriesBizId:{GetQueryString("sid", input)}";
        if (input.Contains("/space.bilibili.com/") && input.Contains("/lists/"))
            return ResolveSpaceList(input);
        if (input.Contains("/space.bilibili.com/") && input.Contains("/favlist"))
            return $"favId:{GetQueryString("fid", input)}:{UidRegex( ).Match(input).Groups[1].Value}";
        if (input.Contains("/space.bilibili.com/"))
            throw new NotSupportedException("目前下载器不支持下载用户空间的全部投稿视频，请逐条传入具体视频链接进行下载。");
        if (input.Contains("ep_id="))
            return $"ep:{GetQueryString("ep_id", input)}";
        if (GlobalEpRegex( ).Match(input) is { Success: true } globalEp)
            return $"ep:{globalEp.Groups[1].Value}";
        if (BangumiMdRegex( ).Match(input) is { Success: true } md)
            return $"ep:{await GetEpIdByMDAsync(md.Groups[1].Value, cfg)}";
        return $"ep:{await ScrapeFirstEpIdAsync(input, cfg)}";
    }

    private static async Task<string> ResolveShorthandAsync(string input, Core.AppConfig cfg)
    {
        if (input.ToLower( ).StartsWith("bv"))
            return GetAidByBV(input[3..]);
        if (input.ToLower( ).StartsWith("av"))
            return input.ToLower( )[2..];
        if (input.StartsWith("cheese/")) // ^cheese/(ep|ss)\d+ 格式
            return await ResolveCheeseAsync(input, cfg);
        if (input.StartsWith("ep"))
            return $"ep:{input[2..]}";
        if (input.StartsWith("ss"))
            return $"ep:{await GetEpIdByBangumiSSIdAsync(input[2..], cfg)}";
        if (input.StartsWith("md"))
            return $"ep:{await GetEpIdByMDAsync(MdRegex( ).Match(input).Groups[1].Value, cfg)}";
        throw new ArgumentException("输入有误", nameof(input));
    }

    private static async Task<string> ResolveCheeseAsync(string input, Core.AppConfig cfg)
    {
        var epId = "";
        if (input.Contains("/ep"))
            epId = EpRegex( ).Match(input).Groups[1].Value;
        else if (input.Contains("/ss"))
            epId = await GetEpidBySSIdAsync(SsRegex( ).Match(input).Groups[1].Value, cfg);
        return $"cheese:{epId}";
    }

    // 新版个人空间合集/系列链接：
    //   合集: https://space.bilibili.com/392959666/lists/1560264?type=season
    //   系列: https://space.bilibili.com/392959666/lists/1560264?type=series
    private static string ResolveSpaceList(string input)
    {
        // path 最后一个 / 后到 ? 前即为 sid
        var path = input.Split('?', '#')[0];
        var sid = path[(path.LastIndexOf('/') + 1)..];
        var type = GetQueryString("type", input).ToLower( );
        // 未知类型按合集处理，至少不会识别失败
        return type == "series" ? $"seriesBizId:{sid}" : $"listBizId:{sid}";
    }

    private static async Task<string> ScrapeFirstEpIdAsync(string input, Core.AppConfig cfg)
    {
        var web = await GetWebSourceAsync(input, cfg);
        var json = StateRegex( ).Match(web).Groups[1].Value;
        using var jDoc = JsonDocument.Parse(json);
        return jDoc.RootElement.GetProperty("epList").EnumerateArray( ).First( ).GetProperty("id").ToString( );
    }

    public static string FormatFileSize(double fileSize)
    {
        return fileSize switch
        {
            < 0 => throw new ArgumentOutOfRangeException(nameof(fileSize)),
            >= 1024 * 1024 * 1024 => $"{fileSize / (1024 * 1024 * 1024):########0.00} GB",
            >= 1024 * 1024 => $"{fileSize / (1024 * 1024):####0.00} MB",
            >= 1024 => $"{fileSize / 1024:####0.00} KB",
            _ => $"{fileSize} bytes"
        };
    }

    public static string FormatTime(int time, bool absolute = false)
    {
        var ts = TimeSpan.FromSeconds(time);
        var totalHours = (int) ts.TotalHours;
        var minutes = ts.Minutes;
        var seconds = ts.Seconds;

        if (absolute)
        {
            return $"{totalHours:D2}:{minutes:D2}:{seconds:D2}";
        }

        return totalHours == 0 ? $"{minutes:D2}m{seconds:D2}s" : $"{totalHours}h{minutes:D2}m{seconds:D2}s";
    }

    /// <summary>
    /// 通过avid检测是否为版权内容, 如果是的话返回ep:xx格式
    /// </summary>
    /// <param name="avid"></param>
    /// <returns></returns>
    private static async Task<string> FixAvidAsync(string avid)
    {
        if (!avid.All(char.IsDigit))
            return avid;
        var api = $"https://www.bilibili.com/video/av{avid}/";
        var location = await GetWebLocationAsync(api);
        return location.Contains("/ep") ? $"ep:{EpRegex( ).Match(location).Groups[1].Value}" : avid;
    }

    private static string GetAidByBV(string bv)
    {
        // 能在本地就在本地
        return Core.Util.BilibiliBvConverter.Decode(bv).ToString( );
    }

    private static async Task<string> GetEpidBySSIdAsync(string ssid, Core.AppConfig cfg)
    {
        var api = $"https://api.bilibili.com/pugv/view/web/season?season_id={ssid}";
        var json = await GetWebSourceAsync(api, cfg);
        using var jDoc = JsonDocument.Parse(json);
        var epId = jDoc.RootElement.GetProperty("data").GetProperty("episodes").EnumerateArray( ).First( ).GetProperty("id").ToString( );
        return epId;
    }

    private static async Task<string> GetEpIdByBangumiSSIdAsync(string ssId, Core.AppConfig cfg)
    {
        var api = $"https://{cfg.EpHost}/pgc/view/web/season?season_id={ssId}";
        var json = await GetWebSourceAsync(api, cfg);
        using var jDoc = JsonDocument.Parse(json);
        var epId = jDoc.RootElement.GetProperty("result").GetProperty("episodes").EnumerateArray( ).First( ).GetProperty("id").ToString( );
        return epId;
    }

    private static async Task<string> GetEpIdByMDAsync(string mdId, Core.AppConfig cfg)
    {
        var api = $"https://api.bilibili.com/pgc/review/user?media_id={mdId}";
        var json = await GetWebSourceAsync(api, cfg);
        using var jDoc = JsonDocument.Parse(json);
        var epId = jDoc.RootElement.GetProperty("result").GetProperty("media").GetProperty("new_ep").GetProperty("id").ToString( );
        return epId;
    }

    /// <summary>
    /// 输入一堆已存在的文件, 合并到新文件
    /// </summary>
    /// <param name="files"></param>
    /// <param name="outputFilePath"></param>
    public static void CombineMultipleFilesIntoSingleFile(string[] files, string outputFilePath)
    {
        if (files.Length == 0) return;
        if (files.Length == 1)
        {
            FileInfo fi = new(files[0]);
            fi.MoveTo(outputFilePath, true);
            return;
        }

        if (!Directory.Exists(Path.GetDirectoryName(outputFilePath)))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputFilePath)!);
        }

        var inputFilePaths = files;
        using var outputStream = File.Create(outputFilePath);
        foreach (var inputFilePath in inputFilePaths)
        {
            if (inputFilePath.Length == 0)
                continue;
            using var inputStream = File.OpenRead(inputFilePath);
            // Buffer size can be passed as the second argument.
            inputStream.CopyTo(outputStream);
        }
    }

    /// <summary>
    /// 寻找指定目录下指定后缀的文件的详细路径 如".txt"
    /// </summary>
    /// <param name="dir"></param>
    /// <param name="ext"></param>
    /// <returns></returns>
    public static string[] GetFiles(string dir, string ext)
    {
        List<string> al = [];
        StringBuilder sb = new( );
        DirectoryInfo d = new(dir);
        foreach (var fi in d.GetFiles( ))
        {
            if (fi.Extension.ToUpper( ) == ext.ToUpper( ))
            {
                al.Add(fi.FullName);
            }
        }

        var res = al.ToArray( );
        Array.Sort(res); //排序
        return res;
    }

    public static string GetValidFileName(string input, string re = "_", bool filterSlash = false)
    {
        return Core.Util.FileNameUtil.GetValidFileName(input, re, filterSlash);
    }

    /// <summary>
    /// 获取url字符串参数, 返回参数值字符串
    /// </summary>
    /// <param name="name">参数名称</param>
    /// <param name="url">url字符串</param>
    /// <returns></returns>
    public static string GetQueryString(string name, string url)
    {
        var re = QueryRegex( );
        var mc = re.Matches(url);
        foreach (var m in mc.Cast<Match>( ))
        {
            if (m.Result("$2").Equals(name))
            {
                return m.Result("$3");
            }
        }

        return "";
    }

    public static string GetSign(string parms)
    {
        var toEncode = parms + "59b43e04ad6965f34319062b478f83dd";
        return string.Concat(MD5.HashData(Encoding.UTF8.GetBytes(toEncode)).Select(i => i.ToString("x2")));
    }

    public static string GetTimeStamp(bool bflag)
    {
        var ts = DateTimeOffset.Now;
        return (bflag ? ts.ToUnixTimeSeconds( ) : ts.ToUnixTimeMilliseconds( )).ToString( );
    }

    //https://stackoverflow.com/questions/1344221/how-can-i-generate-random-alphanumeric-strings
    private static readonly Random random = new( );
    public static string GetRandomString(int length)
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz_0123456789";
        return new string(Enumerable.Repeat(chars, length)
            .Select(s => s[random.Next(s.Length)]).ToArray( ));
    }

    //https://stackoverflow.com/a/45088333
    public static string ToQueryString(NameValueCollection nameValueCollection)
    {
        var httpValueCollection = HttpUtility.ParseQueryString(string.Empty);
        httpValueCollection.Add(nameValueCollection);
        return httpValueCollection.ToString( )!;
    }

    public static Dictionary<string, string> ToDictionary(this NameValueCollection nameValueCollection)
    {
        Dictionary<string, string> dict = [];
        foreach (var key in nameValueCollection.AllKeys)
        {
            dict[key!] = nameValueCollection[key]!;
        }

        return dict;
    }

    public static NameValueCollection GetTVLoginParms( )
    {
        NameValueCollection sb = [];
        var now = DateTime.Now;
        var deviceId = GetRandomString(20);
        var buvid = GetRandomString(37);
        var fingerprint = $"{now:yyyyMMddHHmmssfff}{GetRandomString(45)}";
        sb.Add("appkey", "4409e2ce8ffd12b8");
        sb.Add("auth_code", "");
        sb.Add("bili_local_id", deviceId);
        sb.Add("build", "102801");
        sb.Add("buvid", buvid);
        sb.Add("channel", "master");
        sb.Add("device", "OnePlus");
        sb.Add("device_id", deviceId);
        sb.Add("device_name", "OnePlus7TPro");
        sb.Add("device_platform", "Android10OnePlusHD1910");
        sb.Add("fingerprint", fingerprint);
        sb.Add("guid", buvid);
        sb.Add("local_fingerprint", fingerprint);
        sb.Add("local_id", buvid);
        sb.Add("mobi_app", "android_tv_yst");
        sb.Add("networkstate", "wifi");
        sb.Add("platform", "android");
        sb.Add("sys_ver", "29");
        sb.Add("ts", GetTimeStamp(true));
        sb.Add("sign", GetSign(ToQueryString(sb)));

        return sb;
    }

    /// <summary>
    /// 检测ffmpeg是否识别杜比视界
    /// </summary>
    /// <returns></returns>
    public static bool CheckFFmpegDOVI( )
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = BBDownMuxer.FFMPEG,
                    Arguments = "-version",
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                }
            };
            process.Start( );
            var info = process.StandardOutput.ReadToEnd( ) + Environment.NewLine + process.StandardError.ReadToEnd( );
            process.WaitForExit( );
            var match = LibavutilRegex( ).Match(info);
            if (!match.Success) return false;
            if (Convert.ToInt32(match.Groups[1].Value) is (57 and >= 17) or > 57)
            {
                return true;
            }
        }
        catch (Exception)
        {
        }

        return false;
    }

    /// <summary>
    /// 获取章节信息
    /// </summary>
    /// <param name="cid"></param>
    /// <param name="aid"></param>
    /// <returns></returns>
    public static async Task<List<ViewPoint>> FetchPointsAsync(string cid, string aid, Core.AppConfig cfg)
    {
        List<ViewPoint> points = [];
        try
        {
            var api = $"https://api.bilibili.com/x/player/wbi/v2?cid={cid}&aid={aid}";
            var json = await GetWebSourceAsync(api, cfg);
            using var infoJson = JsonDocument.Parse(json);
            if (infoJson.RootElement.GetProperty("data").TryGetProperty("view_points", out var vPoint))
            {
                foreach (var point in vPoint.EnumerateArray( ))
                {
                    points.Add(new ViewPoint( )
                    {
                        title = point.GetProperty("content").GetString( )!,
                        start = int.Parse(point.GetProperty("from").ToString( )),
                        end = int.Parse(point.GetProperty("to").ToString( ))
                    });
                }
            }
        }
        catch (Exception) { }

        return points;
    }

    /// <summary>
    /// 生成metadata文件, 用于ffmpeg混流章节信息
    /// </summary>
    /// <param name="points"></param>
    /// <returns></returns>
    public static string GetFFmpegMetaString(List<ViewPoint> points)
    {
        StringBuilder sb = new( );
        sb.AppendLine(";FFMETADATA");
        foreach (var p in points)
        {
            var time = 1000; //固定 1000
            sb.AppendLine("[CHAPTER]");
            sb.AppendLine($"TIMEBASE=1/{time}");
            sb.AppendLine($"START={p.start * time}");
            sb.AppendLine($"END={p.end * time}");
            sb.AppendLine($"title={p.title}");
            sb.AppendLine( );
        }

        return sb.ToString( );
    }

    /// <summary>
    /// 生成metadata文件, 用于mp4box混流章节信息
    /// </summary>
    /// <param name="points"></param>
    /// <returns></returns>
    public static string GetMp4boxMetaString(List<ViewPoint> points)
    {
        StringBuilder sb = new( );
        foreach (var p in points)
        {
            sb.AppendLine($"{FormatTime(p.start, true)} {p.title}");
        }

        return sb.ToString( );
    }

    public static string? FindExecutable(string name)
    {
        var fileExt = OperatingSystem.IsWindows( ) ? ".exe" : "";
        var searchPath = new[] { Environment.CurrentDirectory, Program.APP_DIR };
        var envPath = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator) ?? [];
        return searchPath.Concat(envPath).Select(p => Path.Combine(p, name + fileExt)).FirstOrDefault(File.Exists);
    }

    public static string RSubString(string sub)
    {
        sub = sub[(sub.LastIndexOf('/') + 1)..];
        return sub[..sub.LastIndexOf('.')];
    }

    internal static string GetMixinKey(string orig)
    {
        byte[] mixinKeyEncTab =
        [
            46, 47, 18, 2, 53, 8, 23, 32, 15, 50, 10, 31, 58, 3, 45, 35,
            27, 43, 5, 49, 33, 9, 42, 19, 29, 28, 14, 39, 12, 38, 41, 13
        ];

        var tmp = new StringBuilder(32);
        foreach (var index in mixinKeyEncTab)
        {
            tmp.Append(orig[index]);
        }

        return tmp.ToString( );
    }

    public static async Task<(AccountInfo Info, string Wbi)> ProbeAccountAsync(Core.AppConfig cfg)
    {
        try
        {
            var api = "https://api.bilibili.com/x/web-interface/nav";
            var source = await GetWebSourceAsync(api, cfg);
            var json = JsonDocument.Parse(source).RootElement;
            var info = ParseNav(json.GetProperty("data"));
            var wbi_img = json.GetProperty("data").GetProperty("wbi_img");
            var wbi = GetMixinKey(RSubString(wbi_img.GetProperty("img_url").GetString( )!) + RSubString(wbi_img.GetProperty("sub_url").GetString( )!));
            LogDebug("wbi: {0}", wbi);
            return (info, wbi);
        }
        catch (Exception)
        {
            return (new AccountInfo(false, "", 0, false, ""), "");
        }
    }

    /// <summary>
    /// 从 nav 接口的 data 节点解析账号信息（昵称/等级/大会员等）。
    /// 各字段均做了缺失保护，避免接口结构变动导致整体解析失败。
    /// </summary>
    internal static AccountInfo ParseNav(JsonElement data)
    {
        var isLogin = data.TryGetProperty("isLogin", out var il) && il.GetBoolean( );
        var uname = data.TryGetProperty("uname", out var u) ? (u.GetString( ) ?? "") : "";
        var level = data.TryGetProperty("level_info", out var li) && li.TryGetProperty("current_level", out var cl) ? cl.GetInt32( ) : 0;
        var isVip = false;
        var vipLabel = "";
        if (data.TryGetProperty("vip", out var vip))
        {
            isVip = vip.TryGetProperty("vipStatus", out var vs) && vs.GetInt32( ) == 1;
            if (vip.TryGetProperty("label", out var label) && label.TryGetProperty("text", out var lt))
                vipLabel = lt.GetString( ) ?? "";
        }
        return new AccountInfo(isLogin, uname, level, isVip, vipLabel);
    }

    [GeneratedRegex("av(\\d+)")]
    private static partial Regex AvRegex( );
    [GeneratedRegex("[Bb][Vv]1(\\w+)")]
    private static partial Regex BVRegex( );
    [GeneratedRegex("/ep(\\d+)")]
    private static partial Regex EpRegex( );
    [GeneratedRegex("/ss(\\d+)")]
    private static partial Regex SsRegex( );
    [GeneratedRegex(@"space\.bilibili\.com/(\d+)")]
    private static partial Regex UidRegex( );
    [GeneratedRegex(@"\.bilibili\.tv\/\w+\/play\/\d+\/(\d+)")]
    private static partial Regex GlobalEpRegex( );
    [GeneratedRegex("bangumi/media/(md\\d+)")]
    private static partial Regex BangumiMdRegex( );
    [GeneratedRegex(@"window.__INITIAL_STATE__=([\s\S].*?);\(function\(\)")]
    private static partial Regex StateRegex( );
    [GeneratedRegex("md(\\d+)")]
    private static partial Regex MdRegex( );
    [GeneratedRegex("(^|&)?(\\w+)=([^&]+)(&|$)?", RegexOptions.Compiled)]
    private static partial Regex QueryRegex( );
    [GeneratedRegex("libavutil\\s+(\\d+)\\. +(\\d+)\\.")]
    private static partial Regex LibavutilRegex( );
}