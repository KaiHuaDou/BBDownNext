using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core;
using BBDown.Core.Util;
using BBDown.Mux;
using BBDown.Util;

using static BBDown.Core.Entity.Entity;
using static BBDown.Core.Logger;
using static BBDown.Download.DownloadUtil;

namespace BBDown.Media;

internal static class PageAssets
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
        var subtitleInfo = await SubUtil.GetSubtitlesAsync(p.aid, p.cid, p.epid, p.index, myOption.Api == ApiType.Intl, ctx.Fetch.Cfg, ct);
        if (!myOption.Content.Has(DownloadContent.AiSubtitle) && subtitleInfo.Count != 0)
        {
            Log("跳过下载 AI 字幕");
            subtitleInfo = [.. subtitleInfo.Where(s => !s.lan.StartsWith("ai-"))];
        }

        foreach (var s in subtitleInfo)
        {
            s.path = Path.Combine(pageCtx.TempDir, Path.GetFileName(s.path));
            Log($"下载字幕 {s.lan} => {SubUtil.GetSubtitleCode(s.lan).Name}...");
            LogDebug("下载：{0}", s.url);
            await SubUtil.SaveSubtitleAsync(s.url, s.path, ctx.Fetch.Cfg, ct);
            if (File.Exists(s.path) && File.ReadAllText(s.path).Length != 0)
            {
                MoveSubtitleToOutput(s, ctx, pageCtx, !myOption.Content.Has(DownloadContent.Video));
            }
            else if (File.Exists(s.path))
            {
                File.Delete(s.path);
            }
        }

        return subtitleInfo;
    }

    private static void MoveSubtitleToOutput(Subtitle s, WorkContext ctx, PageContext pageCtx, bool audioOnly)
    {
        var outSubPath = SavePath.Build(ctx, pageCtx, null, null);
        // 内容集无 v（仅音频）时产物为 .m4a，字幕文件须与之一致，否则音视频之外的文件仍挂在 .mp4 基底
        if (audioOnly)
        {
            outSubPath = MuxFinish.ToAudioOnlyPath(outSubPath);
        }

        var outDir = Path.GetDirectoryName(outSubPath);
        if (!string.IsNullOrEmpty(outDir))
        {
            Directory.CreateDirectory(outDir);
        }

        outSubPath = Path.ChangeExtension(outSubPath, $".{s.lan}.srt");
        File.Move(s.path, outSubPath, true);
        // 移动后临时路径失效，回写输出路径供混流内嵌与收尾逻辑使用
        s.path = outSubPath;
    }

    internal static async Task<bool> DownloadDanmakuAsync(DownloadSession session, string savePath, CancellationToken ct = default)
    {
        var (myOption, ctx, pageCtx, _, downloadConfig, _) = session;
        var p = pageCtx.Page;
        var danmakuXmlPath = Path.ChangeExtension(savePath, ".xml");
        var danmakuAssPath = Path.ChangeExtension(savePath, ".ass");
        Log("正在下载 XML 弹幕文件...");
        await DownloadFileAsync($"{BiliApi.DanmakuXml}/{p.cid}.xml", danmakuXmlPath, downloadConfig, ct);
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
