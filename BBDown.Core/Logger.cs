using System;

using BBDown.Core.Logging;

namespace BBDown.Core;

/// <summary>
/// 业务消息发射门面：只产生消息（转 MessageBus.Publish），不渲染。
/// 展示由宿主决定——CLI 控制台渲染器 / GUI 窗口日志区 / serve 事件流桥接。
/// </summary>
public static class Logger
{
    public static void Log(object text, bool enter = true)
    {
        MessageBus.Publish(LogLevel.Info, text.ToString( ) ?? "", enter: enter);
    }

    public static void LogError(object text)
    {
        MessageBus.Publish(LogLevel.Error, text.ToString( ) ?? "");
    }

    /// <summary>强调消息（CLI 渲染为高亮色），仍属 Info 级别。</summary>
    public static void LogColor(object text, bool time = true)
    {
        MessageBus.Publish(LogLevel.Info, text.ToString( ) ?? "", emphasized: true, showTime: time);
    }

    public static void LogWarn(object text, bool time = true)
    {
        MessageBus.Publish(LogLevel.Warn, text.ToString( ) ?? "", showTime: time);
    }

    public static void LogDebug(string toFormat, params object[] args)
    {
        if (!Config.DebugLog)
        {
            return;
        }

        var text = args.Length > 0 ? string.Format(toFormat, args).Trim( ) : toFormat;
        MessageBus.Publish(LogLevel.Debug, text);
    }
}
