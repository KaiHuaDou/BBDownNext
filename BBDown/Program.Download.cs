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
using static BBDown.Core.Util.FileNameUtil;
using static BBDown.Utils;

namespace BBDown;

internal sealed partial class Program
{
    // Aborted 为 true 表示该分P应立即结束（不再登记 SavePath）；Selected 需回传以跨重试保留用户已手动选轨的状态
    private readonly record struct PageOutcome(bool Aborted, string SavePath, bool Selected)
    {
        public static PageOutcome Abort(bool selected) => new(true, "", selected);

        public static PageOutcome Done(string savePath, bool selected) => new(false, savePath, selected);
    }

    public static async Task DownloadPagesAsync(MyOption myOption, WorkContext ctx, DownloadTask? relatedTask = null)
    {
        var vInfo = ctx.VInfo!;
        var pagesInfo = vInfo.PagesInfo;
        //获取已选择的分P列表
        var selectedPages = GetSelectedPages(myOption, vInfo, ctx.Input);

        Log($"共计 {pagesInfo.Count} 个分P，已选择：" + (selectedPages == null ? "ALL" : string.Join(",", selectedPages)));
        var pagesCount = pagesInfo.Count;

        //过滤不需要的分P
        if (selectedPages != null)
        {
            pagesInfo = pagesInfo.Where(p => selectedPages.Contains(p.index.ToString( ))).ToList( );
        }

        ctx = ctx with { SavePathFormat = ResolveSavePathFormat(myOption, pagesCount, vInfo.IsBangumi, vInfo.IsBangumiEnd) };

        foreach (var p in pagesInfo)
        {
            if (pagesInfo.Count > 1 && ctx.Delay > 0)
            {
                Log($"停顿 {ctx.Delay} 秒...");
                await Task.Delay(ctx.Delay * 1000);
            }

            Log($"开始解析 P{p.index}：{p.aid}...（{pagesInfo.IndexOf(p) + 1} / {pagesInfo.Count}）");

            if (myOption.SaveArchivesToFile && CheckAidFromFile(p.aid))
            {
                Log($"aid：{p.aid} 已下载过，跳过下载...");
                continue;
            }

            await DownloadPageAsync(p, myOption, ctx, pagesInfo, relatedTask);

            if (myOption.SaveArchivesToFile)
            {
                SaveAidToFile(p.aid);
            }
        }

        Log("任务完成。");
    }

    // 1. 多P; 2. 只有1P, 但是是番剧, 尚未完结时 按照多P处理
    internal static string ResolveSavePathFormat(MyOption myOption, int pagesCount, bool isBangumi, bool isBangumiEnd)
    {
        return pagesCount > 1 || (isBangumi && !isBangumiEnd)
            ? (string.IsNullOrEmpty(myOption.MultiFilePattern) ? MultiPageDefaultSavePath : myOption.MultiFilePattern)
            : (string.IsNullOrEmpty(myOption.FilePattern) ? SinglePageDefaultSavePath : myOption.FilePattern);
    }

    private static async Task DownloadPageAsync(Page p, MyOption myOption, WorkContext ctx, List<Page> selectedPagesInfo, DownloadTask? relatedTask = null)
    {
        var pageCtx = BuildPageContext(p, ctx, selectedPagesInfo);
        List<Subtitle> subtitleInfo = [];
        var selected = false; //用户是否已经手动选择过了轨道
        var retryCount = 0;
        while (true)
        {
            try
            {
                LogDebug("尝试获取章节信息...");
                p.points = await FetchPointsAsync(p.cid, p.aid, ctx.Cfg);

                if (!myOption.OnlyShowInfo)
                {
                    subtitleInfo = await PrepareCoverAndSubtitlesAsync(myOption, ctx, pageCtx);
                    if (myOption.SubOnly)
                    {
                        TryDeleteEmptyDir(pageCtx.TempDir);
                        return;
                    }
                }

                //调用解析
                var parsedResult = await ExtractTracksAsync(ctx.FetchedAid, p.aid, p.cid, p.epid,
                    myOption.UseTvApi, myOption.UseIntlApi, myOption.UseAppApi, ctx.FirstEncoding, ctx.Cfg);
                if (p.points.Count == 0)
                {
                    p.points = parsedResult.ExtraPoints;
                }

                if (Config.DebugLog)
                {
                    File.WriteAllText(Path.Combine(ctx.WorkDir, $"debug_{DateTime.Now:yyyyMMddHHmmssfff}.json"), parsedResult.WebJsonString);
                }

                var downloadConfig = BuildDownloadConfig(myOption, ctx.Cfg, relatedTask);
                var outcome = await DownloadTracksAsync(parsedResult, myOption, ctx, pageCtx, subtitleInfo, downloadConfig, relatedTask, selected);

                selected = outcome.Selected;
                if (outcome.Aborted) return;
                if (!string.IsNullOrWhiteSpace(outcome.SavePath))
                {
                    relatedTask?.SavePaths.Add(outcome.SavePath);
                }
            }
            catch (Exception ex)
            {
                if (++retryCount > 2) throw;
                LogError(ex.Message);
                LogWarn("下载出现异常，3 秒后将进行自动重试...");
                await Task.Delay(3000);
                continue;
            }

            break;
        }
    }

