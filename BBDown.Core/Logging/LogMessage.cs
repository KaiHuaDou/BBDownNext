using System;

namespace BBDown.Core.Logging;

/// <summary>
/// 业务消息级别（Debug 最低，Error 最高）。「强调」不作为级别：渲染倾向（如 CLI 的 Cyan）
/// 由宿主根据 <see cref="LogMessage.Emphasized"/> 决定。
/// </summary>
public enum LogLevel
{
    Debug,
    Info,
    Warn,
    Error,
}

/// <summary>
/// 一条业务消息：由 Core 产生，展示方式由宿主决定（CLI 控制台 / GUI 窗口日志区 / serve 事件流）。
/// Scope 为当前任务标识（serve / GUI 并发路由用），CLI 无 Scope。
/// Enter / ShowTime 是渲染细节（换行 / 时间戳），交由渲染器消费。
/// </summary>
public sealed record LogMessage(
    LogLevel Level,
    string Text,
    DateTimeOffset Time,
    bool Emphasized = false,
    string? Scope = null,
    bool Enter = true,
    bool ShowTime = true);
