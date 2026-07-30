using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core;
using BBDown.Core.Entity;
using BBDown.Core.Fetcher;
using BBDown.Core.Util;

using static BBDown.BBDownDownloadUtil;
using static BBDown.Core.Entity.Entity;
using static BBDown.Core.Logger;
using static BBDown.Core.Parser;
using static BBDown.Utils;

namespace BBDown;

internal partial class Program
{
    private const string BACKUP_HOST = "upos-sz-mirrorcoso1.bilivideo.com";
    public static string SinglePageDefaultSavePath { get; set; } = "<videoTitle>";
    public static string MultiPageDefaultSavePath { get; set; } = "<videoTitle>/[P<pageNumberWithZero>]<pageTitle>";

    public static readonly string APP_DIR = Path.GetDirectoryName(Environment.ProcessPath)!;

    private static string FormatTimeStamp(long ts, string format)
    {
        try
        {
            return ts == 0 ? "null" : DateTimeOffset.FromUnixTimeSeconds(ts).ToLocalTime( ).ToString(format);
        }
        catch (Exception ex)
        {
            LogError($"格式化日期出错: {ex.Message}");
            return ts.ToString( );
        }
    }

    [JsonSerializable(typeof(MyOption))]
    [JsonSerializable(typeof(ServeRequestOptions))]
    private partial class MyOptionJsonContext : JsonSerializerContext { }

    private static void Console_CancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        LogWarn("Force Exit...");
        try
        {
            Console.ResetColor( );
            Console.CursorVisible = true;
            if (!OperatingSystem.IsWindows( ))
                System.Diagnostics.Process.Start("stty", "echo");
        }
        catch { }

