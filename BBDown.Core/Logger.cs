using System;
using System.Threading;

namespace BBDown.Core;

public static class Logger
{
    // 日志输出需保证「设色 → 写 → 复位」三步成原子，否则并发下载（Parallel.ForEachAsync）下颜色会错乱、文本会交错（P1-3）
    private static readonly Lock gate = new( );

    /// <summary>
    /// 每次写日志前触发。直播状态行借此先擦掉自己：否则日志会插进状态行中间，
    /// 且状态行的增量重绘会因为「已渲染文本」与实际控制台内容不符而吐出乱码。
    /// 回调内不得再调用本类的任何方法。
    /// </summary>
    public static Action? BeforeWrite { get; set; }

    public static void Log(object text, bool enter = true)
    {
        lock (gate)
        {
            BeforeWrite?.Invoke( );
            Console.Write(DateTime.Now.ToString("[HH:mm:ss]") + " - " + text);
            if (enter)
            {
                Console.WriteLine( );
            }
        }
    }

    public static void LogError(object text)
    {
        lock (gate)
        {
            BeforeWrite?.Invoke( );
            Console.Write(DateTime.Now.ToString("[HH:mm:ss]") + " - ");
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Write(text);
            Console.ResetColor( );
            Console.WriteLine( );
        }
    }

    public static void LogColor(object text, bool time = true)
    {
        lock (gate)
        {
            BeforeWrite?.Invoke( );
            if (time)
            {
                Console.Write(DateTime.Now.ToString("[HH:mm:ss]") + " - ");
            }

            Console.ForegroundColor = ConsoleColor.Cyan;
            if (time)
            {
                Console.Write(text);
            }
            else
            {
                Console.Write("            " + text);
            }

            Console.ResetColor( );
            Console.WriteLine( );
        }
    }

    public static void LogWarn(object text, bool time = true)
    {
        lock (gate)
        {
            BeforeWrite?.Invoke( );
            if (time)
            {
                Console.Write(DateTime.Now.ToString("[HH:mm:ss]") + " - ");
            }

            Console.ForegroundColor = ConsoleColor.DarkYellow;
            if (time)
            {
                Console.Write(text);
            }
            else
            {
                Console.Write("            " + text);
            }

            Console.ResetColor( );
            Console.WriteLine( );
        }
    }

    public static void LogDebug(string toFormat, params object[] args)
    {
        if (Config.DebugLog)
        {
            lock (gate)
            {
                BeforeWrite?.Invoke( );
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write(DateTime.Now.ToString("[HH:mm:ss]") + " - ");
                if (args.Length > 0)
                {
                    Console.Write(string.Format(toFormat, args).Trim( ));
                }
                else
                {
                    Console.Write(toFormat);
                }

                Console.ResetColor( );
                Console.WriteLine( );
            }
        }
    }
}
