using System;
using System.Threading;

namespace BBDown.Core;

public enum LogLevel
{
    Info,
    Error,
    Warn,
    Color,
    Debug,
}

public static class Logger
{
    // 日志输出需保证「设色 → 写 → 复位」三步成原子，否则并发下载（Parallel.ForEachAsync）下颜色会错乱、文本会交错（P1-3）
    private static readonly Lock gate = new( );

    /// <summary>
    /// 每次写日志前触发。直播状态行借此先擦掉自己：否则日志会插进状态行中间，
    /// 且状态行的增量重绘会因为「已渲染文本」与实际控制台内容不符而吐出乱码。
    /// 仅对默认控制台输出生效；回调内不得再调用本类的任何方法。
    /// </summary>
    public static Action? BeforeWrite { get; set; }

    /// <summary>
    /// 日志输出目标。null 时写控制台（含颜色与 <see cref="BeforeWrite"/> 钩子）；
    /// GUI 等无控制台宿主可替换为自定义输出（如窗口日志区），收到的参数为「级别 + 完整渲染文本（含时间戳与换行）」，需自行保证线程安全。
    /// </summary>
    public static Action<LogLevel, string>? Output { get; set; }

    public static void Log(object text, bool enter = true)
    {
        Write(LogLevel.Info, Timestamp( ), text.ToString( ) ?? "", enter);
    }

    public static void LogError(object text)
    {
        Write(LogLevel.Error, Timestamp( ), text.ToString( ) ?? "", true);
    }

    public static void LogColor(object text, bool time = true)
    {
        Write(LogLevel.Color, time ? Timestamp( ) : "            ", text.ToString( ) ?? "", true);
    }

    public static void LogWarn(object text, bool time = true)
    {
        Write(LogLevel.Warn, time ? Timestamp( ) : "            ", text.ToString( ) ?? "", true);
    }

    public static void LogDebug(string toFormat, params object[] args)
    {
        if (!Config.DebugLog)
        {
            return;
        }

        var text = args.Length > 0 ? string.Format(toFormat, args).Trim( ) : toFormat;
        Write(LogLevel.Debug, Timestamp( ), text, true);
    }

    private static string Timestamp( )
    {
        return DateTime.Now.ToString("[HH:mm:ss]") + " - ";
    }

    private static void Write(LogLevel level, string prefix, string body, bool enter)
    {
        lock (gate)
        {
            if (Output is not null)
            {
                Output(level, enter ? prefix + body + "\n" : prefix + body);
                return;
            }

            BeforeWrite?.Invoke( );
            Console.Write(prefix);
            ConsoleColor? color = level switch
            {
                LogLevel.Error => ConsoleColor.Red,
                LogLevel.Warn => ConsoleColor.DarkYellow,
                LogLevel.Color => ConsoleColor.Cyan,
                LogLevel.Debug => ConsoleColor.DarkGray,
                _ => null,
            };
            if (color is { } c)
            {
                Console.ForegroundColor = c;
            }

            Console.Write(body);
            if (color is not null)
            {
                Console.ResetColor( );
            }

            if (enter)
            {
                Console.WriteLine( );
            }
        }
    }
}
