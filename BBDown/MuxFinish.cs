using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

using BBDown.Core;
using BBDown.Core.Entity;

using static BBDown.DownloadUtil;
using static BBDown.Core.Entity.Entity;
using static BBDown.Core.Logger;
using static BBDown.Utils;

namespace BBDown;

internal static class MuxFinish
{
    internal static string ToAudioOnlyPath(string savePath)
    {
        return Path.ChangeExtension(savePath, ".m4a");
    }

    internal static void TryDeleteEmptyDir(string path)
    {
        if (Directory.Exists(path) && Directory.GetFiles(path).Length == 0)
        {
            Directory.Delete(path, true);
        }
    }

    internal static void Cleanup(PageContext pageCtx, string videoPath, string audioPath, List<Subtitle> subtitleInfo, List<AudioMaterial> audioMaterial)
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
        {
            SafeDelete(Path.Combine(Path.GetDirectoryName(trackPath) ?? "", "chapters"));
        }

        foreach (var s in subtitleInfo)
        {
            SafeDelete(s.path);
        }

        foreach (var a in audioMaterial)
        {
            SafeDelete(a.path);
            PartFile.Discard(a.path);
        }

        if (pageCtx.DeleteCoverAfterMux)
        {
            SafeDelete(pageCtx.CoverPath);
        }

        TryDeleteEmptyDir(pageCtx.TempDir);
    }
}
