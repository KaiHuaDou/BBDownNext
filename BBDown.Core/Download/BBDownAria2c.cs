using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core.Util;

namespace BBDown.Core.Download;

public static class BBDownAria2c
{
    // 退出码含可解读语义；非零必须显式抛出，调用方据此判定下载失败而非静默继续
    internal static async Task RunAsync(string command, List<string> args, CancellationToken ct = default)
    {
        using Process p = new( );
        p.StartInfo.UseShellExecute = false;
        p.StartInfo.RedirectStandardOutput = false;
        p.StartInfo.FileName = command;
        foreach (var arg in args)
        {
            p.StartInfo.ArgumentList.Add(arg);
        }

        try
        {
            p.Start( );
        }
        catch (Win32Exception ex)
        {
            // 启动失败（未安装 / 路径错误）与退出码非零是两种独立失败，调用方统一按 InvalidOperationException 判定下载失败
            throw new InvalidOperationException($"无法启动 {command}：请确认已安装 aria2c，或用 --aria2c-path 指定路径", ex);
        }
        // 6h 进程级兜底：防 aria2c 僵死长期占住并发槽。硬超时触发时杀进程并抛 TimeoutException，
        // 与用户取消（ct 由调用方触发）区分语义
        using var hardStop = new CancellationTokenSource(TimeSpan.FromHours(6));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, hardStop.Token);
        await using var _ = linked.Token.Register(( ) =>
        {
            try { p.Kill( ); } catch { }
        });
        try
        {
            await p.WaitForExitAsync(linked.Token);
        }
        catch (OperationCanceledException) when (hardStop.IsCancellationRequested)
        {
            throw new TimeoutException("aria2c 下载超时（6h 兜底），已终止");
        }

        if (p.ExitCode != 0)
        {
            throw new InvalidOperationException($"aria2c 下载失败（退出码 {p.ExitCode}）：{DescribeExitCode(p.ExitCode)}");
        }
    }

    private static string DescribeExitCode(int code)
    {
        return code switch
        {
            1 => "未知错误",
            2 => "超时",
            3 => "资源未找到",
            4 => "网络/连接问题（如 DNS 解析失败）",
            5 => "aria2c 参数错误",
            6 => "被服务器拒绝（如 HTTP 403）",
            9 => "分块哈希校验失败",
            14 => "校验和验证失败",
            16 => "磁盘空间不足",
            18 => "下载未完成",
            _ => "未知错误",
        };
    }

    internal static List<string> BuildArgs(string url, string path, string extraArgs, string cookie, bool singleThread = false,
 int connections = 16)
    {
        List<string> args =
        [
            "--auto-file-renaming=false", "--download-result=hide", "--allow-overwrite=true", "--continue=true",
            "--console-log-level=warn", singleThread ? "-x1" : $"-x{connections}", singleThread ? "-s1" : $"-s{connections}", "-j16", "-k5M"
        ];
        if (!HTTPUtil.IsAndroidPlatformUrl(url))
        {
            args.Add($"--header=Referer: {BiliApi.Site}");
        }

        args.Add("--header=User-Agent: Mozilla/5.0");
        args.Add($"--header=Cookie: {cookie}");
        args.AddRange(SplitArgs(extraArgs));

        var dir = Path.GetDirectoryName(path);
        args.AddRange([url, "-d", string.IsNullOrEmpty(dir) ? "." : dir, "-o", Path.GetFileName(path)]);
        return args;
    }

    /// <summary>
    /// 把 --aria2c-args 的自由文本切成 argv，支持单双引号包裹含空格的片段
    /// </summary>
    internal static List<string> SplitArgs(string input)
    {
        if (input is null)
        {
            return [];
        }

        List<string> result = [];
        var current = new StringBuilder( );
        var quote = '\0';
        var quoted = false;

        foreach (var c in input)
        {
            if (quote != '\0')
            {
                if (c == quote)
                {
                    quote = '\0';
                }
                else
                {
                    current.Append(c);
                }
            }
            else if (c is '"' or '\'')
            {
                quote = c;
                quoted = true;
            }
            else if (char.IsWhiteSpace(c))
            {
                if (quoted || current.Length != 0)
                {
                    result.Add(current.ToString( ));
                    current.Clear( );
                    quoted = false;
                }
            }
            else
            {
                current.Append(c);
            }
        }

        if (quoted || current.Length != 0)
        {
            result.Add(current.ToString( ));
        }

        return result;
    }
}
