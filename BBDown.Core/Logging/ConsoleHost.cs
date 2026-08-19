using System;

namespace BBDown.Core.Logging;

/// <summary>
/// 控制台展示基础设施：渲染器在写日志前调用 <see cref="BeforeWrite"/>（擦除活动状态行），
/// 绘制者（进度条 / 直播状态行）设置它。仅 CLI 宿主消费，GUI / serve 无控制台渲染时不消费。
/// </summary>
public static class ConsoleHost
{
    public static Action? BeforeWrite { get; set; }
}