    private static PageContext BuildPageContext(Page p, WorkContext ctx, List<Page> selectedPagesInfo)
    {
        var vInfo = ctx.VInfo!;
        var pagesCount = selectedPagesInfo.Count;
        var tempDir = Path.Combine(ctx.WorkDir, p.aid);
        return new PageContext(
            Page: p,
            //处理文件夹以.开头/结尾导致的异常情况
            Title: SanitizeTitle(vInfo.Title),
            Desc: string.IsNullOrEmpty(p.desc) ? vInfo.Desc : p.desc,
            EpisodeTitle: BuildEpisodeTitle(p, pagesCount, vInfo.IsBangumi, vInfo.IsBangumiEnd),
            TempDir: tempDir,
            VideoPath: Path.Combine(tempDir, $"{p.aid}.P{p.index}.{p.cid}.mp4"),
            AudioPath: Path.Combine(tempDir, $"{p.aid}.P{p.index}.{p.cid}.m4a"),
            CoverPath: Path.Combine(tempDir, $"{p.aid}.jpg"),
            CoverUrl: vInfo.Pic is { Length: 0 } ? p.cover! : vInfo.Pic,
            PubTime: vInfo.PubTime,
            PagesCount: pagesCount,
            DeleteCoverAfterMux: ShouldDeleteCover(p, selectedPagesInfo));
    }

    internal static string BuildEpisodeTitle(Page p, int pagesCount, bool isBangumi, bool isBangumiEnd)
    {
        return pagesCount > 1 || (isBangumi && !isBangumiEnd) ? p.title : "";
    }

    internal static bool ShouldDeleteCover(Page p, List<Page> selectedPagesInfo)
    {
        return selectedPagesInfo.Count == 1
            || p.index == selectedPagesInfo.Last( ).index
            || p.aid != selectedPagesInfo.Last( ).aid;
    }

    internal static DownloadConfig BuildDownloadConfig(MyOption myOption, AppConfig cfg, DownloadTask? relatedTask)
    {
        return new DownloadConfig
        {
            UseAria2c = myOption.UseAria2c,
            Aria2cArgs = myOption.Aria2cArgs,
            ForceHttp = myOption.ForceHttp,
            MultiThread = myOption.MultiThread,
            RelatedTask = relatedTask,
            Cookie = cfg.Cookie,
        };
    }

