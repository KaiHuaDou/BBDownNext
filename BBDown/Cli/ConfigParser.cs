using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.IO;
using System.Linq;

using BBDown.Core;

using static BBDown.Core.Logger;

namespace BBDown.Cli;

internal static class ConfigParser
{
    /// <summary>
    /// 用配置文件补齐命令行未显式指定的选项，返回待解析的完整参数表。
    /// 无配置文件或无可补项时原样返回 <paramref name="cliArgs"/>（引用相同，调用方据此跳过重新解析）。
    /// </summary>
    /// <remarks>
    /// 只能「补齐」而不能「拼接后让命令行覆盖」：System.CommandLine 对重复出现的单值选项
    /// 会在取值时抛 InvalidOperationException，而非取最后一个。
    /// </remarks>
    public static string[] MergeWithConfig(string[] cliArgs, ParseResult cliResult, RootCommand rootCommand)
    {
        ArgumentNullException.ThrowIfNull(cliResult);
        ArgumentNullException.ThrowIfNull(rootCommand);
        try
        {
            var configPath = cliResult.GetValue<string>("--config");
            if (string.IsNullOrEmpty(configPath))
            {
                configPath = Path.Combine(AppEnv.AppDir, "BBDown.config");
            }

            if (!File.Exists(configPath))
            {
                return cliArgs;
            }

            Log($"加载配置文件：{configPath}");
            var configResult = rootCommand.Parse(TokenizeConfigLines(configPath));
            var specified = cliResult.CommandResult.Children
                .OfType<OptionResult>( )
                .Where(o => !o.Implicit)
                .Select(o => o.Option.Name)
                .ToHashSet(StringComparer.Ordinal);

            List<string> extraOptions = [];
            foreach (var o in configResult.CommandResult.Children.OfType<OptionResult>( ))
            {
                if (o.Implicit || specified.Contains(o.Option.Name))
                {
                    continue;
                }

                extraOptions.Add(o.Option.Name);
                extraOptions.AddRange(o.Tokens.Select(t => t.Value));
            }

            //命令行未给出 url 时才由配置文件补齐；位置参数须排在最前，避免被开关型选项吞掉
            List<string> extraArguments = [];
            if (!cliResult.CommandResult.Children.OfType<ArgumentResult>( ).Any(a => a.Tokens.Count > 0))
            {
                extraArguments.AddRange(configResult.CommandResult.Children
                    .OfType<ArgumentResult>( )
                    .SelectMany(a => a.Tokens.Select(t => t.Value)));
            }

            return extraOptions.Count == 0 && extraArguments.Count == 0
                ? cliArgs
                : [.. extraArguments, .. cliArgs, .. extraOptions];
        }
        catch (Exception)
        {
            LogError("配置文件读取异常，忽略");
            return cliArgs;
        }
    }

    // 配置行 → argv token 的纯函数：跳过空行与 # 注释；带空格的 `-x y` 拆成两项并去引号；
    // 不带空格的 `-x` / 整行被引号包住的情况原样去引号返回
    internal static string[] TokenizeConfigLines(string configPath)
    {
        return
        [
            .. File.ReadAllLines(configPath)
                .Where(s => !string.IsNullOrWhiteSpace(s) && !s.TrimStart( ).StartsWith('#'))
                .SelectMany(s =>
                {
                    var line = s.Trim( );
                    if (!line.StartsWith('-') || !line.Contains(' '))
                    {
                        return [line.Trim('"')];
                    }

                    var spaceIndex = line.IndexOf(' ');
                    string[] paramsGroup = [line[..spaceIndex], line[spaceIndex..]];
                    return paramsGroup.Where(x => !string.IsNullOrEmpty(x)).Select(x => x.Trim(' ').Trim('"'));
                })
        ];
    }
}
