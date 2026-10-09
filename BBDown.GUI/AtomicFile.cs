using System.IO;
using System.Text;

namespace BBDown.GUI;

/// <summary>
/// 原子写文件：先写临时文件再同卷替换（QueueStore / ConfigStore 共用）
/// 数据经 <see cref="FileOptions.WriteThrough"/> 落盘后才替换目标，崩溃 / 断电留下的要么是旧文件要么是新文件
/// </summary>
internal static class AtomicFile
{
    public static void WriteAllText(string path, string content)
    {
        // 临时文件落在同目录保证同卷替换为原子操作；崩溃残留的 .tmp 会被下次写入覆盖，无害
        var temp = $"{path}.tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
        {
            writer.Write(content);
            // WriteThrough 只保证写入请求直达设备，缓冲区仍可能有数据；显式 flush 到盘才闭合断电窗口
            writer.Flush( );
            stream.Flush(flushToDisk: true);
        }

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
