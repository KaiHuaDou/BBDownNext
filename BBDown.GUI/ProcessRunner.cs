using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BBDown.GUI;

/// <summary>启动 BBDown.exe 子进程，重定向 stdout / stderr，取消时 Kill 整棵进程树。</summary>
public static class ProcessRunner
{
    /// <summary>运行子进程直到退出，逐行回调输出；取消时抛 OperationCanceledException。</summary>
    public static async Task<int> RunAsync(
        string exePath, string[] args, Action<string, bool> onLine, CancellationToken token)
    {
        ProcessStartInfo startInfo = new( )
        {
            FileName = exePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            // 不显式指定编码时中文日志按系统 ANSI 解码会乱码
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using Process process = new( ) { StartInfo = startInfo };
        process.Start( );

        var outputTask = ReadLinesAsync(process.StandardOutput, line => onLine(line, false), token);
        var errorTask = ReadLinesAsync(process.StandardError, line => onLine(line, true), token);

        try
        {
            await Task.WhenAll(process.WaitForExitAsync(token), outputTask, errorTask);
        }
        catch (OperationCanceledException)
        {
            KillTree(process);
            // 取消后不传播 token，等待进程树真正退出再返回
            await process.WaitForExitAsync(CancellationToken.None);
            throw;
        }

        return process.ExitCode;
    }

    private static async Task ReadLinesAsync(StreamReader reader, Action<string> onLine, CancellationToken token)
    {
        while (await reader.ReadLineAsync(token) is { } line)
        {
            onLine(line);
        }
    }

    private static void KillTree(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // 进程已自行退出
        }
    }
}
