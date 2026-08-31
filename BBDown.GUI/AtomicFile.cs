using System.IO;

namespace BBDown.GUI;

/// <summary>原子写文件：先写临时文件再替换，写一半崩溃 / 断电不会损坏既有文件（QueueStore / ConfigStore 共用）。</summary>
internal static class AtomicFile
{
    public static void WriteAllText(string path, string content)
    {
        // 临时文件落在同目录保证同卷替换为原子操作；崩溃残留的 .tmp 会被下次写入覆盖，无害
        var temp = $"{path}.tmp";
        File.WriteAllText(temp, content);
        if (File.Exists(path))
        {
            File.Replace(temp, path, null);
        }
        else
        {
            File.Move(temp, path);
        }
    }
}
