using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core;
using BBDown.Core.Util;

namespace BBDown;

internal static class BBDownAria2c
{
    public static string ARIA2C = "aria2c";

    internal static async Task<int> RunCommandCodeAsync(string command, List<string> args, CancellationToken ct = default)
    {
        using Process p = new( );
        p.StartInfo.UseShellExecute = false;
        p.StartInfo.RedirectStandardOutput = false;
        p.StartInfo.FileName = command;
        foreach (var arg in args)
        {
            p.StartInfo.ArgumentList.Add(arg);
        }

        p.Start( );
        // 取消时杀掉子进程, 避免 aria2c 在 WaitForExitAsync 已取消后仍挂起
        using var _ = ct.Register(() =>
        {
            try { p.Kill( ); } catch { }
        });
        await p.WaitForExitAsync(ct);
        return p.ExitCode;
    }

    internal static List<string> BuildArgs(string url, string path, string extraArgs, string cookie)
    {
        List<string> args =
        [
            "--auto-file-renaming=false", "--download-result=hide", "--allow-overwrite=true",
            "--console-log-level=warn", "-x16", "-s16", "-j16", "-k5M"
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
        List<string> result = [];
        var current = new StringBuilder( );
        var quote = '\0';
        var quoted = false;

        foreach (var c in input)
        {
            if (quote != '\0')
            {
                if (c == quote) quote = '\0';
                else current.Append(c);
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

        if (quoted || current.Length != 0) result.Add(current.ToString( ));
        return result;
    }
}
