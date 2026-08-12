using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace BBDown.DRM;

// 密钥自管：主程序不感知密钥，本插件从自身配置读取。
// 优先级：环境变量 BBDOWN_DRM_KEYS（分号/逗号分隔的 kid:key 条目）> exe 同目录 BBDown.DRM.json
internal static class KeyConfig
{
    public static DrmKeySource Load( )
    {
        var env = Environment.GetEnvironmentVariable("BBDOWN_DRM_KEYS");
        if (!string.IsNullOrWhiteSpace(env))
        {
            return new DrmKeySource(env.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        var configPath = Path.Combine(AppContext.BaseDirectory, "BBDown.DRM.json");
        if (File.Exists(configPath))
        {
            var config = JsonSerializer.Deserialize<KeyFile>(File.ReadAllText(configPath));
            if (config?.Keys is { Count: > 0 })
            {
                return new DrmKeySource(config.Keys);
            }
        }

        return new DrmKeySource([]);
    }

    // widevine 设备文件路径：环境变量 BBDOWN_WVD_PATH 优先，回落 exe 同目录 device.wvd
    public static string? FindWvdPath( )
    {
        var env = Environment.GetEnvironmentVariable("BBDOWN_WVD_PATH");
        if (!string.IsNullOrWhiteSpace(env))
        {
            return env;
        }

        var bundled = Path.Combine(AppContext.BaseDirectory, "device.wvd");
        return File.Exists(bundled) ? bundled : null;
    }
}

internal sealed class KeyFile
{
    public List<string> Keys { get; set; } = [];
}
