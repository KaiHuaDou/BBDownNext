using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core;
using BBDown.Core.Entity;
using BBDown.Core.Util;

using static BBDown.BBDownDownloadUtil;
using static BBDown.Core.Entity.Entity;
using static BBDown.Core.Logger;
using static BBDown.Core.Parser;
using static BBDown.Utils;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;


namespace BBDown;

internal sealed partial class Program
{
    public static async Task DownloadPagesAsync(MyOption myOption, VInfo vInfo, Dictionary<string, byte> encodingPriority, Dictionary<string, int> dfnPriority,
        string firstEncoding, bool downloadDanmaku, BBDownDanmakuFormat[] downloadDanmakuFormats, string input, string savePathFormat, string lang, string aidOri, int delay, string apiType, AppConfig cfg, DownloadTask? relatedTask = null)
    {
        List<Page> pagesInfo = vInfo.PagesInfo;
        var bangumi = vInfo.IsBangumi;
        var cheese = vInfo.IsCheese;
        //获取已选择的分P列表
        var selectedPages = GetSelectedPages(myOption, vInfo, input);

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

        foreach (var p in pagesInfo)
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
                        await DownloadFileAsync(pic is { Length: 0 } ? p.cover! : pic, coverPath, new DownloadConfig( ) { Cookie = cfg.Cookie });
                    }

                    if (!myOption.SkipSubtitle && !myOption.DanmakuOnly && !myOption.CoverOnly)
                    {
                        LogDebug("获取字幕...");
                        subtitleInfo = await SubUtil.GetSubtitlesAsync(p.aid, p.cid, p.epid, p.index, myOption.UseIntlApi, cfg);
                        if (myOption.SkipAi && subtitleInfo.Count != 0)
                        {
                            Log($"跳过下载AI字幕");
                            subtitleInfo = subtitleInfo.Where(s => !s.lan.StartsWith("ai-")).ToList( );
                        }

                        foreach (var s in subtitleInfo)
                        {
                            Log($"下载字幕 {s.lan} => {SubUtil.GetSubtitleCode(s.lan).Item2}...");
                            LogDebug("下载：{0}", s.url);
                            await SubUtil.SaveSubtitleAsync(s.url, s.path, cfg);
                            if (myOption.SubOnly && File.Exists(s.path) && File.ReadAllText(s.path).Length != 0)
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
                if (p.points.Count == 0)
                {
                    p.points = parsedResult.ExtraPoints;
                }

                if (Config.DebugLog)
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
                if ((parsedResult.VideoTracks.Count != 0 || parsedResult.AudioTracks.Count != 0) && parsedResult.Clips.Count == 0)   //dash
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
                        var danmakus = DanmakuUtil.ParseXml(danmakuXmlPath);
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
                        var coverUrl = pic is { Length: 0 } ? p.cover! : pic;
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

                    if (parsedResult.RoleAudioList.Count != 0)
                    {
                        foreach (AudioMaterialInfo role in parsedResult.RoleAudioList)
                        {
                            Log($"开始下载P{p.index}配音[{role.title}]...");
                            await DownloadTrackAsync(role.audio[aIndex].baseUrl, role.path, downloadConfig, video: false);
                            audioMaterial.Add(new AudioMaterial { title = role.title, personName = role.personName, path = role.path });
                        }
                    }

                    Log($"下载P{p.index}完毕");
                    if (parsedResult.VideoTracks.Count == 0) videoPath = "";
                    if (parsedResult.AudioTracks.Count == 0) audioPath = "";
                    if (myOption.SkipMux) return;
                    Log($"开始合并音视频{(subtitleInfo.Count != 0 ? "和字幕" : "")}...");
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
                    if (parsedResult.VideoTracks.Count != 0) File.Delete(videoPath);
                    if (parsedResult.AudioTracks.Count != 0) File.Delete(audioPath);
                    if (p.points.Count != 0) File.Delete(Path.Combine(Path.GetDirectoryName(string.IsNullOrEmpty(videoPath) ? audioPath : videoPath)!, "chapters"));
                    foreach (var s in subtitleInfo) File.Delete(s.path);
                    foreach (var a in audioMaterial) File.Delete(a.path);
                    if (selectedPagesInfo.Count == 1 || p.index == selectedPagesInfo.Last( ).index || p.aid != selectedPagesInfo.Last( ).aid)
                        File.Delete(coverPath);
                    TryDeleteEmptyDir(p.aid);
                }
                else if (parsedResult.Clips.Count != 0 && parsedResult.Dfns.Count != 0)   //flv
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
                            if (p.points.Count == 0) p.points = parsedResult.ExtraPoints;
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
                        Log($"开始混流视频{(subtitleInfo.Count != 0 ? "和字幕" : "")}...");
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
                        foreach (var s in subtitleInfo) File.Delete(s.path);
                        foreach (var a in audioMaterial) File.Delete(a.path);
                        if (p.points.Count != 0) File.Delete(Path.Combine(Path.GetDirectoryName(string.IsNullOrEmpty(videoPath) ? audioPath : videoPath)!, "chapters"));
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

    private static List<Video> SortTracks(List<Video> videoTracks, Dictionary<string, int> dfnPriority, Dictionary<string, byte> encodingPriority, bool videoAscending)
    {
        //用户同时输入了自定义分辨率优先级和自定义编码优先级, 则根据输入顺序依次进行排序
        return dfnPriority.Count != 0 && encodingPriority.Count != 0 && Environment.CommandLine.IndexOf("--encoding-priority", StringComparison.Ordinal) < Environment.CommandLine.IndexOf("--dfn-priority")
            ? [.. videoTracks
                .OrderBy(v => encodingPriority.GetValueOrDefault(v.codecs, (byte) 100))
                .ThenBy(v => dfnPriority.GetValueOrDefault(v.dfn, 100))
                .ThenByDescending(v => Convert.ToInt32(v.id))
                .ThenBy(v => videoAscending ? v.bandwidth : -v.bandwidth)]
            : [.. videoTracks
                .OrderBy(v => dfnPriority.GetValueOrDefault(v.dfn, 100))
                .ThenBy(v => encodingPriority.GetValueOrDefault(v.codecs, (byte) 100))
                .ThenByDescending(v => Convert.ToInt32(v.id))
                .ThenBy(v => videoAscending ? v.bandwidth : -v.bandwidth)];
    }

    private static List<Audio> SortTracks(List<Audio> audioTracks, Dictionary<string, byte> encodingPriority, bool audioAscending)
    {
        return [.. audioTracks
            .OrderBy(a => encodingPriority.GetValueOrDefault(a.shortCodecs, (byte) 100))
            .ThenBy(a => audioAscending ? a.bandwidth : -a.bandwidth)];
    }

    private static string FormatSavePath(string savePathFormat, string title, Video? videoTrack, Audio? audioTrack, Page p, int pagesCount, string apiType, long pubTime)
    {
        var result = savePathFormat.Replace('\\', '/');
        var regex = InfoRegex( );
        foreach (var m in regex.Matches(result).Cast<Match>( ))
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
                "videoBandwidth" => videoTrack == null ? "" : videoTrack.bandwidth.ToString( ),
                "audioCodecs" => audioTrack == null ? "" : audioTrack.codecs,
                "audioBandwidth" => audioTrack == null ? "" : audioTrack.bandwidth.ToString( ),
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
