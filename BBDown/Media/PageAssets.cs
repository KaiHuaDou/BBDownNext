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

        if (!myOption.NoCover && !myOption.SubOnly && !File.Exists(pageCtx.CoverPath) && !myOption.DanmakuOnly && !myOption.CoverOnly)
        {
            await DownloadFileAsync(pageCtx.CoverUrl, pageCtx.CoverPath, new DownloadConfig { Cookie = ctx.Cfg.Cookie, Aria2cPath = ctx.Tools.Aria2c }, ct);
        }

        if (myOption.NoSub || myOption.DanmakuOnly || myOption.CoverOnly)
        {
            return [];
        }

        LogDebug("获取字幕...");
        var subtitleInfo = await SubUtil.GetSubtitlesAsync(p.aid, p.cid, p.epid, p.index, myOption.UseIntlApi, ctx.Cfg, ct);
        if (!myOption.AllowAi && subtitleInfo.Count != 0)
        {
            Log("跳过下载 AI 字幕");
            subtitleInfo = [.. subtitleInfo.Where(s => !s.lan.StartsWith("ai-"))];
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
        var outSubPath = SavePath.Build(ctx, pageCtx, null, null);
        var outDir = Path.GetDirectoryName(outSubPath);
        if (!string.IsNullOrEmpty(outDir))
        {
            Directory.CreateDirectory(outDir);
        }

        outSubPath = Path.ChangeExtension(outSubPath, $".{s.lan}.srt");
        File.Move(s.path, outSubPath, true);
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
        else if (ctx.DownloadDanmakuFormats.Contains(DanmakuFormat.Ass))
        {
            Log("正在保存 ASS 弹幕文件...");
            await DanmakuUtil.SaveAsAssAsync(danmakus, danmakuAssPath, ct);
        }

        if (!ctx.DownloadDanmakuFormats.Contains(DanmakuFormat.Xml) && File.Exists(danmakuXmlPath))
        {
            File.Delete(danmakuXmlPath);
        }

        if (!myOption.DanmakuOnly)
        {
            return false;
        }

        MuxFinish.TryDeleteEmptyDir(pageCtx.TempDir);

        return true;
    }
}