        Environment.Exit(0);
    }

    public static async Task<int> Main(params string[] args)
    {
        Console.CancelKeyPress += Console_CancelKeyPress;

        RootCommand rootCommand = CommandLineInvoker.GetRootCommand(RunApp);
        rootCommand.Description = "BBDown是一个免费且便捷高效的哔哩哔哩下载/解析软件.";
        rootCommand.TreatUnmatchedTokensAsErrors = false;

        Command loginCommand = new("login", "通过APP扫描二维码以登录您的WEB账号");
        loginCommand.SetAction(_ => Login.Web( ));
        rootCommand.Subcommands.Add(loginCommand);

        Command loginTVCommand = new("logintv", "通过APP扫描二维码以登录您的TV账号");
        loginTVCommand.SetAction(_ => Login.TV( ));
        rootCommand.Subcommands.Add(loginTVCommand);

        Command serverCommand = new("serve", "以服务器模式运行")
        {
            new Option<string>("--listen", "-l")
            {
                Description = "服务器监听地址"
            }
        };
        serverCommand.SetAction(result => StartServer(result.GetValue<string>("--listen")));
        rootCommand.Subcommands.Add(serverCommand);

        ParseResult rootResult = rootCommand.Parse(args, new ParserConfiguration( )
        {
            EnablePosixBundling = true,
        });

        // 显式抛出异常
        if (rootResult.Errors.Any( ))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine(rootResult.Errors[0].Message);
            Console.ResetColor( );
            Console.Error.WriteLine("请使用 BBDown --help 查看帮助");
            return 1;
        }

        var argsList = new List<string>( );

        foreach (SymbolResult item in rootResult.CommandResult.Children)
        {
            if (item is ArgumentResult a)
            {
                if (a.Tokens.Count > 0)
                {
                    argsList.Add(a.Tokens[0].Value);
                }
            }
            else if (item is OptionResult o)
            {
                argsList.Add($"--{o.Option.Name}");
                argsList.AddRange(o.Tokens.Select(t => t.Value));
            }
        }

        if (argsList.Contains("--debug"))
        {
            Config.DEBUG_LOG = true;
        }

        Console.BackgroundColor = ConsoleColor.DarkBlue;
        Console.ForegroundColor = ConsoleColor.White;
        Version ver = System.Reflection.Assembly.GetExecutingAssembly( ).GetName( ).Version!;
        Console.Write($"BBDown version {ver.Major}.{ver.Minor}.{ver.Build}, Bilibili Downloader.\r\n");
        Console.ResetColor( );
        Console.WriteLine( );

        //处理配置文件
        BBDownConfigParser.HandleConfig(argsList, rootCommand);

        return await rootResult.InvokeAsync(new InvocationConfiguration( ) { EnableDefaultExceptionHandler = false });
    }

    private static Task RunApp(MyOption myOption)
    {
        return DoWorkAsync(myOption);
    }

    private static void StartServer(string? listenUrl)
    {
        var defaultListenUrl = "http://0.0.0.0:23333";
        var server = new BBDownApiServer( );
        server.SetUpServer( );
        server.Run(string.IsNullOrEmpty(listenUrl) ? defaultListenUrl : listenUrl);
    }

    public static (Dictionary<string, byte> encodingPriority, Dictionary<string, int> dfnPriority, string firstEncoding,
        bool downloadDanmaku, BBDownDanmakuFormat[] downloadDanmakuFormats, string input, string savePathFormat, string lang, string aidOri, int delay)
        SetUpWork(MyOption myOption)
    {
        //处理废弃选项
        HandleDeprecatedOptions(myOption);

        //处理冲突选项
        HandleConflictingOptions(myOption);

        //寻找并设置所需的二进制文件路径
        FindBinaries(myOption);

        //切换工作目录
        ChangeWorkingDir(myOption);

        //解析优先级
        Dictionary<string, byte> encodingPriority = ParseEncodingPriority(myOption, out var firstEncoding);
        Dictionary<string, int> dfnPriority = ParseDfnPriority(myOption);

        //优先使用用户设置的UA
        HTTPUtil.UserAgent = string.IsNullOrEmpty(myOption.UserAgent) ? HTTPUtil.UserAgent : myOption.UserAgent;

        var downloadDanmaku = myOption.DownloadDanmaku || myOption.DanmakuOnly;
        BBDownDanmakuFormat[] downloadDanmakuFormats = ParseDownloadDanmakuFormats(myOption);

        var input = myOption.Url;
        var savePathFormat = myOption.FilePattern;
        var lang = myOption.Language;
        var aidOri = ""; //原始aid
        var delay = int.TryParse(myOption.DelayPerPage, out var delayValue) ? delayValue : 0;
        Config.DEBUG_LOG = myOption.Debug;

        LogDebug("AppDirectory: {0}", APP_DIR);
        LogDebug("运行参数：{0}", JsonSerializer.Serialize(myOption, MyOptionJsonContext.Default.MyOption));
        return (encodingPriority, dfnPriority, firstEncoding, downloadDanmaku, downloadDanmakuFormats, input, savePathFormat, lang, aidOri, delay);
    }

    public static async Task<(string fetchedAid, VInfo vInfo, string apiType, AppConfig cfg)> GetVideoInfoAsync(MyOption myOption, string aidOri, string input)
    {
        // 加载认证信息
        var (cookie, token) = LoadCredentials(myOption);

        var cfg = new AppConfig(cookie, token, myOption.Host, myOption.EpHost, myOption.TvHost, myOption.Area, "");

        // 检测是否登录了账号
        if (myOption is { UseIntlApi: false, UseTvApi: false } && cfg.Area == "")
        {
            Log("检测账号登录...");
            var (isLogin, wbi) = await CheckLogin(cfg);
            cfg = cfg with { Wbi = wbi };
            if (!isLogin)
            {
                LogWarn("你尚未登录B站账号, 解析可能受到限制");
            }
        }

        Log("获取aid...");
        aidOri = await GetAvIdAsync(input, cfg);
        Log($"获取aid结束: {aidOri}");

        if (string.IsNullOrEmpty(aidOri))
        {
            throw new Exception("输入有误");
        }

        Log("获取视频信息...");
        VInfo? vInfo = null;

        // 只输入 EP/SS 时优先按番剧查找，如果找不到则尝试按课程查找
        try
        {
            vInfo = await FetcherRegistry.FetchAsync(aidOri, cfg, myOption.UseIntlApi);
        }
        catch (KeyNotFoundException e)
        {
            if (e.Message != "Arg_KeyNotFound") throw; // 错误消息不符合预期，抛出异常
            if (aidOri.StartsWith("cheese:")) throw; // 已经按课程查找过，不再重复尝试

            LogWarn("未找到此 EP/SS 对应番剧信息, 正在尝试按课程查找。");

            aidOri = aidOri.Replace("ep", "cheese");
            Log("新的 aid: " + aidOri);

            if (string.IsNullOrEmpty(aidOri))
            {
                throw new Exception("输入有误");
            }

            Log("获取视频信息...");
            vInfo = await FetcherRegistry.FetchAsync(aidOri, cfg, myOption.UseIntlApi);
        }

        var title = vInfo.Title;
        var pubTime = vInfo.PubTime;
        LogColor("视频标题: " + title);
        if (pubTime != 0)
        {
            Log("发布时间: " + FormatTimeStamp(pubTime, "yyyy-MM-dd HH:mm:ss zzz"));
        }

        var bvid = vInfo.PagesInfo.FirstOrDefault( )?.bvid;
        if (!string.IsNullOrEmpty(bvid) && !myOption.UseIntlApi)
        {
            Log($"视频URL: https://www.bilibili.com/video/{bvid}/");
        }

        var mid = vInfo.PagesInfo.FirstOrDefault(p => !string.IsNullOrEmpty(p.ownerMid))?.ownerMid;
        if (!string.IsNullOrEmpty(mid))
        {
            Log($"UP主页: https://space.bilibili.com/{mid}");
        }

        if (vInfo.IsSteinGate && myOption.UseTvApi)
        {
            Log("视频为互动视频，暂时不支持tv下载，修改为默认下载");
            myOption.UseTvApi = false;
        }

        var apiType = myOption.UseTvApi ? "TV" : (myOption.UseAppApi ? "APP" : (myOption.UseIntlApi ? "INTL" : "WEB"));

        //打印分P信息
        List<Page> pagesInfo = vInfo.PagesInfo;
        var more = false;
        foreach (Page p in pagesInfo)
        {
            if (!myOption.ShowAll)
            {
                if (more && p.index != pagesInfo.Count) continue;
                if (!more && p.index > 5)
                {
                    Log("......");
                    more = true;
                    continue;
                }
            }

            Log($"P{p.index}: [{p.cid}] [{p.title}] [{FormatTime(p.dur)}]");
        }

        return (aidOri, vInfo, apiType, cfg);
    }

    public static async Task DownloadPagesAsync(MyOption myOption, VInfo vInfo, Dictionary<string, byte> encodingPriority, Dictionary<string, int> dfnPriority,
        string firstEncoding, bool downloadDanmaku, BBDownDanmakuFormat[] downloadDanmakuFormats, string input, string savePathFormat, string lang, string aidOri, int delay, string apiType, AppConfig cfg, DownloadTask? relatedTask = null)
    {
        List<Page> pagesInfo = vInfo.PagesInfo;
        var bangumi = vInfo.IsBangumi;
        var cheese = vInfo.IsCheese;
        //获取已选择的分P列表
        List<string>? selectedPages = GetSelectedPages(myOption, vInfo, input);

        Log($"共计 {pagesInfo.Count} 个分P, 已选择：" + (selectedPages == null ? "ALL" : string.Join(",", selectedPages)));
        var pagesCount = pagesInfo.Count;

        //过滤不需要的分P
        if (selectedPages != null)
        {
            pagesInfo = pagesInfo.Where(p => selectedPages.Contains(p.index.ToString( ))).ToList( );
        }

        // 根据p数选择存储路径
        savePathFormat = string.IsNullOrEmpty(myOption.FilePattern) ? SinglePageDefaultSavePath : myOption.FilePattern;
        // 1. 多P; 2. 只有1P, 但是是番剧, 尚未完结时 按照多P处理
        if (pagesCount > 1 || (bangumi && !vInfo.IsBangumiEnd))
        {
            savePathFormat = string.IsNullOrEmpty(myOption.MultiFilePattern) ? MultiPageDefaultSavePath : myOption.MultiFilePattern;
        }

        foreach (Page p in pagesInfo)
        {
            if (pagesInfo.Count > 1 && delay > 0)
            {
                Log($"停顿{delay}秒...");
                await Task.Delay(delay * 1000);
            }

            Log($"开始解析P{p.index}: {p.aid}... ({pagesInfo.IndexOf(p) + 1} of {pagesInfo.Count})");

            if (myOption.SaveArchivesToFile)
            {
                if (CheckAidFromFile(p.aid))
                {

                    Log($"aid: {p.aid}已下载过, 跳过下载...");
                    continue;
                }
            }

            await DownloadPageAsync(p, myOption, vInfo, pagesInfo, encodingPriority, dfnPriority, firstEncoding,
                downloadDanmaku, downloadDanmakuFormats, input, savePathFormat, lang, aidOri, apiType, cfg, relatedTask);

            if (myOption.SaveArchivesToFile)
            {
                SaveAidToFile(p.aid);
            }
        }

        Log("任务完成");
    }

    private static async Task DownloadPageAsync(Page p, MyOption myOption, VInfo vInfo, List<Page> selectedPagesInfo, Dictionary<string, byte> encodingPriority, Dictionary<string, int> dfnPriority,
        string firstEncoding, bool downloadDanmaku, BBDownDanmakuFormat[] downloadDanmakuFormats, string input, string savePathFormat, string lang, string aidOri, string apiType, AppConfig cfg, DownloadTask? relatedTask = null)
    {
        var desc = string.IsNullOrEmpty(p.desc) ? vInfo.Desc : p.desc;
        var bangumi = vInfo.IsBangumi;
        var pagesCount = selectedPagesInfo.Count;
        List<Subtitle> subtitleInfo = [];
        var title = vInfo.Title;
        var pic = vInfo.Pic;
        var pubTime = vInfo.PubTime;
        var selected = false; //用户是否已经手动选择过了轨道
        var retryCount = 0;
        while (true)
        {
            try
            {
            LogDebug("尝试获取章节信息...");
            p.points = await FetchPointsAsync(p.cid, p.aid, cfg);

            var videoPath = $"{p.aid}/{p.aid}.P{p.index}.{p.cid}.mp4";
            var audioPath = $"{p.aid}/{p.aid}.P{p.index}.{p.cid}.m4a";
            var coverPath = $"{p.aid}/{p.aid}.jpg";

            //处理文件夹以.开头/结尾导致的异常情况
            title = SanitizeTitle(title);

            //处理封面&&字幕
            if (!myOption.OnlyShowInfo)
            {
                if (!Directory.Exists(p.aid))
                {
                    Directory.CreateDirectory(p.aid);
                }

                if (!myOption.SkipCover && !myOption.SubOnly && !File.Exists(coverPath) && !myOption.DanmakuOnly && !myOption.CoverOnly)
                {
                    await DownloadFileAsync(pic == "" ? p.cover! : pic, coverPath, new DownloadConfig( ) { Cookie = cfg.Cookie });
                }

                if (!myOption.SkipSubtitle && !myOption.DanmakuOnly && !myOption.CoverOnly)
                {
                    LogDebug("获取字幕...");
                    subtitleInfo = await SubUtil.GetSubtitlesAsync(p.aid, p.cid, p.epid, p.index, myOption.UseIntlApi, cfg);
                    if (myOption.SkipAi && subtitleInfo.Any( ))
                    {
                        Log($"跳过下载AI字幕");
                        subtitleInfo = subtitleInfo.Where(s => !s.lan.StartsWith("ai-")).ToList( );
                    }

                    foreach (Subtitle s in subtitleInfo)
                    {
                        Log($"下载字幕 {s.lan} => {SubUtil.GetSubtitleCode(s.lan).Item2}...");
                        LogDebug("下载：{0}", s.url);
                        await SubUtil.SaveSubtitleAsync(s.url, s.path, cfg);
                        if (myOption.SubOnly && File.Exists(s.path) && File.ReadAllText(s.path) != "")
                        {
                            var _outSubPath = FormatSavePath(savePathFormat, title, null, null, p, pagesCount, apiType, pubTime);
                            if (_outSubPath.Contains('/'))
                            {
                                if (!Directory.Exists(_outSubPath.Split('/').First( )))
                                    Directory.CreateDirectory(_outSubPath.Split('/').First( ));
                            }

                            _outSubPath = Path.ChangeExtension(_outSubPath, $".{s.lan}.srt");
                            File.Move(s.path, _outSubPath, true);
                        }
                    }
                }

                if (myOption.SubOnly)
                {
                    TryDeleteEmptyDir(p.aid);
                    return;
                }
            }

            //调用解析
            ParsedResult parsedResult = await ExtractTracksAsync(aidOri, p.aid, p.cid, p.epid, myOption.UseTvApi, myOption.UseIntlApi, myOption.UseAppApi, firstEncoding, cfg);
            List<AudioMaterial> audioMaterial = [];
            if (!p.points.Any( ))
            {
                p.points = parsedResult.ExtraPoints;
            }

            if (Config.DEBUG_LOG)
            {
                File.WriteAllText($"debug_{DateTime.Now:yyyyMMddHHmmssfff}.json", parsedResult.WebJsonString);
            }

            var savePath = "";

            var downloadConfig = new DownloadConfig( )
            {
                UseAria2c = myOption.UseAria2c,
                Aria2cArgs = myOption.Aria2cArgs,
                ForceHttp = myOption.ForceHttp,
                MultiThread = myOption.MultiThread,
                RelatedTask = relatedTask,
                Cookie = cfg.Cookie,
            };

            //此处代码简直灾难, 后续优化吧
            if ((parsedResult.VideoTracks.Any( ) || parsedResult.AudioTracks.Any( )) && !parsedResult.Clips.Any( ))   //dash
            {
                if (parsedResult.VideoTracks.Count == 0)
                {
                    LogWarn("没有找到符合要求的视频流");
                    if (myOption.VideoOnly) return;
                }

                if (parsedResult.AudioTracks.Count == 0)
                {
                    LogWarn("没有找到符合要求的音频流");
                    if (myOption.AudioOnly) return;
                }

                if (myOption.AudioOnly)
                {
                    parsedResult.VideoTracks.Clear( );
                }

                if (myOption.VideoOnly)
                {
                    parsedResult.AudioTracks.Clear( );
                    parsedResult.BackgroundAudioTracks.Clear( );
                    parsedResult.RoleAudioList.Clear( );
                }

                //排序
                parsedResult.VideoTracks = SortTracks(parsedResult.VideoTracks, dfnPriority, encodingPriority, myOption.VideoAscending);
                parsedResult.AudioTracks = SortTracks(parsedResult.AudioTracks, encodingPriority, myOption.AudioAscending);
                parsedResult.BackgroundAudioTracks = SortTracks(parsedResult.BackgroundAudioTracks, encodingPriority, myOption.AudioAscending);
                foreach (AudioMaterialInfo role in parsedResult.RoleAudioList)
                {
                    role.audio = SortTracks(role.audio, encodingPriority, myOption.AudioAscending);
                }

                //打印轨道信息
                if (!myOption.HideStreams)
                {
                    PrintAllTracksInfo(parsedResult, p.dur, myOption.OnlyShowInfo);
                }

                //仅展示 跳过下载
                if (myOption.OnlyShowInfo)
                {
                    return;
                }

                var vIndex = 0; //用户手动选择的视频序号
                var aIndex = 0; //用户手动选择的音频序号

                //选择轨道
                if (myOption.Interactive && !selected)
                {
                    SelectTrackManually(parsedResult, ref vIndex, ref aIndex);
                    selected = true;
                }

                Video? selectedVideo = parsedResult.VideoTracks.ElementAtOrDefault(vIndex);
                Audio? selectedAudio = parsedResult.AudioTracks.ElementAtOrDefault(aIndex);
                Audio? selectedBackgroundAudio = parsedResult.BackgroundAudioTracks.ElementAtOrDefault(aIndex);

                LogDebug("Format Before: " + savePathFormat);
                savePath = FormatSavePath(savePathFormat, title, selectedVideo, selectedAudio, p, pagesCount, apiType, pubTime);
                LogDebug("Format After: " + savePath);

                if (downloadDanmaku)
                {
                    var danmakuXmlPath = Path.ChangeExtension(savePath, ".xml");
                    var danmakuAssPath = Path.ChangeExtension(savePath, ".ass");
                    Log("正在下载弹幕Xml文件");
                    var danmakuUrl = $"https://comment.bilibili.com/{p.cid}.xml";
                    await DownloadFileAsync(danmakuUrl, danmakuXmlPath, downloadConfig);
                    DanmakuUtil.DanmakuItem[]? danmakus = DanmakuUtil.ParseXml(danmakuXmlPath);
                    if (danmakus == null)
                    {
                        Log("弹幕Xml解析失败, 删除Xml...");
                        File.Delete(danmakuXmlPath);
                    }
                    else if (danmakus.Length == 0)
                    {
                        Log("当前视频没有弹幕, 删除Xml...");
                        File.Delete(danmakuXmlPath);
                    }
                    else if (downloadDanmakuFormats.Contains(BBDownDanmakuFormat.Ass))
                    {
                        Log("正在保存弹幕Ass文件...");
                        await DanmakuUtil.SaveAsAssAsync(danmakus, danmakuAssPath);
                    }

                    // delete xml if possible
                    if (!downloadDanmakuFormats.Contains(BBDownDanmakuFormat.Xml) && File.Exists(danmakuXmlPath))
                    {
                        File.Delete(danmakuXmlPath);
                    }

                    if (myOption.DanmakuOnly)
                    {
                        if (Directory.Exists(p.aid))
                        {
                            Directory.Delete(p.aid);
                        }

                        return;
                    }
                }

                if (myOption.CoverOnly)
                {
                    var coverUrl = pic == "" ? p.cover! : pic;
                    var newCoverPath = Path.ChangeExtension(savePath, Path.GetExtension(coverUrl));
                    await DownloadFileAsync(coverUrl, newCoverPath, downloadConfig);
                    TryDeleteEmptyDir(p.aid);
                    relatedTask?.SavePaths.Add(newCoverPath);
                }

                Log($"已选择的流:");
                PrintSelectedTrackInfo(selectedVideo, selectedAudio, p.dur);

                //用户开启了强制替换
                if (myOption.ForceReplaceHost && string.IsNullOrEmpty(myOption.UposHost))
                {
                    myOption.UposHost = BACKUP_HOST;
                }

                //处理PCDN
                HandlePcdn(myOption, selectedVideo, selectedAudio, cfg);

                if (!myOption.OnlyShowInfo && File.Exists(savePath) && new FileInfo(savePath).Length != 0)
                {
                    Log($"{savePath}已存在, 跳过下载...");
                    relatedTask?.SavePaths.Add(savePath);
                    File.Delete(coverPath);
                    if (Directory.Exists(p.aid) && Directory.GetFiles(p.aid).Length == 0)
                    {
                        Directory.Delete(p.aid, true);
                    }

                    return;
                }

                if (selectedVideo != null)
                {
                    //杜比视界, 若ffmpeg版本小于5.0, 使用mp4box封装
                    if (selectedVideo.dfn == Config.qualitys["126"] && !myOption.UseMP4box && !CheckFFmpegDOVI( ))
                    {
                        LogWarn($"检测到杜比视界清晰度且您的ffmpeg版本小于5.0,将使用mp4box混流...");
                        myOption.UseMP4box = true;
                    }

                    Log($"开始下载P{p.index}视频...");
                    await DownloadTrackAsync(selectedVideo.baseUrl, videoPath, downloadConfig, video: true);
                }

                if (selectedAudio != null)
                {
                    Log($"开始下载P{p.index}音频...");
                    await DownloadTrackAsync(selectedAudio.baseUrl, audioPath, downloadConfig, video: false);
                }

                if (selectedBackgroundAudio != null)
                {
                    var backgroundPath = $"{p.aid}/{p.aid}.{p.cid}.P{p.index}.back_ground.m4a";
                    Log($"开始下载P{p.index}背景配音...");
                    await DownloadTrackAsync(selectedBackgroundAudio.baseUrl, backgroundPath, downloadConfig, video: false);
                    audioMaterial.Add(new AudioMaterial { title = "背景音频", personName = "", path = backgroundPath });
                }

                if (parsedResult.RoleAudioList.Any( ))
                {
                    foreach (AudioMaterialInfo role in parsedResult.RoleAudioList)
                    {
                        Log($"开始下载P{p.index}配音[{role.title}]...");
                        await DownloadTrackAsync(role.audio[aIndex].baseUrl, role.path, downloadConfig, video: false);
                        audioMaterial.Add(new AudioMaterial { title = role.title, personName = role.personName, path = role.path });
                    }
                }

                Log($"下载P{p.index}完毕");
                if (!parsedResult.VideoTracks.Any( )) videoPath = "";
                if (!parsedResult.AudioTracks.Any( )) audioPath = "";
                if (myOption.SkipMux) return;
                Log($"开始合并音视频{(subtitleInfo.Any( ) ? "和字幕" : "")}...");
                if (myOption.AudioOnly)
                    savePath = savePath[..^4] + ".m4a";

                var isHevc = selectedVideo?.codecs == "HEVC";
                var code = BBDownMuxer.MuxAV(myOption.UseMP4box, p.bvid, videoPath, audioPath, audioMaterial, savePath,
                    desc,
                    title,
                    p.ownerName ?? "",
                    (pagesCount > 1 || (bangumi && !vInfo.IsBangumiEnd)) ? p.title : "",
                    File.Exists(coverPath) ? coverPath : "",
                    lang,
                    subtitleInfo, myOption.AudioOnly, myOption.VideoOnly, p.points, p.pubTime, myOption.SimplyMux, isHevc);
                if (code != 0 || !File.Exists(savePath) || new FileInfo(savePath).Length == 0)
                {
                    LogError("合并失败"); return;
                }

                Log("清理临时文件...");
                Thread.Sleep(200);
                if (parsedResult.VideoTracks.Any( )) File.Delete(videoPath);
                if (parsedResult.AudioTracks.Any( )) File.Delete(audioPath);
                if (p.points.Any( )) File.Delete(Path.Combine(Path.GetDirectoryName(string.IsNullOrEmpty(videoPath) ? audioPath : videoPath)!, "chapters"));
                foreach (Subtitle s in subtitleInfo) File.Delete(s.path);
                foreach (AudioMaterial a in audioMaterial) File.Delete(a.path);
                if (selectedPagesInfo.Count == 1 || p.index == selectedPagesInfo.Last( ).index || p.aid != selectedPagesInfo.Last( ).aid)
                    File.Delete(coverPath);
                TryDeleteEmptyDir(p.aid);
            }
            else if (parsedResult.Clips.Any( ) && parsedResult.Dfns.Any( ))   //flv
            {
                var flag = false;
                List<string> clips = parsedResult.Clips;
                List<string> dfns = parsedResult.Dfns;
                while (true)
                {
//排序
                parsedResult.VideoTracks = SortTracks(parsedResult.VideoTracks, dfnPriority, encodingPriority, myOption.VideoAscending);

                var vIndex = 0;
                if (myOption.Interactive && !flag && !selected)
                {
                    var i = 0;
                    dfns.ForEach(key => LogColor($"{i++}.{Config.qualitys[key]}"));
                    Log("请选择最想要的清晰度(输入序号): ", false);
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    vIndex = Convert.ToInt32(Console.ReadLine( ));
                    if (vIndex > dfns.Count || vIndex < 0) vIndex = 0;
                    Console.ResetColor( );
                    //重新解析
                    parsedResult.VideoTracks.Clear( );
                    parsedResult = await ExtractTracksAsync(aidOri, p.aid, p.cid, p.epid, myOption.UseTvApi, myOption.UseIntlApi, myOption.UseAppApi, firstEncoding, cfg, dfns[vIndex]);
                    if (!p.points.Any( )) p.points = parsedResult.ExtraPoints;
                    flag = true;
                    selected = true;
                    continue;
                }

                Log($"共计{parsedResult.VideoTracks.Count}条流(共有{clips.Count}个分段).");
                var index = 0;
                foreach (Video v in parsedResult.VideoTracks)
                {
                    LogColor($"{index++}. [{v.dfn}] [{v.res}] [{v.codecs}] [{v.fps}] [~{v.size / 1024 / v.dur * 8:00} kbps] [{FormatFileSize(v.size)}]".Replace("[] ", ""), false);
                    if (myOption.OnlyShowInfo)
                    {
                        clips.ForEach(Console.WriteLine);
                    }
                }

                if (myOption.OnlyShowInfo) return;
                savePath = FormatSavePath(savePathFormat, title, parsedResult.VideoTracks.ElementAtOrDefault(vIndex), null, p, pagesCount, apiType, pubTime);
                if (File.Exists(savePath) && new FileInfo(savePath).Length != 0)
                {
                    Log($"{savePath}已存在, 跳过下载...");
                    relatedTask?.SavePaths.Add(savePath);
                    if (selectedPagesInfo.Count == 1 && Directory.Exists(p.aid))
                    {
                        Directory.Delete(p.aid, true);
                    }

                    return;
                }

                var pad = string.Empty.PadRight(clips.Count.ToString( ).Length, '0');
                for (var i = 0; i < clips.Count; i++)
                {
                    var link = clips[i];
                    videoPath = $"{p.aid}/{p.aid}.P{p.index}.{p.cid}.{i.ToString(pad)}.mp4";
                    Log($"开始下载P{p.index}视频, 片段({(i + 1).ToString(pad)}/{clips.Count})...");
                    await DownloadTrackAsync(link, videoPath, downloadConfig, video: true);
                }

                Log($"下载P{p.index}完毕");
                Log("开始合并分段...");
                var files = GetFiles(Path.GetDirectoryName(videoPath)!, ".mp4");
                videoPath = $"{p.aid}/{p.aid}.P{p.index}.{p.cid}.mp4";
                BBDownMuxer.MergeFLV(files, videoPath);
                if (myOption.SkipMux) return;
                Log($"开始混流视频{(subtitleInfo.Any( ) ? "和字幕" : "")}...");
                if (myOption.AudioOnly)
                    savePath = savePath[..^4] + ".m4a";
                var code = BBDownMuxer.MuxAV(false, p.bvid, videoPath, "", audioMaterial, savePath,
                    desc,
                    title,
                    p.ownerName ?? "",
                    (pagesCount > 1 || (bangumi && !vInfo.IsBangumiEnd)) ? p.title : "",
                    File.Exists(coverPath) ? coverPath : "",
                    lang,
                    subtitleInfo, myOption.AudioOnly, myOption.VideoOnly, p.points, p.pubTime, myOption.SimplyMux);
                if (code != 0 || !File.Exists(savePath) || new FileInfo(savePath).Length == 0)
                {
                    LogError("合并失败"); return;
                }

                Log("清理临时文件...");
                Thread.Sleep(200);
                if (parsedResult.VideoTracks.Count != 0) File.Delete(videoPath);
                foreach (Subtitle s in subtitleInfo) File.Delete(s.path);
                foreach (AudioMaterial a in audioMaterial) File.Delete(a.path);
                if (p.points.Any( )) File.Delete(Path.Combine(Path.GetDirectoryName(string.IsNullOrEmpty(videoPath) ? audioPath : videoPath)!, "chapters"));
                if (selectedPagesInfo.Count == 1 || p.index == selectedPagesInfo.Last( ).index || p.aid != selectedPagesInfo.Last( ).aid)
                    File.Delete(coverPath);
                TryDeleteEmptyDir(p.aid);
                break;
                }
            }
            else
            {
                LogError("解析此分P失败(建议--debug查看详细信息)");
                if (parsedResult.WebJsonString.Length < 100)
                {
                    LogError(parsedResult.WebJsonString);
                }

                LogDebug("{0}", parsedResult.WebJsonString);
            }

            if (!string.IsNullOrWhiteSpace(savePath))
            {
                relatedTask?.SavePaths.Add(savePath);
            }
        }
        catch (Exception ex)
        {
            if (++retryCount > 2) throw;
            LogError(ex.Message);
            LogWarn("下载出现异常, 3秒后将进行自动重试...");
            await Task.Delay(3000);
        }
            break;
        }
    }

    private static async Task DoWorkAsync(MyOption myOption)
    {
        try
        {
            (Dictionary<string, byte> encodingPriority, Dictionary<string, int> dfnPriority, var firstEncoding, var downloadDanmaku, BBDownDanmakuFormat[] downloadDanmakuFormats,
                var input, var savePathFormat, var lang, var aidOri, var delay) = SetUpWork(myOption);
            (var fetchedAid, VInfo vInfo, var apiType, AppConfig cfg) = await GetVideoInfoAsync(myOption, aidOri, input);
            await DownloadPagesAsync(myOption, vInfo, encodingPriority, dfnPriority, firstEncoding, downloadDanmaku, downloadDanmakuFormats,
                input, savePathFormat, lang, fetchedAid, delay, apiType, cfg);
        }
        catch (Exception e)
        {
            Console.BackgroundColor = ConsoleColor.Red;
            Console.ForegroundColor = ConsoleColor.White;
            var msg = Config.DEBUG_LOG ? e.ToString( ) : e.Message;
            Console.Write($"{msg}{Environment.NewLine}请尝试升级到最新版本后重试!");
            Console.ResetColor( );
            Console.WriteLine( );
            Thread.Sleep(1);
            Environment.Exit(1);
        }
    }

    private static List<Video> SortTracks(List<Video> videoTracks, Dictionary<string, int> dfnPriority, Dictionary<string, byte> encodingPriority, bool videoAscending)
    {
        //用户同时输入了自定义分辨率优先级和自定义编码优先级, 则根据输入顺序依次进行排序
        return dfnPriority.Count != 0 && encodingPriority.Count != 0 && Environment.CommandLine.IndexOf("--encoding-priority", StringComparison.Ordinal) < Environment.CommandLine.IndexOf("--dfn-priority")
            ? [.. videoTracks
                .OrderBy(v => encodingPriority.GetValueOrDefault(v.codecs, (byte) 100))
                .ThenBy(v => dfnPriority.GetValueOrDefault(v.dfn, 100))
                .ThenByDescending(v => Convert.ToInt32(v.id))
                .ThenBy(v => videoAscending ? v.bandwith : -v.bandwith)]
            : [.. videoTracks
                .OrderBy(v => dfnPriority.GetValueOrDefault(v.dfn, 100))
                .ThenBy(v => encodingPriority.GetValueOrDefault(v.codecs, (byte) 100))
                .ThenByDescending(v => Convert.ToInt32(v.id))
                .ThenBy(v => videoAscending ? v.bandwith : -v.bandwith)];
    }

    private static List<Audio> SortTracks(List<Audio> audioTracks, Dictionary<string, byte> encodingPriority, bool audioAscending)
    {
        return [.. audioTracks
            .OrderBy(a => encodingPriority.GetValueOrDefault(a.shortCodecs, (byte) 100))
            .ThenBy(a => audioAscending ? a.bandwith : -a.bandwith)];
    }

    private static string FormatSavePath(string savePathFormat, string title, Video? videoTrack, Audio? audioTrack, Page p, int pagesCount, string apiType, long pubTime)
    {
        var result = savePathFormat.Replace('\\', '/');
        Regex regex = InfoRegex( );
        foreach (Match m in regex.Matches(result).Cast<Match>( ))
        {
            var key = m.Groups[1].Value;

            //解析自定义日期格式
            var defaultDateFormat = "yyyy-MM-dd_HH-mm-ss";
            string[] prefixes = ["publishDate:", "videoDate:"];
            foreach (var prefix in prefixes)
            {
                if (key.StartsWith(prefix))
                {
                    defaultDateFormat = key[(key.IndexOf(':') + 1)..];
                    key = prefix.Replace(":", "");
                    break;
                }
            }

            var v = key switch
            {
                "videoTitle" => GetValidFileName(title, filterSlash: true).Trim( ).TrimEnd('.').Trim( ),
                "pageNumber" => p.index.ToString( ),
                "pageNumberWithZero" => p.index.ToString( ).PadLeft(pagesCount.ToString( ).Length, '0'),
                "pageTitle" => GetValidFileName(p.title, filterSlash: true).Trim( ).TrimEnd('.').Trim( ),
                "bvid" => p.bvid,
                "aid" => p.aid,
                "cid" => p.cid,
                "ownerName" => p.ownerName == null ? "" : GetValidFileName(p.ownerName, filterSlash: true).Trim( ).TrimEnd('.').Trim( ),
                "ownerMid" => p.ownerMid ?? "",
                "dfn" => videoTrack == null ? "" : videoTrack.dfn,
                "res" => videoTrack == null ? "" : videoTrack.res,
                "fps" => videoTrack == null ? "" : videoTrack.fps,
                "videoCodecs" => videoTrack == null ? "" : videoTrack.codecs,
                "videoBandwidth" => videoTrack == null ? "" : videoTrack.bandwith.ToString( ),
                "audioCodecs" => audioTrack == null ? "" : audioTrack.codecs,
                "audioBandwidth" => audioTrack == null ? "" : audioTrack.bandwith.ToString( ),
                "publishDate" => FormatTimeStamp(pubTime, defaultDateFormat),
                "videoDate" => FormatTimeStamp(p.pubTime, defaultDateFormat),
                "apiType" => apiType,
                _ => $"<{key}>"
            };
            result = result.Replace(m.Value, v);
        }

        if (!result.EndsWith(".mp4")) { result += ".mp4"; }

        return result;
    }

    private static void TryDeleteEmptyDir(string path)
    {
        if (Directory.Exists(path) && Directory.GetFiles(path).Length == 0)
            Directory.Delete(path, true);
    }

    private static string SanitizeTitle(string title)
    {
        if (title.EndsWith('.')) title += "_fix";
        if (title.StartsWith('.')) title = "_" + title;
        return title;
    }

    [GeneratedRegex("<([\\w:\\-.]+?)>")]
    private static partial Regex InfoRegex( );
}
