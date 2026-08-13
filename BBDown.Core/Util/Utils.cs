using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using static BBDown.Core.Logger;

namespace BBDown.Core.Util;

public static partial class Utils
{
    /// <summary>
    /// 输入一堆已存在的文件，合并到新文件
    /// </summary>
    public static void CombineMultipleFilesIntoSingleFile(string[] files, string outputFilePath)
    {
        if (files.Length == 0)
        {
            return;
        }

        if (files.Length == 1)
        {
            FileInfo fi = new(files[0]);
            fi.MoveTo(outputFilePath, true);
            return;
        }

        if (!Directory.Exists(Path.GetDirectoryName(outputFilePath)))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputFilePath)!);
        }

        var inputFilePaths = files;
        using var outputStream = File.Create(outputFilePath);
        foreach (var inputFilePath in inputFilePaths)
        {
            if (inputFilePath.Length == 0)
            {
                continue;
            }

            using var inputStream = File.OpenRead(inputFilePath);
            // Buffer size can be passed as the second argument.
            inputStream.CopyTo(outputStream);
        }
    }

    /// <summary>
    /// 按 APP_DIR → PATH 的顺序查找可执行文件。刻意不搜索当前工作目录，
    /// 否则在下载目录里放一个同名程序即可劫持 ffmpeg/aria2c 调用。
    /// </summary>
    public static string? FindExecutable(params string[] names)
    {
        var fileExt = OperatingSystem.IsWindows( ) ? ".exe" : "";
        var envPath = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator) ?? [];
        return new[] { AppEnv.AppDir }.Concat(envPath)
            .Where(dir => !string.IsNullOrWhiteSpace(dir))
            .SelectMany(dir => names.Select(name => Path.Combine(dir, name + fileExt)))
            .FirstOrDefault(File.Exists);
    }

    public static string FormatFileSize(double fileSize)
    {
        return fileSize switch
        {
            < 0 => throw new ArgumentOutOfRangeException(nameof(fileSize)),
            >= 1024 * 1024 * 1024 => $"{fileSize / (1024 * 1024 * 1024):########0.00} GB",
            >= 1024 * 1024 => $"{fileSize / (1024 * 1024):####0.00} MB",
            >= 1024 => $"{fileSize / 1024:####0.00} KB",
            _ => $"{fileSize} bytes"
        };
    }

    // 采样周期 1 秒时，delta 即本周期新增字节数，也就是瞬时速度
    public static string FormatSpeed(long delta)
    {
        return $"{FormatFileSize(delta)}/s";
    }

    public static string FormatTime(int time, bool absolute = false)
    {
        var ts = TimeSpan.FromSeconds(time);
        var totalHours = (int) ts.TotalHours;
        var minutes = ts.Minutes;
        var seconds = ts.Seconds;

        if (absolute)
        {
            return $"{totalHours:D2}:{minutes:D2}:{seconds:D2}";
        }

        return totalHours == 0 ? $"{minutes:D2}m{seconds:D2}s" : $"{totalHours}h{minutes:D2}m{seconds:D2}s";
    }

    // ETA：按已完成比例线性外推剩余时间。比例过低时剩余时间发散，返回 null 表示不显示。
    public static string? FormatEta(double ratio, TimeSpan elapsed)
    {
        if (ratio <= 0.02)
        {
            return null;
        }

        var remaining = elapsed.TotalSeconds * (1 - ratio) / ratio;
        return FormatTime((int) Math.Ceiling(remaining));
    }

    /// <summary>
    /// 寻找指定目录下指定后缀的文件的详细路径 如".txt"
    /// </summary>
    public static string[] GetFiles(string dir, string ext)
    {
        List<string> al = [];
        StringBuilder sb = new( );
        DirectoryInfo d = new(dir);
        foreach (var fi in d.GetFiles( ))
        {
            if (string.Equals(fi.Extension, ext, StringComparison.OrdinalIgnoreCase))
            {
                al.Add(fi.FullName);
            }
        }

        var res = al.ToArray( );
        Array.Sort(res); //排序
        return res;
    }

    /// <summary>
    /// 获取 url 字符串参数，返回参数值字符串
    /// </summary>
    public static string GetQueryString(string name, string url)
    {
        var re = QueryRegex( );
        var mc = re.Matches(url);
        foreach (var m in mc.Cast<Match>( ))
        {
            if (m.Result("$2").Equals(name, StringComparison.Ordinal))
            {
                return m.Result("$3");
            }
        }

        return "";
    }

    /// <summary>
    /// 带重试的文件删除：Windows 上杀软/资源管理器可能短暂持锁导致 IOException，
    /// 用短暂退避重试替代 Thread.Sleep 硬等。删除失败仅记录，不抛出（避免掩盖主流程异常）。
    /// </summary>
    public static void SafeDelete(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                File.Delete(path);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt == 2) { LogDebug("删除失败（已忽略）: {0}", path); return; }

                Thread.Sleep(50 * (attempt + 1));
            }
        }
    }

    public static string FormatTimeStamp(long ts, string format)
    {
        try
        {
            return ts == 0 ? "null" : DateTimeOffset.FromUnixTimeSeconds(ts).ToLocalTime( ).ToString(format);
        }
        catch (Exception ex)
        {
            LogError($"格式化日期出错：{ex.Message}。");
            return ts.ToString( );
        }
    }

    /// <summary>运行外部程序，stderr 逐行回吐日志，取消时杀掉子进程；返回退出码。</summary>
    public static async Task<int> RunExe(string app, List<string> args, CancellationToken ct = default)
    {
        LogDebug("{0}命令: {1}", Path.GetFileNameWithoutExtension(app), FormatArgs(args));
        using Process p = new( );
        p.StartInfo.FileName = app;
        foreach (var arg in args)
        {
            p.StartInfo.ArgumentList.Add(arg);
        }

        p.StartInfo.UseShellExecute = false;
        p.StartInfo.RedirectStandardError = true;
        p.StartInfo.CreateNoWindow = true;
        p.StartInfo.StandardErrorEncoding = Encoding.UTF8;
        p.ErrorDataReceived += (sendProcess, output) =>
        {
            if (!string.IsNullOrWhiteSpace(output.Data))
            {
                Log(output.Data);
            }
        };
        p.Start( );
        p.BeginErrorReadLine( );
        await using var _ = ct.Register(( ) => { try { p.Kill( ); } catch { } });
        await p.WaitForExitAsync(ct);
        return p.ExitCode;
    }

    private static string FormatArgs(List<string> args)
    {
        return string.Join(' ', args.Select(a => a.Length == 0 || a.Contains(' ') ? $"\"{a}\"" : a));
    }

    [GeneratedRegex("(^|&)?(\\w+)=([^&]+)(&|$)?", RegexOptions.Compiled)]
    private static partial Regex QueryRegex( );
}
