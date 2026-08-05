using System;
using System.IO;
using System.Threading;

namespace BBDown;

internal static class AppEnv
{
    // AppContext.BaseDirectory 指向入口程序集所在目录；Environment.ProcessPath 在 `dotnet BBDown.dll` 下返回宿主路径，会写错位置（P1-13）
    public static readonly string AppDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);

    // 全局取消源：Ctrl+C 时取消，令牌沿 Fetcher → Parser → HTTP → 下载 → 外部进程 全链路透传
    public static CancellationToken CancellationToken => cancelSource.Token;

    public static void Cancel( )
    {
        cancelSource.Cancel( );
    }

    private static readonly CancellationTokenSource cancelSource = new( );
}
