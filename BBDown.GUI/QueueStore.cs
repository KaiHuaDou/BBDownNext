using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BBDown.GUI;

/// <summary>待恢复任务：参数快照 + 目标。</summary>
public sealed record QueuedTask(TaskParams Options, string Url);

/// <summary>队列持久化 DTO 的 JSON 源生成上下文，AOT 下替代反射序列化。</summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(List<QueuedTask>))]
public sealed partial class QueueJsonContext : JsonSerializerContext;

/// <summary>未完成任务队列的 portable 读写，配置文件随 exe 存放于同目录。</summary>
public static class QueueStore
{
    private static string FilePath => Path.Combine(ExeDirectory( ), "BBDown.GUI.queue.json");

    /// <summary>加载待恢复队列；文件缺失或损坏返回空列表。</summary>
    public static List<QueuedTask> Load( )
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return [];
            }

            return JsonSerializer.Deserialize(File.ReadAllText(FilePath), QueueJsonContext.Default.ListQueuedTask) ?? [];
        }
        catch (Exception)
        {
            return [];
        }
    }

    /// <summary>保存待恢复队列；失败抛异常由调用方记录。</summary>
    public static void Save(IEnumerable<QueuedTask> tasks)
    {
        Directory.CreateDirectory(ExeDirectory( ));
        File.WriteAllText(FilePath, JsonSerializer.Serialize([.. tasks], QueueJsonContext.Default.ListQueuedTask));
    }

    private static string ExeDirectory( )
    {
        return Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
    }
}