    private static async Task<List<Subtitle>> PrepareCoverAndSubtitlesAsync(MyOption myOption, WorkContext ctx, PageContext pageCtx)
    {
        var p = pageCtx.Page;
        Directory.CreateDirectory(pageCtx.TempDir);

        if (!myOption.SkipCover && !myOption.SubOnly && !File.Exists(pageCtx.CoverPath) && !myOption.DanmakuOnly && !myOption.CoverOnly)
        {
            await DownloadFileAsync(pageCtx.CoverUrl, pageCtx.CoverPath, new DownloadConfig { Cookie = ctx.Cfg.Cookie });
        }

        if (myOption.SkipSubtitle || myOption.DanmakuOnly || myOption.CoverOnly)
        {
            return [];
        }

        LogDebug("获取字幕...");
        var subtitleInfo = await SubUtil.GetSubtitlesAsync(p.aid, p.cid, p.epid, p.index, myOption.UseIntlApi, ctx.Cfg);
        if (myOption.SkipAi && subtitleInfo.Count != 0)
        {
            Log($"跳过下载 AI 字幕。");
            subtitleInfo = subtitleInfo.Where(s => !s.lan.StartsWith("ai-")).ToList( );
        }

        foreach (var s in subtitleInfo)
        {
            s.path = Path.Combine(pageCtx.TempDir, Path.GetFileName(s.path));
            Log($"下载字幕 {s.lan} => {SubUtil.GetSubtitleCode(s.lan).Name}...");
            LogDebug("下载：{0}", s.url);
            await SubUtil.SaveSubtitleAsync(s.url, s.path, ctx.Cfg);
            if (myOption.SubOnly && File.Exists(s.path) && File.ReadAllText(s.path).Length != 0)
            {
                MoveSubtitleToOutput(s, ctx, pageCtx);
            }
        }

        return subtitleInfo;
    }

    private static void MoveSubtitleToOutput(Subtitle s, WorkContext ctx, PageContext pageCtx)
    {
        var outSubPath = FormatSavePath(ctx, pageCtx, null, null);
        var outDir = Path.GetDirectoryName(outSubPath);
        if (!string.IsNullOrEmpty(outDir)) Directory.CreateDirectory(outDir);

        outSubPath = Path.ChangeExtension(outSubPath, $".{s.lan}.srt");
        File.Move(s.path, outSubPath, true);
    }

    private static async Task<PageOutcome> DownloadTracksAsync(ParsedResult parsedResult, MyOption myOption, WorkContext ctx, PageContext pageCtx,
        List<Subtitle> subtitleInfo, DownloadConfig downloadConfig, DownloadTask? relatedTask, bool selected)
    {
        if ((parsedResult.VideoTracks.Count != 0 || parsedResult.AudioTracks.Count != 0) && parsedResult.Clips.Count == 0)
        {
            return await DownloadDashAsync(parsedResult, myOption, ctx, pageCtx, subtitleInfo, downloadConfig, relatedTask, selected);
        }

        if (parsedResult.Clips.Count != 0 && parsedResult.Dfns.Count != 0)
        {
            return await DownloadFlvAsync(parsedResult, myOption, ctx, pageCtx, subtitleInfo, downloadConfig, relatedTask, selected);
        }

        LogError("解析此分P失败（建议 --debug 查看详细信息）。");
        if (parsedResult.WebJsonString.Length < 100)
        {
            LogError(parsedResult.WebJsonString);
        }

        LogDebug("{0}", parsedResult.WebJsonString);
        return PageOutcome.Done("", selected);
    }

