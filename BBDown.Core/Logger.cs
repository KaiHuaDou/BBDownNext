using System;

namespace BBDown.Core;

public static class Logger
{
    // 日志输出需保证「设色 → 写 → 复位」三步成原子，否则并发下载（Parallel.ForEachAsync）下颜色会错乱、文本会交错（P1-3）
    private static readonly object _gate = new();

    public static void Log(object text, bool enter = true)
    {
        lock (_gate)
        {
            Console.Write(DateTime.Now.ToString("[HH:mm:ss]") + " - " + text);
            if (enter)
            {
                Console.WriteLine( );
            }
        }
    }

    public static void LogError(object text)
    {
        lock (_gate)
        {
            Console.Write(DateTime.Now.ToString("[HH:mm:ss]") + " - ");
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Write(text);
            Console.ResetColor( );
            Console.WriteLine( );
        }
    }

    public static void LogColor(object text, bool time = true)
    {
        lock (_gate)
        {
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
        lock (_gate)
        {
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
            lock (_gate)
            {
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
