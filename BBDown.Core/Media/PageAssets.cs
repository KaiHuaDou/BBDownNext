using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core;
using BBDown.Core.Util;
using BBDown.Core.Mux;
using BBDown.Core.Download;

using static BBDown.Core.Logger;
using static BBDown.Core.Download.DownloadUtil;
using BBDown.Core.Entity;

namespace BBDown.Core.Media;

public static class PageAssets
{
    internal static async Task<List<Subtitle>> PrepareAsync(DownloadSession session, CancellationToken ct = default)
    {
        var (myOption, ctx, pageCtx, _, _, _) = session;
        var p = pageCtx.Page;
        Directory.CreateDirectory(pageCtx.TempDir);

        // 混流封面（C）需要临时封面文件；独立封面（c）不走临时目录，由 DashDownload 直接落到输出路径
        if (myOption.Content.Has(DownloadContent.MuxCover) && !File.Exists(pageCtx.CoverPath))
        {
            await DownloadFileAsync(pageCtx.CoverUrl, pageCtx.CoverPath, new DownloadConfig { Cookie = ctx.Fetch.Cfg.Cookie, Aria2cPath = ctx.Run.Tools.Aria2c }, ct);
        }

        // 无 s / S 则不下字幕；S 不依赖 s（AI 字幕与普通字幕独立）
        if (!myOption.Content.HasAny(DownloadContent.Subtitle | DownloadContent.AiSubtitle))
        {
            return [];
        }

        LogDebug("获取字幕...");
        var subtitleInfo = await SubUtil.GetSubtitlesAsync(p.Aid, p.Cid, p.EpId, p.Index, myOption.Api == ApiType.Intl, ctx.Fetch.Cfg, ct);
        if (!myOption.Content.Has(DownloadContent.AiSubtitle) && subtitleInfo.Count != 0)
        {
            Log("跳过下载 AI 字幕");
            subtitleInfo = [.. subtitleInfo.Where(s => !s.Lan.StartsWith("ai-"))];
        }

        foreach (var s in subtitleInfo)
        {
            s.Path = Path.Combine(pageCtx.TempDir, Path.GetFileName(s.Path));
            Log($"下载字幕 {s.Lan} => {SubUtil.GetSubtitleCode(s.Lan).Name}...");
            LogDebug("下载：{0}", s.Url);
            await SubUtil.SaveSubtitleAsync(s.Url, s.Path, ctx.Fetch.Cfg, ct);
            if (File.Exists(s.Path) && File.ReadAllText(s.Path).Length != 0)
            {
                MoveSubtitleToOutput(s, ctx, pageCtx, !myOption.Content.Has(DownloadContent.Video));
            }
            else if (File.Exists(s.Path))
            {
                File.Delete(s.Path);
            }
        }

        return subtitleInfo;
    }

    private static void MoveSubtitleToOutput(Subtitle s, WorkContext ctx, PageContext pageCtx, bool audioOnly)
    {
        var outSubPath = SavePath.Build(ctx, pageCtx, null, null);
        // 产物扩展名随混流方式/内容集：纯音频与 mkv 容器须与混流产物一致，否则字幕挂在 .mp4 基底
        outSubPath = MuxFinish.ToOutputPath(outSubPath, ctx.Run.Mux, !audioOnly);

        var outDir = Path.GetDirectoryName(outSubPath);
        if (!string.IsNullOrEmpty(outDir))
        {
            Directory.CreateDirectory(outDir);
        }

        outSubPath = Path.ChangeExtension(outSubPath, $".{s.Lan}.srt");
        File.Move(s.Path, outSubPath, true);
        // 移动后临时路径失效，回写输出路径供混流内嵌与收尾逻辑使用
        s.Path = outSubPath;
    }

    internal static async Task<bool> DownloadDanmakuAsync(DownloadSession session, string savePath, CancellationToken ct = default)
    {
        var (myOption, ctx, pageCtx, _, downloadConfig, _) = session;
        var p = pageCtx.Page;
        var danmakuXmlPath = Path.ChangeExtension(savePath, ".xml");
        var danmakuAssPath = Path.ChangeExtension(savePath, ".ass");
        Log("正在下载 XML 弹幕文件...");
        await DownloadFileAsync($"{BiliApi.DanmakuXml}/{p.Cid}.xml", danmakuXmlPath, downloadConfig, ct);
        var danmakus = DanmakuUtil.ParseXml(danmakuXmlPath);
        if (danmakus == null)
        {
            Log("XML 弹幕解析失败");
            File.Delete(danmakuXmlPath);
        }
        else if (danmakus.Length == 0)
        {
            Log("当前视频没有弹幕");
            File.Delete(danmakuXmlPath);
        }
        else if (ctx.Run.DownloadDanmakuFormats.Contains(DanmakuFormat.Ass))
        {
            Log("正在保存 ASS 弹幕文件...");
            await DanmakuUtil.SaveAsAssAsync(danmakus, danmakuAssPath, ct);
        }

        if (!ctx.Run.DownloadDanmakuFormats.Contains(DanmakuFormat.Xml) && File.Exists(danmakuXmlPath))
        {
            File.Delete(danmakuXmlPath);
        }

        // 仅有弹幕（d）而无音视频时，弹幕落盘即中止；有音视频则继续走下载流程
        if (myOption.Content.HasAny(DownloadContent.Audio | DownloadContent.Video))
        {
            return false;
        }

        MuxFinish.TryDeleteEmptyDir(pageCtx.TempDir);

        return true;
    }
}
