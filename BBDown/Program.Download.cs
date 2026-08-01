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

    public static async Task DownloadPagesAsync(MyOption myOption, WorkContext ctx, DownloadTask? relatedTask = null, CancellationToken ct = default)
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

        var errors = await RunPagesAsync(pagesInfo, myOption.StopOnError, async (p, token) =>
        {
            if (pagesInfo.Count > 1 && ctx.Delay > 0)
            {
                Log($"停顿 {ctx.Delay} 秒...");
                await Task.Delay(ctx.Delay * 1000, token);
            }

            Log($"开始解析 P{p.index}：{p.aid}...（{pagesInfo.IndexOf(p) + 1} / {pagesInfo.Count}）");

            if (myOption.SaveArchivesToFile && CheckArchive(p.aid, p.cid))
            {
                Log($"分P 已下载过（aid：{p.aid} / cid：{p.cid}），跳过下载...");
                return;
            }

            var outcome = await DownloadPageAsync(p, myOption, ctx, pagesInfo, relatedTask, token);

            // 只有完整成功（含混流）才记归档；半截失败/中止不应标记为已下载
            if (myOption.SaveArchivesToFile && !outcome.Aborted && !string.IsNullOrWhiteSpace(outcome.SavePath))
            {
                SaveArchive(p.aid, p.cid, outcome.SavePath);
            }
        }, ct);

        if (errors.Count > 0)
        {
            var list = string.Join(", ", errors.Select(e => $"P{e.Page.index}（{e.Page.aid}）"));
            LogError($"以下分P 下载失败：{list}");
            throw new AggregateException(errors.Select(e => e.Error));
        }

        Log("任务完成。");
    }

    /// <summary>
    /// 逐个跑分P 并收集失败：默认（stopOnError=false）遇到异常继续下一个，末尾一并返回；
    /// stopOnError=true 时第一个异常即停。Ctrl+C 的 OperationCanceledException 不被吞，直接上抛。
    /// 具体的延迟、归档校验、下载逻辑都在传入的委托里，本函数只负责"跑 + 聚合失败"。
    /// </summary>
    internal static async Task<List<(Page Page, Exception Error)>> RunPagesAsync(
        IReadOnlyList<Page> pages, bool stopOnError,
        Func<Page, CancellationToken, Task> run, CancellationToken ct)
    {
        var errors = new List<(Page, Exception)>( );
        foreach (var page in pages)
        {
            try
            {
                await run(page, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                errors.Add((page, ex));
                if (stopOnError) break;
            }
        }

        return errors;
    }

    // 1. 多P; 2. 只有1P, 但是是番剧, 尚未完结时 按照多P处理
    internal static string ResolveSavePathFormat(MyOption myOption, int pagesCount, bool isBangumi, bool isBangumiEnd)
    {
        return pagesCount > 1 || (isBangumi && !isBangumiEnd)
            ? (string.IsNullOrEmpty(myOption.MultiFilePattern) ? MultiPageDefaultSavePath : myOption.MultiFilePattern)
            : (string.IsNullOrEmpty(myOption.FilePattern) ? SinglePageDefaultSavePath : myOption.FilePattern);
    }

    private static async Task<PageOutcome> DownloadPageAsync(Page p, MyOption myOption, WorkContext ctx, List<Page> selectedPagesInfo, DownloadTask? relatedTask = null, CancellationToken ct = default)
    {
        var pageCtx = BuildPageContext(p, ctx, selectedPagesInfo);
        List<Subtitle> subtitleInfo = [];
        var selected = false; //用户是否已经手动选择过了轨道
        var retryCount = 0;
        var outcome = PageOutcome.Abort(selected);
        while (true)
        {
            try
            {
                LogDebug("尝试获取章节信息...");
                p.points = await FetchPointsAsync(p.cid, p.aid, ctx.Cfg);

                if (!myOption.OnlyShowInfo)
                {
                    subtitleInfo = await PrepareCoverAndSubtitlesAsync(myOption, ctx, pageCtx, ct);
                    if (myOption.SubOnly)
                    {
                        TryDeleteEmptyDir(pageCtx.TempDir);
                        return PageOutcome.Abort(selected);
                    }
                }

                //调用解析
                var parsedResult = await ExtractTracksAsync(ctx.FetchedAid, p.aid, p.cid, p.epid,
                    myOption.UseTvApi, myOption.UseIntlApi, myOption.UseAppApi, ctx.FirstEncoding, ctx.Cfg, ct: ct);
                if (p.points.Count == 0)
                {
                    p.points = parsedResult.ExtraPoints;
                }

                if (Config.DebugLog)
                {
                    File.WriteAllText(Path.Combine(ctx.WorkDir, $"debug_{DateTime.Now:yyyyMMddHHmmssfff}.json"), parsedResult.RawResponse);
                }

                var downloadConfig = BuildDownloadConfig(myOption, ctx.Cfg, relatedTask);
                outcome = await DownloadTracksAsync(parsedResult, myOption, ctx, pageCtx, subtitleInfo, downloadConfig, relatedTask, selected, ct);

                selected = outcome.Selected;
                if (outcome.Aborted) return outcome;
                if (!string.IsNullOrWhiteSpace(outcome.SavePath))
                {
                    relatedTask?.SavePaths.Add(outcome.SavePath);
                }
            }
            catch (Exception ex) when (ShouldRetry(ex))
            {
                if (++retryCount > 2) throw;
                LogError(ex.Message);
                var backoff = TimeSpan.FromSeconds(1 << retryCount);
                LogWarn($"下载出现异常，{backoff.TotalSeconds:0} 秒后将进行自动重试...");
                await Task.Delay(backoff, ct);
                continue;
            }

            break;
        }

        return outcome;
    }

    // 服务器不支持 Range 时重试多少次都是同样结果，直接放行让用户看到换单线程的提示
    // Parallel.ForEachAsync 会把分片异常裹进 AggregateException
    internal static bool IsRangeUnsupported(Exception ex)
    {
        return ex is NotSupportedException
               || (ex is AggregateException agg && agg.InnerExceptions.Any(e => e is NotSupportedException));
    }

    // Ctrl+C 触发的取消不能被当成"下载异常"退避重试，否则用户按下之后还要再等两轮退避（P1-20）
    internal static bool IsCancellation(Exception ex)
    {
        return ex is OperationCanceledException
               || (ex is AggregateException agg && agg.InnerExceptions.Any(e => e is OperationCanceledException));
    }

    internal static bool ShouldRetry(Exception ex) => !IsRangeUnsupported(ex) && !IsCancellation(ex);

    private static PageContext BuildPageContext(Page p, WorkContext ctx, List<Page> selectedPagesInfo)
    {
        var vInfo = ctx.VInfo!;
        var pagesCount = selectedPagesInfo.Count;
        var tempDir = Path.Combine(ctx.WorkDir, p.aid);
        return new PageContext(
            Page: p,
            // 原始标题，落盘前统一交给 GetValidFileName 清洗；这里保持原样是因为它还要写进容器元数据
            Title: vInfo.Title,
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
            NoForceHttp = myOption.NoForceHttp,
            SingleThread = myOption.SingleThread,
            RelatedTask = relatedTask,
            Cookie = cfg.Cookie,
        };
    }

    private static async Task<List<Subtitle>> PrepareCoverAndSubtitlesAsync(MyOption myOption, WorkContext ctx, PageContext pageCtx, CancellationToken ct = default)
    {
        var p = pageCtx.Page;
        Directory.CreateDirectory(pageCtx.TempDir);

        if (!myOption.NoCover && !myOption.SubOnly && !File.Exists(pageCtx.CoverPath) && !myOption.DanmakuOnly && !myOption.CoverOnly)
        {
            await DownloadFileAsync(pageCtx.CoverUrl, pageCtx.CoverPath, new DownloadConfig { Cookie = ctx.Cfg.Cookie }, ct);
        }

        if (myOption.NoSub || myOption.DanmakuOnly || myOption.CoverOnly)
        {
            return [];
        }

        LogDebug("获取字幕...");
        var subtitleInfo = await SubUtil.GetSubtitlesAsync(p.aid, p.cid, p.epid, p.index, myOption.UseIntlApi, ctx.Cfg, ct);
        if (!myOption.AllowAi && subtitleInfo.Count != 0)
        {
            Log($"跳过下载 AI 字幕。");
            subtitleInfo = subtitleInfo.Where(s => !s.lan.StartsWith("ai-")).ToList( );
        }

        foreach (var s in subtitleInfo)
        {
            s.path = Path.Combine(pageCtx.TempDir, Path.GetFileName(s.path));
            Log($"下载字幕 {s.lan} => {SubUtil.GetSubtitleCode(s.lan).Name}...");
            LogDebug("下载：{0}", s.url);
            await SubUtil.SaveSubtitleAsync(s.url, s.path, ctx.Cfg, ct);
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
        List<Subtitle> subtitleInfo, DownloadConfig downloadConfig, DownloadTask? relatedTask, bool selected, CancellationToken ct = default)
    {
        if ((parsedResult.VideoTracks.Count != 0 || parsedResult.AudioTracks.Count != 0) && parsedResult.Clips.Count == 0)
        {
            return await DownloadDashAsync(parsedResult, myOption, ctx, pageCtx, subtitleInfo, downloadConfig, relatedTask, selected, ct);
        }

        if (parsedResult.Clips.Count != 0 && parsedResult.Dfns.Count != 0)
        {
            return await DownloadFlvAsync(parsedResult, myOption, ctx, pageCtx, subtitleInfo, downloadConfig, relatedTask, selected, ct);
        }

        LogError("解析此分P失败（建议 --debug 查看详细信息）。");
        if (parsedResult.RawResponse.Length < 100)
        {
            LogError(parsedResult.RawResponse);
        }

        LogDebug("{0}", parsedResult.RawResponse);
        return PageOutcome.Done("", selected);
    }

    private static async Task<PageOutcome> DownloadDashAsync(ParsedResult parsedResult, MyOption myOption, WorkContext ctx, PageContext pageCtx,
        List<Subtitle> subtitleInfo, DownloadConfig downloadConfig, DownloadTask? relatedTask, bool selected, CancellationToken ct = default)
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

        if (ctx.DownloadDanmaku && await DownloadDanmakuAsync(myOption, ctx, pageCtx, savePath, downloadConfig, ct))
        {
            return PageOutcome.Abort(selected);
        }

        if (myOption.CoverOnly)
        {
            var newCoverPath = Path.ChangeExtension(savePath, Path.GetExtension(pageCtx.CoverUrl));
            await DownloadFileAsync(pageCtx.CoverUrl, newCoverPath, downloadConfig, ct);
            TryDeleteEmptyDir(pageCtx.TempDir);
            relatedTask?.SavePaths.Add(newCoverPath);
        }

        Log($"已选择的流：");
        PrintSelectedTrackInfo(selectedVideo, selectedAudio, p.dur);

        HandleCdnHost(myOption, selectedVideo, selectedAudio, ctx.Cfg);

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
            await DownloadAsync(selectedVideo.baseUrl, videoPath, downloadConfig, ct: ct);
        }

        if (selectedAudio != null)
        {
            Log($"开始下载 P{p.index} 音频...");
            await DownloadAsync(selectedAudio.baseUrl, audioPath, downloadConfig, ct: ct);
        }

        if (selectedBackgroundAudio != null)
        {
            var backgroundPath = Path.Combine(pageCtx.TempDir, $"{p.aid}.{p.cid}.P{p.index}.back_ground.m4a");
            Log($"开始下载 P{p.index} 背景配音...");
            await DownloadAsync(selectedBackgroundAudio.baseUrl, backgroundPath, downloadConfig, ct: ct);
            audioMaterial.Add(new AudioMaterial { title = "背景音频", personName = "", path = backgroundPath });
        }

        foreach (var role in parsedResult.RoleAudioList)
        {
            role.path = Path.Combine(pageCtx.TempDir, Path.GetFileName(role.path));
            Log($"开始下载 P{p.index} 配音 [{role.title}]...");
            await DownloadAsync(role.audio[aIndex].baseUrl, role.path, downloadConfig, ct: ct);
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
        int code = await BBDownMuxer.MuxAV(useMp4box, p.bvid, videoPath, audioPath, audioMaterial, savePath,
            pageCtx.Desc,
            pageCtx.Title,
            p.ownerName ?? "",
            pageCtx.EpisodeTitle,
            File.Exists(pageCtx.CoverPath) ? pageCtx.CoverPath : "",
            ctx.Lang,
            subtitleInfo, myOption.AudioOnly, myOption.VideoOnly, p.points, p.pubTime, myOption.NoMetadata, isHevc, ct);
        if (code != 0 || !File.Exists(savePath) || new FileInfo(savePath).Length == 0)
        {
            LogError("合并失败");
            return PageOutcome.Abort(selected);
        }

        CleanupTempFiles(pageCtx, videoPath, audioPath, subtitleInfo, audioMaterial);
        return PageOutcome.Done(savePath, selected);
    }

    private static async Task<PageOutcome> DownloadFlvAsync(ParsedResult parsedResult, MyOption myOption, WorkContext ctx, PageContext pageCtx,
        List<Subtitle> subtitleInfo, DownloadConfig downloadConfig, DownloadTask? relatedTask, bool selected, CancellationToken ct = default)
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
                    myOption.UseTvApi, myOption.UseIntlApi, myOption.UseAppApi, ctx.FirstEncoding, ctx.Cfg, dfns[vIndex], ct);
                if (p.points.Count == 0) p.points = parsedResult.ExtraPoints;
                reParsed = true;
                selected = true;
                continue;
            }

            HandleCdnHost(myOption, clips, ctx.Cfg);

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

            var clipPaths = await DownloadFlvClipsAsync(clips, pageCtx, downloadConfig, ct);

            Log($"下载 P{p.index} 完毕。");
            Log("开始合并分段...");
            var videoPath = pageCtx.VideoPath;
            try
            {
                await BBDownMuxer.MergeFLV([.. clipPaths], videoPath, ct);
            }
            finally
            {
                foreach (var file in clipPaths)
                {
                    SafeDelete(file);
                    PartFile.Discard(file);
                }
            }
            if (myOption.SkipMux) return PageOutcome.Abort(selected);

            Log($"开始混流视频{(subtitleInfo.Count != 0 ? "和字幕" : "")}...");
            if (myOption.AudioOnly)
                savePath = ToAudioOnlyPath(savePath);

            int code = await BBDownMuxer.MuxAV(false, p.bvid, videoPath, "", audioMaterial, savePath,
                pageCtx.Desc,
                pageCtx.Title,
                p.ownerName ?? "",
                pageCtx.EpisodeTitle,
                File.Exists(pageCtx.CoverPath) ? pageCtx.CoverPath : "",
                ctx.Lang,
                subtitleInfo, myOption.AudioOnly, myOption.VideoOnly, p.points, p.pubTime, myOption.NoMetadata, ct: ct);
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

    private static async Task<List<string>> DownloadFlvClipsAsync(List<string> clips, PageContext pageCtx, DownloadConfig downloadConfig, CancellationToken ct = default)
    {
        var p = pageCtx.Page;
        var pad = string.Empty.PadRight(clips.Count.ToString( ).Length, '0');
        var clipPaths = new List<string>(clips.Count);
        for (var i = 0; i < clips.Count; i++)
        {
            var clipPath = Path.Combine(pageCtx.TempDir, $"{p.aid}.P{p.index}.{p.cid}.{i.ToString(pad)}.mp4");
            clipPaths.Add(clipPath);
            Log($"开始下载 P{p.index} 视频，片段（{(i + 1).ToString(pad)} / {clips.Count}）...");
            await DownloadAsync(clips[i], clipPath, downloadConfig, ct: ct);
        }

        return clipPaths;
    }

    // 返回 true 表示 --danmaku-only 已完成任务，应结束该分P
    private static async Task<bool> DownloadDanmakuAsync(MyOption myOption, WorkContext ctx, PageContext pageCtx, string savePath, DownloadConfig downloadConfig, CancellationToken ct = default)
    {
        var p = pageCtx.Page;
        var danmakuXmlPath = Path.ChangeExtension(savePath, ".xml");
        var danmakuAssPath = Path.ChangeExtension(savePath, ".ass");
        Log("正在下载弹幕 XML 文件。");
        await DownloadFileAsync($"{BiliApi.DanmakuXml}/{p.cid}.xml", danmakuXmlPath, downloadConfig, ct);
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
            await DanmakuUtil.SaveAsAssAsync(danmakus, danmakuAssPath, ct);
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
        SafeDelete(videoPath);
        SafeDelete(audioPath);
        // 续传状态清单随 track 一起清理：只在混流成功时走到这里，
        // 失败/Ctrl+C 时 DownloadAsync 保留 .bbdown.part/.json，重跑即可续上
        PartFile.Discard(videoPath);
        PartFile.Discard(audioPath);
        var trackPath = string.IsNullOrEmpty(videoPath) ? audioPath : videoPath;
        if (pageCtx.Page.points.Count != 0 && !string.IsNullOrEmpty(trackPath))
            SafeDelete(Path.Combine(Path.GetDirectoryName(trackPath) ?? "", "chapters"));
        foreach (var s in subtitleInfo) SafeDelete(s.path);
        foreach (var a in audioMaterial)
        {
            SafeDelete(a.path);
            PartFile.Discard(a.path);
        }
        if (pageCtx.DeleteCoverAfterMux) SafeDelete(pageCtx.CoverPath);
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
        var matches = regex.Matches(result).Cast<Match>( ).ToList( );
        var replacements = new List<(int Index, int Length, string Value)>(matches.Count);
        foreach (var m in matches)
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
                "videoTitle" => GetValidFileName(title),
                "pageNumber" => p.index.ToString( ),
                "pageNumberWithZero" => p.index.ToString( ).PadLeft(pagesCount.ToString( ).Length, '0'),
                "pageTitle" => GetValidFileName(p.title),
                "bvid" => p.bvid,
                "aid" => p.aid,
                "cid" => p.cid,
                "ownerName" => p.ownerName == null ? "" : GetValidFileName(p.ownerName),
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
                _ => UnknownPlaceholder(key)
            };
            replacements.Add((m.Index, m.Length, v ?? ""));
        }

        for (var i = replacements.Count - 1; i >= 0; i--)
        {
            var (index, length, value) = replacements[i];
            result = result.Remove(index, length).Insert(index, value);
        }

        if (!result.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)) { result += ".mp4"; }

        return result;
    }

    private static string UnknownPlaceholder(string key)
    {
        LogWarn($"未知的文件名变量 <{key}>，已原样保留");
        return $"<{key}>";
    }

    internal static string ToAudioOnlyPath(string savePath) => Path.ChangeExtension(savePath, ".m4a");

    private static void TryDeleteEmptyDir(string path)
    {
        if (Directory.Exists(path) && Directory.GetFiles(path).Length == 0)
            Directory.Delete(path, true);
    }

    [GeneratedRegex("<([\\w:\\-.]+?)>")]
    private static partial Regex InfoRegex( );
}
