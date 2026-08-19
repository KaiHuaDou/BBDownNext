using System;
using System.Threading;

using BBDown.Core.Logging;

namespace BBDown.Cli;

/// <summary>
/// 控制台消息渲染器：订阅 MessageBus 把业务消息输出到控制台（颜色 / 时间戳 / 写前擦状态行）。
/// CLI 专属展示；serve 进程可额外装配本渲染器作为运维可见输出。
/// </summary>
public sealed class ConsoleMessageRenderer : IDisposable
{
    private readonly Lock gate = new( );

    public ConsoleMessageRenderer( )
    {
        MessageBus.Subscribe(Render);
    }

    public void Dispose( )
    {
        MessageBus.Unsubscribe(Render);
    }

    private void Render(LogMessage message)
    {
        lock (gate)
        {
            // 写前擦活动状态行（进度条 / 直播状态行），让日志从行首开始
            ConsoleHost.BeforeWrite?.Invoke( );

            var prefix = message.ShowTime ? Timestamp(message.Time) : "            ";
            Console.Write(prefix);
            ConsoleColor? color = message.Level switch
            {
                LogLevel.Error => ConsoleColor.Red,
                LogLevel.Warn => ConsoleColor.DarkYellow,
                LogLevel.Debug => ConsoleColor.DarkGray,
                _ => message.Emphasized ? ConsoleColor.Cyan : null,
            };
            if (color is { } c)
            {
                Console.ForegroundColor = c;
            }

            Console.Write(message.Text);
            if (color is not null)
            {
                Console.ResetColor( );
            }

            if (message.Enter)
            {
                Console.WriteLine( );
            }
        }
    }

    // 时间戳直接取消息产生时刻（LogMessage.Time），不重取渲染时刻：消息是同步发射的，两者无差，
    // 但 Time 语义是「产生时刻」，消费方应以此为唯一时间源
    private static string Timestamp(DateTimeOffset time)
    {
        return time.ToString("[HH:mm:ss]") + " - ";
    }
}
