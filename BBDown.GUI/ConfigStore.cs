using System;
using System.IO;
using System.Text.Json;

namespace BBDown.GUI;

/// <summary>GUI 配置：面板选项（不含 url）+ BBDown.exe 路径 + 并发数 + 窗口尺寸。</summary>
public sealed record ConfigData
{
    public TaskParams Options { get; init; } = new( );
    public string ExePath { get; init; } = "";
    public int Concurrency { get; init; } = 3;
    public double? WindowLeft { get; init; }
    public double? WindowTop { get; init; }
    public double? WindowWidth { get; init; }
    public double? WindowHeight { get; init; }
}

/// <summary>portable 配置读写，配置文件随 exe 存放于同目录。</summary>
public static class ConfigStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new( ) { WriteIndented = true };

    private static string FilePath => Path.Combine(ExeDirectory( ), "BBDown.GUI.config.json");

    /// <summary>加载配置；文件缺失或损坏返回 null（调用方回落默认值）。</summary>
    public static ConfigData? Load( )
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return null;
            }

            return JsonSerializer.Deserialize<ConfigData>(File.ReadAllText(FilePath), SerializerOptions);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>保存配置，失败抛异常由调用方记录。</summary>
    public static void Save(ConfigData config)
    {
        Directory.CreateDirectory(ExeDirectory( ));
        File.WriteAllText(FilePath, JsonSerializer.Serialize(config, SerializerOptions));
    }

    private static string ExeDirectory( )
    {
        return Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
    }
}