    private static async Task<PageOutcome> DownloadDashAsync(ParsedResult parsedResult, MyOption myOption, WorkContext ctx, PageContext pageCtx,
        List<Subtitle> subtitleInfo, DownloadConfig downloadConfig, DownloadTask? relatedTask, bool selected)
    {
        var p = pageCtx.Page;

        if (parsedResult.VideoTracks.Count == 0)
        {
            LogWarn("没有找到符合要求的视频流。");
            if (myOption.VideoOnly) return PageOutcome.Abort(selected);
        }

        if (parsedResult.AudioTracks.Count == 0)
        {
            LogWarn("没有找到符合要求的音频流。");
            if (myOption.AudioOnly) return PageOutcome.Abort(selected);
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

        SortDashTracks(parsedResult, ctx, myOption);

        if (!myOption.HideStreams)
        {
            PrintAllTracksInfo(parsedResult, p.dur, myOption.OnlyShowInfo);
        }

        //仅展示 跳过下载
        if (myOption.OnlyShowInfo)
        {
            return PageOutcome.Abort(selected);
        }

        var vIndex = 0; //用户手动选择的视频序号
        var aIndex = 0; //用户手动选择的音频序号
        if (myOption.Interactive && !selected)
        {
            SelectTrackManually(parsedResult, ref vIndex, ref aIndex);
            selected = true;
        }

        var selectedVideo = parsedResult.VideoTracks.ElementAtOrDefault(vIndex);
        var selectedAudio = parsedResult.AudioTracks.ElementAtOrDefault(aIndex);
        var selectedBackgroundAudio = parsedResult.BackgroundAudioTracks.ElementAtOrDefault(aIndex);

        LogDebug("Format Before: " + ctx.SavePathFormat);
        var savePath = FormatSavePath(ctx, pageCtx, selectedVideo, selectedAudio);
        LogDebug("Format After: " + savePath);

        if (ctx.DownloadDanmaku && await DownloadDanmakuAsync(myOption, ctx, pageCtx, savePath, downloadConfig))
        {
            return PageOutcome.Abort(selected);
        }

        if (myOption.CoverOnly)
        {
            var newCoverPath = Path.ChangeExtension(savePath, Path.GetExtension(pageCtx.CoverUrl));
            await DownloadFileAsync(pageCtx.CoverUrl, newCoverPath, downloadConfig);
            TryDeleteEmptyDir(pageCtx.TempDir);
            relatedTask?.SavePaths.Add(newCoverPath);
        }

        Log($"已选择的流：");
        PrintSelectedTrackInfo(selectedVideo, selectedAudio, p.dur);

        //用户开启了强制替换
        if (myOption.ForceReplaceHost && string.IsNullOrEmpty(myOption.UposHost))
        {
            myOption.UposHost = BACKUP_HOST;
        }

        //处理PCDN
        HandlePcdn(myOption, selectedVideo, selectedAudio, ctx.Cfg);

        if (File.Exists(savePath) && new FileInfo(savePath).Length != 0)
        {
            Log($"{savePath} 已存在，跳过下载...");
            relatedTask?.SavePaths.Add(savePath);
            File.Delete(pageCtx.CoverPath);
            TryDeleteEmptyDir(pageCtx.TempDir);
            return PageOutcome.Abort(selected);
        }

        var videoPath = pageCtx.VideoPath;
        var audioPath = pageCtx.AudioPath;
        List<AudioMaterial> audioMaterial = [];
        var useMp4box = myOption.UseMP4box;

        if (selectedVideo != null)
        {
            //杜比视界(id=126), 若 ffmpeg 版本小于 5.0, 使用 mp4box 封装
            if (selectedVideo.id == "126" && !useMp4box && !CheckFFmpegDOVI( ))
            {
                LogWarn($"检测到杜比视界清晰度且您的 ffmpeg 版本小于 5.0，将使用 mp4box 混流...");
                useMp4box = true;
            }

            Log($"开始下载 P{p.index} 视频...");
            await DownloadTrackAsync(selectedVideo.baseUrl, videoPath, downloadConfig, video: true);
        }

        if (selectedAudio != null)
        {
            Log($"开始下载 P{p.index} 音频...");
            await DownloadTrackAsync(selectedAudio.baseUrl, audioPath, downloadConfig, video: false);
        }

        if (selectedBackgroundAudio != null)
        {
            var backgroundPath = Path.Combine(pageCtx.TempDir, $"{p.aid}.{p.cid}.P{p.index}.back_ground.m4a");
            Log($"开始下载 P{p.index} 背景配音...");
            await DownloadTrackAsync(selectedBackgroundAudio.baseUrl, backgroundPath, downloadConfig, video: false);
            audioMaterial.Add(new AudioMaterial { title = "背景音频", personName = "", path = backgroundPath });
        }

        foreach (var role in parsedResult.RoleAudioList)
        {
            role.path = Path.Combine(pageCtx.TempDir, Path.GetFileName(role.path));
            Log($"开始下载 P{p.index} 配音 [{role.title}]...");
            await DownloadTrackAsync(role.audio[aIndex].baseUrl, role.path, downloadConfig, video: false);
            audioMaterial.Add(new AudioMaterial { title = role.title, personName = role.personName, path = role.path });
        }

        Log($"下载 P{p.index} 完毕。");
        if (parsedResult.VideoTracks.Count == 0) videoPath = "";
        if (parsedResult.AudioTracks.Count == 0) audioPath = "";
        if (myOption.SkipMux) return PageOutcome.Abort(selected);

        Log($"开始合并音视频{(subtitleInfo.Count != 0 ? "和字幕" : "")}...");
        if (myOption.AudioOnly)
            savePath = ToAudioOnlyPath(savePath);

        var isHevc = selectedVideo?.codecs == "HEVC";
        var code = BBDownMuxer.MuxAV(useMp4box, p.bvid, videoPath, audioPath, audioMaterial, savePath,
            pageCtx.Desc,
            pageCtx.Title,
            p.ownerName ?? "",
            pageCtx.EpisodeTitle,
            File.Exists(pageCtx.CoverPath) ? pageCtx.CoverPath : "",
            ctx.Lang,
            subtitleInfo, myOption.AudioOnly, myOption.VideoOnly, p.points, p.pubTime, myOption.SimplyMux, isHevc);
        if (code != 0 || !File.Exists(savePath) || new FileInfo(savePath).Length == 0)
        {
            LogError("合并失败");
            return PageOutcome.Abort(selected);
        }

        CleanupTempFiles(pageCtx, videoPath, audioPath, subtitleInfo, audioMaterial);
        return PageOutcome.Done(savePath, selected);
    }

    private static async Task<PageOutcome> DownloadFlvAsync(ParsedResult parsedResult, MyOption myOption, WorkContext ctx, PageContext pageCtx,
        List<Subtitle> subtitleInfo, DownloadConfig downloadConfig, DownloadTask? relatedTask, bool selected)
    {
        var p = pageCtx.Page;
        List<AudioMaterial> audioMaterial = [];
        var reParsed = false;
        // clips/dfns 取自首次解析结果，重解析后仍沿用旧引用（与原实现一致）
        var clips = parsedResult.Clips;
        var dfns = parsedResult.Dfns;
        while (true)
        {
            parsedResult.VideoTracks = SortTracks(parsedResult.VideoTracks, ctx.DfnPriority, ctx.EncodingPriority, myOption.VideoAscending, ctx.EncodingFirst);

            var vIndex = 0;
            if (myOption.Interactive && !reParsed && !selected)
            {
                vIndex = SelectDfnManually(dfns);
                //重新解析
                parsedResult.VideoTracks.Clear( );
                parsedResult = await ExtractTracksAsync(ctx.FetchedAid, p.aid, p.cid, p.epid,
                    myOption.UseTvApi, myOption.UseIntlApi, myOption.UseAppApi, ctx.FirstEncoding, ctx.Cfg, dfns[vIndex]);
                if (p.points.Count == 0) p.points = parsedResult.ExtraPoints;
                reParsed = true;
                selected = true;
                continue;
            }

            Log($"共计 {parsedResult.VideoTracks.Count} 条流（共有 {clips.Count} 个分段）。");
            var index = 0;
            foreach (var v in parsedResult.VideoTracks)
            {
                LogColor($"{index++}. [{v.dfn}] [{v.res}] [{v.codecs}] [{v.fps}] [~{v.size / 1024 / v.dur * 8:00} kbps] [{FormatFileSize(v.size)}]".Replace("[] ", ""), false);
                if (myOption.OnlyShowInfo)
                {
                    clips.ForEach(Console.WriteLine);
                }
            }

            if (myOption.OnlyShowInfo) return PageOutcome.Abort(selected);

            var savePath = FormatSavePath(ctx, pageCtx, parsedResult.VideoTracks.ElementAtOrDefault(vIndex), null);
            if (File.Exists(savePath) && new FileInfo(savePath).Length != 0)
            {
                Log($"{savePath} 已存在，跳过下载...");
                relatedTask?.SavePaths.Add(savePath);
                if (pageCtx.PagesCount == 1 && Directory.Exists(pageCtx.TempDir))
                {
                    Directory.Delete(pageCtx.TempDir, true);
                }

                return PageOutcome.Abort(selected);
            }

            var lastClipPath = await DownloadFlvClipsAsync(clips, pageCtx, downloadConfig);

            Log($"下载 P{p.index} 完毕。");
            Log("开始合并分段...");
            var files = GetFiles(Path.GetDirectoryName(lastClipPath)!, ".mp4");
            var videoPath = pageCtx.VideoPath;
            BBDownMuxer.MergeFLV(files, videoPath);
            if (myOption.SkipMux) return PageOutcome.Abort(selected);

            Log($"开始混流视频{(subtitleInfo.Count != 0 ? "和字幕" : "")}...");
            if (myOption.AudioOnly)
                savePath = ToAudioOnlyPath(savePath);

            var code = BBDownMuxer.MuxAV(false, p.bvid, videoPath, "", audioMaterial, savePath,
                pageCtx.Desc,
                pageCtx.Title,
                p.ownerName ?? "",
                pageCtx.EpisodeTitle,
                File.Exists(pageCtx.CoverPath) ? pageCtx.CoverPath : "",
                ctx.Lang,
                subtitleInfo, myOption.AudioOnly, myOption.VideoOnly, p.points, p.pubTime, myOption.SimplyMux);
            if (code != 0 || !File.Exists(savePath) || new FileInfo(savePath).Length == 0)
            {
                LogError("合并失败");
                return PageOutcome.Abort(selected);
            }

            CleanupTempFiles(pageCtx, parsedResult.VideoTracks.Count != 0 ? videoPath : "", "", subtitleInfo, audioMaterial);
            return PageOutcome.Done(savePath, selected);
        }
    }

    private static int SelectDfnManually(List<string> dfns)
    {
        var i = 0;
        dfns.ForEach(key => LogColor($"{i++}.{Config.GetQualityName(key)}"));
        Log("请选择最想要的清晰度（输入序号）：", false);
        Console.ForegroundColor = ConsoleColor.Cyan;
        var vIndex = Convert.ToInt32(Console.ReadLine( ));
        if (vIndex > dfns.Count || vIndex < 0) vIndex = 0;
        Console.ResetColor( );
        return vIndex;
    }

    private static async Task<string> DownloadFlvClipsAsync(List<string> clips, PageContext pageCtx, DownloadConfig downloadConfig)
    {
        var p = pageCtx.Page;
        var pad = string.Empty.PadRight(clips.Count.ToString( ).Length, '0');
        var clipPath = pageCtx.VideoPath;
        for (var i = 0; i < clips.Count; i++)
        {
            clipPath = Path.Combine(pageCtx.TempDir, $"{p.aid}.P{p.index}.{p.cid}.{i.ToString(pad)}.mp4");
            Log($"开始下载 P{p.index} 视频，片段（{(i + 1).ToString(pad)} / {clips.Count}）...");
            await DownloadTrackAsync(clips[i], clipPath, downloadConfig, video: true);
        }

        return clipPath;
    }

    // 返回 true 表示 --danmaku-only 已完成任务，应结束该分P
    private static async Task<bool> DownloadDanmakuAsync(MyOption myOption, WorkContext ctx, PageContext pageCtx, string savePath, DownloadConfig downloadConfig)
    {
        var p = pageCtx.Page;
        var danmakuXmlPath = Path.ChangeExtension(savePath, ".xml");
        var danmakuAssPath = Path.ChangeExtension(savePath, ".ass");
        Log("正在下载弹幕 XML 文件。");
        await DownloadFileAsync($"{BiliApi.DanmakuXml}/{p.cid}.xml", danmakuXmlPath, downloadConfig);
        var danmakus = DanmakuUtil.ParseXml(danmakuXmlPath);
        if (danmakus == null)
        {
            Log("弹幕 XML 解析失败，删除 XML...");
            File.Delete(danmakuXmlPath);
        }
        else if (danmakus.Length == 0)
        {
            Log("当前视频没有弹幕，删除 XML...");
            File.Delete(danmakuXmlPath);
        }
        else if (ctx.DownloadDanmakuFormats.Contains(BBDownDanmakuFormat.Ass))
        {
            Log("正在保存弹幕 ASS 文件...");
            await DanmakuUtil.SaveAsAssAsync(danmakus, danmakuAssPath);
        }

        if (!ctx.DownloadDanmakuFormats.Contains(BBDownDanmakuFormat.Xml) && File.Exists(danmakuXmlPath))
        {
            File.Delete(danmakuXmlPath);
        }

        if (!myOption.DanmakuOnly) return false;

        TryDeleteEmptyDir(pageCtx.TempDir);

        return true;
    }

    private static void SortDashTracks(ParsedResult parsedResult, WorkContext ctx, MyOption myOption)
    {
        parsedResult.VideoTracks = SortTracks(parsedResult.VideoTracks, ctx.DfnPriority, ctx.EncodingPriority, myOption.VideoAscending, ctx.EncodingFirst);
        parsedResult.AudioTracks = SortTracks(parsedResult.AudioTracks, ctx.EncodingPriority, myOption.AudioAscending);
        parsedResult.BackgroundAudioTracks = SortTracks(parsedResult.BackgroundAudioTracks, ctx.EncodingPriority, myOption.AudioAscending);
        foreach (var role in parsedResult.RoleAudioList)
        {
            role.audio = SortTracks(role.audio, ctx.EncodingPriority, myOption.AudioAscending);
        }
    }

    private static void CleanupTempFiles(PageContext pageCtx, string videoPath, string audioPath, List<Subtitle> subtitleInfo, List<AudioMaterial> audioMaterial)
    {
        Log("清理临时文件...");
        Thread.Sleep(200);
        if (!string.IsNullOrEmpty(videoPath)) File.Delete(videoPath);
        if (!string.IsNullOrEmpty(audioPath)) File.Delete(audioPath);
        var trackPath = string.IsNullOrEmpty(videoPath) ? audioPath : videoPath;
        if (pageCtx.Page.points.Count != 0 && !string.IsNullOrEmpty(trackPath))
            File.Delete(Path.Combine(Path.GetDirectoryName(trackPath) ?? "", "chapters"));
        foreach (var s in subtitleInfo) File.Delete(s.path);
        foreach (var a in audioMaterial) File.Delete(a.path);
        if (pageCtx.DeleteCoverAfterMux) File.Delete(pageCtx.CoverPath);
        TryDeleteEmptyDir(pageCtx.TempDir);
    }

    internal static List<Video> SortTracks(List<Video> videoTracks, Dictionary<string, int> dfnPriority, Dictionary<string, byte> encodingPriority, bool videoAscending, bool encodingFirst)
    {
        //用户同时输入了自定义分辨率优先级和自定义编码优先级, 则根据输入顺序依次进行排序
        return dfnPriority.Count != 0 && encodingPriority.Count != 0 && encodingFirst
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

    internal static List<Audio> SortTracks(List<Audio> audioTracks, Dictionary<string, byte> encodingPriority, bool audioAscending)
    {
        return [.. audioTracks
            .OrderBy(a => encodingPriority.GetValueOrDefault(a.shortCodecs, (byte) 100))
            .ThenBy(a => audioAscending ? a.bandwidth : -a.bandwidth)];
    }

    private static string FormatSavePath(WorkContext ctx, PageContext pageCtx, Video? videoTrack, Audio? audioTrack)
    {
        var relative = FormatSavePath(ctx.SavePathFormat, pageCtx.Title, videoTrack, audioTrack, pageCtx.Page, pageCtx.PagesCount, ctx.ApiType, pageCtx.PubTime);
        return Path.Combine(ctx.WorkDir, relative);
    }

    internal static string FormatSavePath(string savePathFormat, string title, Video? videoTrack, Audio? audioTrack, Page p, int pagesCount, string apiType, long pubTime)
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
                "videoTitle" => GetValidFileName(title).Trim( ).TrimEnd('.').Trim( ),
                "pageNumber" => p.index.ToString( ),
                "pageNumberWithZero" => p.index.ToString( ).PadLeft(pagesCount.ToString( ).Length, '0'),
                "pageTitle" => GetValidFileName(p.title).Trim( ).TrimEnd('.').Trim( ),
                "bvid" => p.bvid,
                "aid" => p.aid,
                "cid" => p.cid,
                "ownerName" => p.ownerName == null ? "" : GetValidFileName(p.ownerName).Trim( ).TrimEnd('.').Trim( ),
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

    internal static string ToAudioOnlyPath(string savePath) => savePath[..^4] + ".m4a";

    private static void TryDeleteEmptyDir(string path)
    {
        if (Directory.Exists(path) && Directory.GetFiles(path).Length == 0)
            Directory.Delete(path, true);
    }

    internal static string SanitizeTitle(string title)
    {
        if (title.EndsWith('.')) title += "_fix";
        if (title.StartsWith('.')) title = "_" + title;
        return title;
    }

    [GeneratedRegex("<([\\w:\\-.]+?)>")]
    private static partial Regex InfoRegex( );
}
