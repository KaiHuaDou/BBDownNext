using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace BBDown.Core.Util;

public static class ArchiveLog
{
    private static readonly Lock archiveLock = new( );

    private static Dictionary<(string Aid, string Cid), string>? archiveCache;

    // 仅在该分 P 完整成功（含混流）后写入；键为 (aid, cid)，同 aid 不同分 P 互不干扰
    public static void SaveArchive(string aid, string cid, string savePath)
    {
        lock (archiveLock)
        {
            archiveCache ??= LoadArchives( );
            archiveCache[(aid, cid)] = savePath;
            var filePath = Path.Combine(AppEnv.AppDir, "BBDown.archives");
            File.AppendAllText(filePath, $"{Environment.NewLine}{aid}\t{cid}\t{savePath}");
        }
    }

    public static bool CheckArchive(string aid, string cid)
    {
        lock (archiveLock)
        {
            archiveCache ??= LoadArchives( );
            if (archiveCache.TryGetValue((aid, cid), out var savePath))
            {
                // 产物被删/移走或记录路径为空（旧格式/损坏行）→ 无法验证产物，视为未下载重新下
                return !string.IsNullOrEmpty(savePath) && File.Exists(savePath);
            }

            return false;
        }
    }

    // 进程内一次性载入；行格式为 aid\tcid\t路径（制表符分隔，每行一条记录），无法解析的行直接跳过
    private static Dictionary<(string Aid, string Cid), string> LoadArchives( )
    {
        var dict = new Dictionary<(string, string), string>( );
        var filePath = Path.Combine(AppEnv.AppDir, "BBDown.archives");
        if (!File.Exists(filePath))
        {
            return dict;
        }

        foreach (var line in File.ReadAllLines(filePath))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var parts = line.Split('\t');
            if (parts.Length < 2)
            {
                continue;
            }

            dict[(parts[0], parts[1])] = parts.Length > 2 ? parts[2] : "";
        }

        return dict;
    }
}
