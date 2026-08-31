using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BBDown.GUI;

/// <summary>GUI 配置：面板选项（不含 url）+ 并发数 + 窗口尺寸。</summary>
public sealed record ConfigData
{
    public TaskParams Options { get; init; } = new( );
    public int Concurrency { get; init; } = 3;
    public double? WindowLeft { get; init; }
    public double? WindowTop { get; init; }
    public double? WindowWidth { get; init; }
    public double? WindowHeight { get; init; }
}

/// <summary>ConfigData 的 JSON 源生成上下文，AOT 下替代反射序列化。</summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(ConfigData))]
public sealed partial class ConfigJsonContext : JsonSerializerContext;

/// <summary>portable 配置读写，配置文件随 exe 存放于同目录。</summary>
public static class ConfigStore
{
    private static string FilePath => Path.Combine(GuiPaths.ExeDirectory( ), "BBDown.GUI.config.json");

    /// <summary>加载配置；文件缺失或损坏返回 null（调用方回落默认值）。</summary>
    public static ConfigData? Load( )
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return null;
            }

            return JsonSerializer.Deserialize(File.ReadAllText(FilePath), ConfigJsonContext.Default.ConfigData);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>保存配置（原子写），失败抛异常由调用方记录。</summary>
    public static void Save(ConfigData config)
    {
        Directory.CreateDirectory(GuiPaths.ExeDirectory( ));
        AtomicFile.WriteAllText(FilePath, JsonSerializer.Serialize(config, ConfigJsonContext.Default.ConfigData));
    }
}
